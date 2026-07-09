using ShimmerChatLib.Generation;
using ShimmerChatBuiltin.Variable;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 构造 VariableTool（需要 ChatGuid + AgentGuid），加入 Tools 列表
    /// </summary>
    [NodeInfo("Variable Tool", Icon = "📋", Color = "#60c0c0", Category = "Tool/Variables")]
    public class VariableToolNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Variable Tool";

        public Task ExecuteAsync(NodeExecutionContext context)
        {
            var kvData = context.Env.Persistent.KVData;
            var chatGuid = context.Env.Persistent.ChatGuid;
            var agentGuid = context.Env.Persistent.AgentGuid;

            var tool = new VariableToolV2(kvData, chatGuid, agentGuid);
            context.Env.Transient.Tools.Add(tool);

            return Task.CompletedTask;
        }
    }
}
