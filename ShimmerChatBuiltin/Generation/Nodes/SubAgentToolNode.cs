using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 构造 SubAgentToolV2 并加入 Tools 列表
    /// </summary>
    [NodeInfo("SubAgent Tool", Icon = "🔧", Color = "#e060a0", Category = "Tool/SubAgent")]
    public class SubAgentToolNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "SubAgent Tool";

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var kvData = context.Env.Persistent.KVData;
            var api = context.Env.Transient.API;
            var tools = context.Env.Transient.Tools;

            var subAgentTool = new SubAgent.SubAgentToolV2(kvData, api, tools);
            context.Env.Transient.Tools.Add(subAgentTool);

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
