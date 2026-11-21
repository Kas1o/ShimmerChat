using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.DynPrompt
{
    /// <summary>
    /// DynPrompt 动态提示词注入节点。
    /// 从 KVData 中读取 DynPromptSet，评估触发规则，将匹配的提示词注入到 TransientEnv.Fragments。
    /// </summary>
    [NodeInfo("node.dyn_prompt", Icon = "📝", Color = "var(--node-prompt)", CategoryKeys = ["category.content", "category.fragment"], DescriptionKey = "node.dyn_prompt.desc")]
    public class DynPromptNode : IPreGenerationNode
    {

        /// <summary>
        /// 指定要使用的 DynPromptSet 名称。留空则使用所有集合。
        /// </summary>
        [NodeProperty("prop.dyn_prompt.set_name", HintKey = "prop.dyn_prompt.set_name.hint", Order = 1,
            OptionsKey = "dyn_prompt_sets", OptionsSearchable = true, ShowOptionDescription = true,
            OptionsDefaultLabelKey = "prop.dyn_prompt.set_name.all")]
        public string SetName { get; set; } = "";

        /// <summary>
        /// 注入片段的角色
        /// </summary>
        [NodeProperty("prop.dyn_prompt.role", HintKey = "prop.dyn_prompt.role.hint", Order = 2)]
        public PromptBuilder.From From { get; set; } = PromptBuilder.From.system;

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;
            var kvData = context.Env.Persistent.KVData;

            var json = kvData.Read("DynPrompt", "DynPromptSets");
            if (string.IsNullOrEmpty(json))
                return Task.FromResult(NodeResult.Failure(NodeErrorCodes.PresetNotFound,$"No DynPromptSet Exists."));

            List<DynPromptSet>? allSets;
            try
            {
                allSets = JsonConvert.DeserializeObject<List<DynPromptSet>>(json);
            }
            catch (Exception ex)
            {
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ParseError,
                    loc["node_err.dynprompt_parse_failed"],
                    details: ex.ToString()));
            }

            if (allSets == null || allSets.Count == 0)
                return Task.FromResult(NodeResult.SuccessResult());

            // 筛选目标集合
            var targetSets = string.IsNullOrWhiteSpace(SetName)
                ? allSets
                : allSets.Where(s => s.Name == SetName).ToList();

            if (targetSets.Count == 0)
                return Task.FromResult(NodeResult.SuccessResult());

            // 构建评估上下文：拼接所有 fragment 内容
            var fragments = context.Env.Transient.Fragments;
            var evalContext = string.Join("\n", fragments
                .Where(f => !string.IsNullOrEmpty(f.Message.Content))
                .Select(f => f.Message.Content));

            // 收集匹配的 term
            var matchedTerms = new List<DynPromptTerm>();
            foreach (var set in targetSets)
            {
                foreach (var term in set.Terms)
                {
                    try
                    {
                        if (DynPromptEvaluator.Evaluate(term.TriggerRule, evalContext))
                        {
                            matchedTerms.Add(term);
                        }
                    }
                    catch (Exception ex)
                    {
                        return Task.FromResult(NodeResult.Failure(
                            NodeErrorCodes.ParseError,
                            loc.Format("node_err.dynprompt_trigger_eval_failed", term.Name),
                            details: ex.ToString()));
                    }
                }
            }

            if (matchedTerms.Count == 0)
                return Task.FromResult(NodeResult.SuccessResult());

            // 注入槽模式要求“有且仅有一个”槽位：先做结构校验再写入，
            // 避免前面的词条已经改动上下文后才发现槽位缺失/重复。
            ContextSegment? slot = null;
            var slotTerm = matchedTerms.FirstOrDefault(t => t.InjectionMode == DynPromptTermInjectionMode.InjectionSlot);
            if (slotTerm != null)
            {
                var slots = fragments.Where(DynPromptSlot.IsSlot).ToList();
                if (slots.Count == 0)
                {
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.InjectionSlotMissing,
                        loc.Format("node_err.dynprompt_slot_missing", slotTerm.Name),
                        details: $"Term '{slotTerm.Name}' uses InjectionSlot, but no fragment carries " +
                                 $"Metadata['{DynPromptSlot.MetadataKey}']."));
                }

                if (slots.Count > 1)
                {
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.InjectionSlotAmbiguous,
                        loc.Format("node_err.dynprompt_slot_ambiguous", slotTerm.Name, slots.Count),
                        details: $"{slots.Count} fragments carry Metadata['{DynPromptSlot.MetadataKey}']; " +
                                 "an injection slot must be unique."));
                }

                slot = slots[0];
            }

            // 按注入模式处理：BeforeSystem/AfterSystem 合并到已有 system fragment 内容中，AtDepth 追加到末尾
            foreach (var term in matchedTerms)
            {
                switch (term.InjectionMode)
                {
                    case DynPromptTermInjectionMode.BeforeSystem:
                        MergeIntoSystemFragment(fragments, term.Content, prepend: true);
                        break;

                    case DynPromptTermInjectionMode.AfterSystem:
                        MergeIntoSystemFragment(fragments, term.Content, prepend: false);
                        break;

                    case DynPromptTermInjectionMode.AtDepth:
                        {
                            var segment = new ContextSegment
                            {
                                SourceType = typeof(DynPromptNode),
                                Message = new ChatMessage { Content = term.Content },
                                From = From
                            };
                            if (term.InjectionDepth < 0 || term.InjectionDepth >= fragments.Count)
                                fragments.Add(segment);
                            else
                                fragments.Insert(term.InjectionDepth, segment);
                        }
                        break;

                    case DynPromptTermInjectionMode.InjectionSlot:
                        // 槽位已在上面校验为唯一；角色沿用槽位节点，不在这里改 From。
                        DynPromptSlot.AppendContent(slot!, term.Content);
                        break;
                }
            }

            return Task.FromResult(NodeResult.SuccessResult());
        }

        private static void MergeIntoSystemFragment(List<ContextSegment> fragments, string content, bool prepend)
        {
            var existing = fragments.FirstOrDefault(f => f.From == PromptBuilder.From.system);
            if (existing != null)
            {
                existing.Message.Content = prepend
                    ? content + "\n" + existing.Message.Content
                    : existing.Message.Content + "\n" + content;
            }
            else
            {
                fragments.Insert(0, new ContextSegment
                {
                    SourceType = typeof(DynPromptNode),
                    Message = new ChatMessage { Content = content },
                    From = PromptBuilder.From.system
                });
            }
        }
    }
}
