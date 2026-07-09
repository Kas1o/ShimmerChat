using ShimmerChatLib.Generation;
using ShimmerChatBuiltin.Memory;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 构造 MemoryTool（需要 Qdrant 配置 + SharedState），加入 Tools 列表
    /// </summary>
    [NodeInfo("Memory Tool", Icon = "🧠", Color = "#e0c060", Category = "Tool/Memory")]
    public class MemoryToolNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Memory Tool";

        public Task ExecuteAsync(NodeExecutionContext context)
        {
            var kvData = context.Env.Persistent.KVData;
            var sharedState = context.Env.Transient.SharedState;
            var agentGuid = context.Env.Persistent.AgentGuid;

            var tool = new MemoryToolV2(kvData, sharedState, agentGuid);
            context.Env.Transient.Tools.Add(tool);

            return Task.CompletedTask;
        }
    }
}
