using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatBuiltin.DynPrompt;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// DynPrompt 动态提示词注入节点。
    /// 从 KVData 中读取 DynPromptSet，评估触发规则，将匹配的提示词注入到 TransientEnv.Fragments。
    /// </summary>
    [NodeInfo("DynPrompt", Icon = "📝", Color = "#b080d0", Category = "Content/Fragment", Description = "Evaluate DynPrompt trigger rules and inject matching prompts")]
    public class DynPromptNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "DynPrompt";

        /// <summary>
        /// 指定要使用的 DynPromptSet 名称。留空则使用所有集合。
        /// </summary>
        [NodeProperty("Set Name", Hint = "Name of the DynPromptSet to use. Leave empty to use all sets.")]
        public string SetName { get; set; } = "";

        /// <summary>
        /// 注入片段的角色
        /// </summary>
        [NodeProperty("Role", Hint = "Role for injected fragments (system / user / assistant)")]
        public PromptBuilder.From From { get; set; } = PromptBuilder.From.system;

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var kvData = context.Env.Persistent.KVData;

            var json = kvData.Read("DynPrompt", "DynPromptSets");
            if (string.IsNullOrEmpty(json))
                return Task.FromResult(NodeResult.SuccessResult());

            List<DynPromptSet>? allSets;
            try
            {
                allSets = JsonConvert.DeserializeObject<List<DynPromptSet>>(json);
            }
            catch (Exception ex)
            {
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ParseError,
                    "DynPrompt: Failed to parse DynPromptSets from KVData.",
                    details: ex.ToString(),
                    nodeId: Id, nodeName: Name));
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
                            $"DynPrompt: Failed to evaluate trigger rule for term '{term.Name}'.",
                            details: ex.ToString(),
                            nodeId: Id, nodeName: Name));
                    }
                }
            }

            if (matchedTerms.Count == 0)
                return Task.FromResult(NodeResult.SuccessResult());

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
