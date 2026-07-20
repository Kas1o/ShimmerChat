using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Fragments
{
    /// <summary>
    /// 向 TransientEnv.Fragments 末尾追加 ContextSegment
    /// </summary>
    [NodeInfo("node.fragment", Icon = "▤", Color = "var(--node-fragment)", CategoryKeys = ["category.content", "category.fragment"])]
    public class FragmentNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Fragment";

        /// <summary>
        /// 片段内容
        /// </summary>
        [NodeProperty("prop.fragment.content", HintKey = "prop.fragment.content.hint", MultiLine = true)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 片段角色 (system / user / assistant)
        /// </summary>
        [NodeProperty("prop.fragment.role", HintKey = "prop.fragment.role.hint")]
        public PromptBuilder.From From { get; set; } = PromptBuilder.From.system;

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            context.Env.Transient.Fragments.Add(new ContextSegment
            {
                SourceType = typeof(FragmentNode),
                Message = new ChatMessage { Content = Content },
                From = From
            });

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
