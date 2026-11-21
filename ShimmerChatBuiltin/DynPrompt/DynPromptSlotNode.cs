using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.DynPrompt
{
    /// <summary>
    /// 在 TransientEnv.Fragments 中插入一个未填充的注入槽：空内容消息 + 元数据标记。
    /// 位置由节点在树中的顺序决定，DynPrompt 的 InjectionSlot 模式只向该标记位置写入，
    /// 因此不再依赖“第一个 system 片段”或固定下标。
    /// </summary>
    [NodeInfo("node.dyn_prompt_slot", Icon = "🎯", Color = "var(--node-prompt)", CategoryKeys = ["category.content", "category.fragment"], DescriptionKey = "node.dyn_prompt_slot.desc")]
    public class DynPromptSlotNode : IPreGenerationNode
    {
        /// <summary>
        /// 注入内容使用的角色。DynPrompt 的词条注入到槽位时沿用这里的角色。
        /// </summary>
        [NodeProperty("prop.dyn_prompt_slot.role", HintKey = "prop.dyn_prompt_slot.role.hint", Order = 0)]
        public PromptBuilder.From From { get; set; } = PromptBuilder.From.system;

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            // tool_result 必须带 tool_call_id，而注入槽没有可回答的调用；与其让端点拒收请求，不如就地报错。
            if (From == PromptBuilder.From.tool_result)
            {
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ParameterError,
                    context.Env.Persistent.LocService["node_err.dynprompt_slot_tool_result_role"],
                    details: "DynPromptSlotNode does not support PromptBuilder.From.tool_result: " +
                             "an injection slot has no tool_call id to answer."));
            }

            context.Env.Transient.Fragments.Add(new ContextSegment
            {
                SourceType = typeof(DynPromptSlotNode),
                Message = new ChatMessage { Content = string.Empty },
                From = From,
                Metadata = new Dictionary<string, object>
                {
                    [DynPromptSlot.MetadataKey] = true
                }
            });

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
