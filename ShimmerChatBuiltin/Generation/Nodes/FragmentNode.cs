using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 向 TransientEnv.Fragments 末尾追加 ContextSegment
    /// </summary>
    [NodeInfo("node.fragment", Icon = "▤", Color = "#60b0e0", CategoryKeys = ["category.content", "category.fragment"])]
    public class FragmentNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Fragment";

        /// <summary>
        /// 片段内容
        /// </summary>
        [NodeProperty("prop.fragment.content", HintKey = "prop.fragment.content.hint")]
        public string Content { get; set; } = "";

        /// <summary>
        /// 片段角色 (system / user / assistant)
        /// </summary>
        [NodeProperty("prop.fragment.role", HintKey = "prop.fragment.role.hint")]
        public PromptBuilder.From From { get; set; } = PromptBuilder.From.system;

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
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
