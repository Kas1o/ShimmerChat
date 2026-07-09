using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 通过 IAutoCreateToolV2.Create(PersistentEnv) 实例化工具并加入 Tools 列表。
    /// 只处理实现了 IAutoCreateToolV2 的类型。
    /// </summary>
    [NodeInfo("Instantiate Tool", Icon = "⚙", Color = "#70c070", Category = "Tool")]
    [NodeEditor("ShimmerChat.Components.SubComponents.ToolInstantiateNodeEditor")]
    public class ToolInstantiateNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Instantiate Tool";

        /// <summary>
        /// IAutoCreateToolV2 实现的完整类型名
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
            if (toolType == null || !typeof(IAutoCreateToolV2).IsAssignableFrom(toolType))
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ToolNotFound,
                    $"ToolInstantiate: Type '{ToolTypeName}' not found or does not implement IAutoCreateToolV2.",
                    nodeId: Id, nodeName: Name));

            try
            {
                var createMethod = toolType.GetMethod("Create",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (createMethod == null)
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.ToolNotFound,
                        $"ToolInstantiate: Type '{ToolTypeName}' has no static Create method.",
                        nodeId: Id, nodeName: Name));

                var tool = (IToolV2)createMethod.Invoke(null, [context.Env.Persistent])!;
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
