using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 持久化环境，提供对话/Agent 级别的持久数据
    /// </summary>
    public class PersistentEnv
    {
        public required IKVDataService KVData { get; init; }
        public required IToolRegistry ToolRegistry { get; init; }
        public required IPreGenerationNodeSerializer Serializer { get; init; }
        public required ILocService LocService { get; init; }
        public required IDebugOutputService DebugOutput { get; init; }
        /// <summary>
        /// 后生成管线管理器，用于对 LLM 响应进行后处理变换。
        /// 节点可通过此字段在执行完 ToolCallLoop 后调用 Post-Generation 管线。
        /// </summary>
        public IPostGenerationManagerService? PostGenerationManager { get; init; }

        /// <summary>
        /// 当前对话对象。与 UI 层共享同一实例，修改即生效。
        /// </summary>
        public required Chat Chat { get; init; }

        /// <summary>
        /// 消息持久化服务。节点直接操作 <see cref="Chat"/> 时用它在写库；
        /// null 表示当前会话不落库（如 SubAgent 的虚拟会话），节点只应改内存。
        /// </summary>
        public IMessageStoreService? MessageStore { get; init; }

        /// <summary>
        /// 当前 Agent 对象。
        /// </summary>
        public required Agent Agent { get; init; }

        /// <summary>
        /// 图像附件解析服务。消息里的图像附件只有经过它才能变成请求可用的 Base64
        /// （SharperLLM 侧不读文件），因此注入历史消息的节点依赖此字段。
        /// </summary>
        public IImageAttachmentService? ImageAttachments { get; init; }

        /// <summary>
        /// 工具 ↔ 用户 的交互通道。为 null 表示当前上下文没有可展示交互的界面
        /// （如子代理的虚拟会话），需要用户输入的工具必须据此明确失败而不是一直等下去。
        /// </summary>
        public IToolInteractionService? Interactions { get; init; }

        /// <summary>
        /// 嵌入式向量存储。为 null 表示当前宿主未提供，需要向量的节点/工具必须明确失败。
        /// </summary>
        public IVectorStoreService? VectorStore { get; init; }

        /// <summary>
        /// 生成作用域的资源袋。节点用它登记需要在本次生成结束时释放的资源
        /// （如 MCP 子进程连接、HTTP 会话）。
        /// 由 <c>GenerationManagerV2</c> 在生成结束时统一释放。
        /// 同一实例会在一次生成内被多次复用（Tool Call 循环重建 TransientEnv 时不会重建 PersistentEnv）。
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public GenerationResourceBag Resources { get; } = new();

        /// <summary>从 Chat 实例派生</summary>
        public Guid ChatGuid => Chat.Guid;

        /// <summary>
        /// 宿主服务提供者（可选）。节点需要宿主级服务（如 ILoggerFactory）时使用。
        /// 事件型生成等场景可能为 null，节点必须容忍 null 并降级为无日志运行。
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public IServiceProvider? Services { get; init; }

        /// <summary>从 Agent 实例派生</summary>
        public Guid AgentGuid => Agent.Guid;
    }
}
