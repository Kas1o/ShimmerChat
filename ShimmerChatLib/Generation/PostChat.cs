using SharperLLM.Util;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 后处理节点操作对话的薄封装：只负责落库与顺序号维护，不限制自由度。
    /// 节点仍可绕过它直接操作 <see cref="PostGenerationEnv.Chat"/>，
    /// 但那样需要自己调用 <see cref="Chat.SaveMessage"/> 并保证 <see cref="Message.Order"/> 与集合索引一致。
    /// </summary>
    public static class PostChat
    {
        /// <summary>
        /// 追加一条消息到对话末尾，并落库（虚拟会话只改内存）。
        /// <paramref name="images"/> 会在写入存储前挂到消息上，保证图像引用与消息原子落库。
        /// </summary>
        public static Message Append(
            PostGenerationEnv env,
            ChatMessage message,
            string sender,
            IReadOnlyList<MessageImage>? images = null)
        {
            var msg = new Message
            {
                message = message,
                sender = sender,
                timestamp = DateTime.Now,
                Images = images is { Count: > 0 } ? [.. images] : null
            };

            env.Chat.AddMessage(msg);
            if (env.MessageStore is { } store)
                env.Chat.SaveMessage(store, msg);
            env.ChatMutated = true;
            return msg;
        }

        /// <summary>
        /// 在 anchor 之后插入一条消息。顺序号由 Chat 统一重排并全量持久化，
        /// 因此这里不依赖 timestamp 决定位置。
        /// </summary>
        public static Message InsertAfter(PostGenerationEnv env, Message anchor, ChatMessage message, string sender)
        {
            var chat = env.Chat;
            var index = chat.Messages.IndexOf(anchor);
            if (index < 0)
                throw new InvalidOperationException(
                    $"Anchor message '{anchor.Id}' is not in chat '{chat.Guid}'.");

            var msg = new Message
            {
                message = message,
                sender = sender,
                timestamp = DateTime.Now
            };

            if (env.MessageStore is { } store)
            {
                chat.InsertMessage(store, index + 1, msg);
            }
            else
            {
                // 虚拟会话：只改内存，顺序号仍需正确，否则宿主的一致性校验会报警
                chat.Messages.Insert(index + 1, msg);
                chat.NormalizeOrder();
            }

            env.ChatMutated = true;
            return msg;
        }

        /// <summary>改写目标消息的当前版本内容，并落库。</summary>
        public static void Replace(PostGenerationEnv env, Message target, ChatMessage newVersion)
        {
            target.message = newVersion;
            if (env.MessageStore is { } store)
                env.Chat.SaveMessage(store, target);
            env.ChatMutated = true;
        }

        /// <summary>从对话与存储中删除目标消息。</summary>
        public static void Remove(PostGenerationEnv env, Message target)
        {
            if (env.MessageStore is { } store)
            {
                env.Chat.DeleteMessage(store, target);
            }
            else
            {
                env.Chat.Messages.Remove(target);
                env.Chat.NormalizeOrder();
            }

            env.ChatMutated = true;
        }
    }
}
