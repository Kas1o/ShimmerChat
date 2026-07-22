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
        /// 当前 Agent 对象。
        /// </summary>
        public required Agent Agent { get; init; }

        /// <summary>从 Chat 实例派生</summary>
        public Guid ChatGuid => Chat.Guid;

        /// <summary>从 Agent 实例派生</summary>
        public Guid AgentGuid => Agent.Guid;
    }
}
