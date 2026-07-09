using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 通过 Activator.CreateInstance 实例化无参构造的 IToolV2 并加入 Tools 列表。
    /// 只处理无参构造的工具。
    /// </summary>
    [NodeInfo("Instantiate Tool", Icon = "⚙", Color = "#70c070", Category = "Tool")]
    public class ToolInstantiateNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Instantiate Tool";

        /// <summary>
        /// IToolV2 实现的完整类型名
        /// </summary>
        public string ToolTypeName { get; set; } = "";

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(ToolTypeName))
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ToolNotFound,
                    "ToolInstantiate: ToolTypeName is empty.",
                    nodeId: Id, nodeName: Name));

            var toolType = Type.GetType(ToolTypeName);
            if (toolType == null || !typeof(IToolV2).IsAssignableFrom(toolType))
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ToolNotFound,
                    $"ToolInstantiate: Type '{ToolTypeName}' not found or is not an IToolV2.",
                    nodeId: Id, nodeName: Name));

            var ctor = toolType.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ToolNotFound,
                    $"ToolInstantiate: Type '{ToolTypeName}' has no parameterless constructor.",
                    nodeId: Id, nodeName: Name));

            try
            {
                var tool = (IToolV2)Activator.CreateInstance(toolType)!;
                context.Env.Transient.Tools.Add(tool);
                return Task.FromResult(NodeResult.SuccessResult());
            }
            catch (Exception ex)
            {
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ToolNotFound,
                    $"ToolInstantiate: Failed to create instance of '{ToolTypeName}'.",
                    details: ex.ToString(),
                    nodeId: Id, nodeName: Name));
            }
        }
    }
}
