using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PostGeneration
{
    public enum PostMessageMode
    {
        /// <summary>作为新消息追加到对话末尾（下一轮可见，也会出现在聊天记录里）。</summary>
        Append,
        /// <summary>覆盖本轮 assistant 响应文本。</summary>
        ReplaceResponse
    }

    public enum PostMessageSender
    {
        User,
        System,
        Assistant,
        ToolResult
    }

    /// <summary>
    /// 向对话追加一条消息，或覆盖本轮响应文本。
    /// 追加的消息会立即落库，因此下一轮前生成管线能读到它。
    /// </summary>
    [NodeInfo("node.post_message",
        Icon = "✉",
        Color = "var(--node-prompt)",
        CategoryKeys = ["category.post"],
        DescriptionKey = "node.post_message.desc")]
    public class PostMessageNode : IPostGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Append Message";

        [NodeProperty("prop.post_message.mode", HintKey = "prop.post_message.mode.hint", Order = 10)]
        public PostMessageMode Mode { get; set; } = PostMessageMode.Append;

        [NodeProperty("prop.post_message.sender", HintKey = "prop.post_message.sender.hint", Order = 20)]
        public PostMessageSender Sender { get; set; } = PostMessageSender.User;

        [NodeProperty("prop.post_message.content",
            HintKey = "prop.post_message.content.hint", MultiLine = true, Order = 30)]
        public string Content { get; set; } = "";

        public Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            var env = context.Env;

            switch (Mode)
            {
                case PostMessageMode.ReplaceResponse:
                    env.ResponseText = Content;
                    break;

                case PostMessageMode.Append:
                    PostChat.Append(env, new ChatMessage { Content = Content }, ResolveSender());
                    break;

                default:
                    var fail = PostNodeResult.Failure(NodeErrorCodes.ParameterError,
                        $"PostMessageNode: unsupported mode '{Mode}'.");
                    fail.NodeId = Id;
                    fail.NodeName = Name;
                    return Task.FromResult(fail);
            }

            return Task.FromResult(PostNodeResult.SuccessResult());
        }

        private string ResolveSender() => Sender switch
        {
            PostMessageSender.System => ShimmerChatLib.Sender.System,
            PostMessageSender.Assistant => ShimmerChatLib.Sender.AI,
            PostMessageSender.ToolResult => ShimmerChatLib.Sender.ToolResult,
            _ => ShimmerChatLib.Sender.User
        };
    }
}
