using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 通过 IAutoCreateToolV2.Create(PersistentEnv) 实例化工具并加入 Tools 列表。
    /// 只处理实现了 IAutoCreateToolV2 的类型。
    /// </summary>
    [NodeInfo("node.instantiate_tool", Icon = "⚙", Color = "var(--node-tool)", CategoryKeys = ["category.tool"])]
    [NodeEditor(typeof(ToolInstantiateNodeEditor))]
    public class ToolInstantiateNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Instantiate Tool";

        /// <summary>
        /// IAutoCreateToolV2 实现的完整类型名
        /// </summary>
        public string ToolTypeName { get; set; } = "";

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;

            if (string.IsNullOrWhiteSpace(ToolTypeName))
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ToolNotFound,
                    loc["node_err.tool_instantiate_empty"],
                    nodeId: Id, nodeName: Name));

            try
            {
                var tool = context.Env.Persistent.ToolRegistry.CreateInstance(ToolTypeName, context.Env.Persistent);
                if (tool == null)
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.ToolNotFound,
                        loc.Format("node_err.tool_instantiate_type_not_found", ToolTypeName),
                        nodeId: Id, nodeName: Name));

                context.Env.Transient.Tools.Add(tool);
                return Task.FromResult(NodeResult.SuccessResult());
            }
            catch (Exception ex)
            {
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ToolNotFound,
                    loc.Format("node_err.tool_instantiate_create_failed", ToolTypeName),
                    details: ex.ToString(),
                    nodeId: Id, nodeName: Name));
            }
        }
    }
}
