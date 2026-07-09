using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 向 TransientEnv.Fragments 注入 ContextSegment
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

        /// <summary>
        /// 插入位置（默认 -1 表示末尾）
        /// </summary>
        [NodeProperty("Insert At", Hint = "Insert position (-1 = end)")]
        public int InsertAt { get; set; } = -1;

        /// <summary>
        /// 是否覆盖同角色的首个片段
        /// </summary>
        [NodeProperty("Overwrite First", Hint = "Replace first fragment of the same role")]
        public bool OverwriteFirst { get; set; } = false;

        public Task ExecuteAsync(NodeExecutionContext context)
        {
            var segment = new ContextSegment
            {
                SourceType = typeof(FragmentNode),
                Message = new ChatMessage { Content = Content },
                From = From
            };

            var fragments = context.Env.Transient.Fragments;

            if (OverwriteFirst)
            {
                var existing = fragments.FirstOrDefault(f => f.From == From);
                if (existing != null)
                {
                    existing.Message.Content = Content;
                    return Task.CompletedTask;
                }
            }

            if (InsertAt < 0 || InsertAt >= fragments.Count)
                fragments.Add(segment);
            else
                fragments.Insert(InsertAt, segment);

            return Task.CompletedTask;
        }
    }
}
