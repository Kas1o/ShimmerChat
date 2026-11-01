using SharperLLM.Util;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 后处理节点对本轮之后的循环控制意图。整棵树执行完毕后由宿主统一应用。
    /// </summary>
    public enum PostLoopDirective
    {
        /// <summary>本轮结束后结束生成（默认）。</summary>
        Stop = 0,

        /// <summary>继续下一轮：重建前生成环境后再次请求模型。</summary>
        Continue = 1
    }

    /// <summary>
    /// 后生成管线环境。
    /// <para>
    /// 节点可直接读写 <see cref="Chat"/>，并通过 <see cref="MessageStore"/> 落库
    /// （为 null 表示虚拟会话，如 SubAgent，此时只能改内存）。
    /// 是否进入下一轮由 <see cref="LoopDirective"/> 决定。
    /// </para>
    /// <para>
    /// <b>本轮输出的权威来源是 <see cref="ResponseMessage"/>/<see cref="ResponseText"/></b>：
    /// 宿主在提交阶段会用它覆盖 <see cref="CurrentMessage"/> 的当前版本。
    /// <see cref="CurrentMessage"/> 用于定位本轮消息、删除它，或作为插入锚点。
    /// </para>
    /// </summary>
    public class PostGenerationEnv
    {
        /// <summary>LLM 响应文本（节点可修改，提交时成为本轮消息内容）。</summary>
        public string ResponseText { get; set; }

        /// <summary>LLM 完整响应消息（含 tool calls、thinking 等）。用 <see cref="SetResponse"/> 整体替换。</summary>
        public ChatMessage ResponseMessage { get; private set; }

        /// <summary>前生成管线构建的 Fragments（只读参考）</summary>
        public IReadOnlyList<ContextSegment> PreFragments { get; }

        /// <summary>节点间共享状态。由宿主跨轮复用，因此可累积循环级状态。</summary>
        public Dictionary<string, object> SharedState { get; }

        /// <summary>持久化服务访问</summary>
        public PersistentEnv Persistent { get; }

        /// <summary>当前管线的序列化器（CallNode 加载预设时使用）</summary>
        public ITreeNodeSerializer Serializer { get; set; } = default!;

        /// <summary>本轮序号（0-based）。副作用节点可据此只在首轮执行。</summary>
        public int RoundIndex { get; init; }

        /// <summary>本轮 assistant 的 Message 活对象（已在 Chat.Messages 中）。可能为 null。</summary>
        public Message? CurrentMessage { get; init; }

        /// <summary>本轮可用工具（由前生成管线产出）。</summary>
        public IReadOnlyList<IToolV2> Tools { get; init; } = Array.Empty<IToolV2>();

        /// <summary>循环控制意图。默认 Stop。</summary>
        public PostLoopDirective LoopDirective { get; set; } = PostLoopDirective.Stop;

        /// <summary>本轮 toolCalls 是否已被节点接管。宿主用它发现"有工具调用却没人处理"的配置错误。</summary>
        public bool ToolCallsHandled { get; set; }

        /// <summary>节点是否改动过 Chat。宿主据此决定滚动与刷新。</summary>
        public bool ChatMutated { get; set; }

        /// <summary>会话对象（= <see cref="PersistentEnv.Chat"/>）。节点直接在此增删改消息。</summary>
        public Chat Chat => Persistent.Chat;

        /// <summary>消息持久化服务；null 表示虚拟会话，不落库。</summary>
        public IMessageStoreService? MessageStore => Persistent.MessageStore;

        public PostGenerationEnv(
            ChatMessage responseMessage,
            IReadOnlyList<ContextSegment> preFragments,
            PersistentEnv persistent,
            IReadOnlyList<IToolV2>? tools = null,
            Message? currentMessage = null,
            int roundIndex = 0,
            Dictionary<string, object>? sharedState = null)
        {
            ResponseMessage = responseMessage;
            ResponseText = responseMessage.Content ?? "";
            PreFragments = preFragments;
            Persistent = persistent;
            Tools = tools ?? Array.Empty<IToolV2>();
            CurrentMessage = currentMessage;
            RoundIndex = roundIndex;
            SharedState = sharedState ?? new Dictionary<string, object>();
        }

        /// <summary>整体替换本轮响应消息（含 toolCalls / thinking）。</summary>
        public void SetResponse(ChatMessage message)
        {
            ResponseMessage = message;
            ResponseText = message.Content ?? "";
        }
    }
}
