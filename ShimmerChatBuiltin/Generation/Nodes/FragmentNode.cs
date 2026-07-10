using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 向 TransientEnv.Fragments 末尾追加 ContextSegment
    /// </summary>
    [NodeInfo("Fragment", Icon = "▤", Color = "#60b0e0", Category = "Content/Fragment")]
    public class FragmentNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Fragment";

        /// <summary>
        /// 片段内容
        /// </summary>
        [NodeProperty("Content", Hint = "The text content to inject into the context")]
        public string Content { get; set; } = "";

        /// <summary>
        /// 片段角色 (system / user / assistant)
        /// </summary>
        [NodeProperty("Role", Hint = "system / user / assistant")]
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
