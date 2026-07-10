using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 持久化环境，提供对话/Agent 级别的持久数据
    /// </summary>
    public class PersistentEnv
    {
        public required IKVDataService KVData { get; init; }
        public required Guid ChatGuid { get; init; }
        public required Guid AgentGuid { get; init; }
        public required IToolRegistry ToolRegistry { get; init; }

        /// <summary>
        /// 获取当前对话对象（惰性加载）
        /// </summary>
        public Chat GetChat() => Chat.Load(ChatGuid, KVData);

        /// <summary>
        /// 获取当前 Agent 对象（惰性加载）
        /// </summary>
        public Agent GetAgent() => Agent.Load(AgentGuid, KVData);
    }
}
