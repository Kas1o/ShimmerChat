using System.Reflection;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点包装器基类。持有树结构状态（Id / 显示名 / 展开状态），把这些状态从业务节点类中移出。
    /// 序列化时包装器只写出自身状态与 <c>Node</c> 负载，负载仍按白名单类型解析。
    /// </summary>
    /// <typeparam name="TNode">管线节点接口类型。</typeparam>
    public class NodeWrapper<TNode> : ITreeNode where TNode : class
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "";

        public bool IsExpanded { get; set; } = true;

        /// <summary>业务节点实例。反序列化失败或数据缺失时为 null。</summary>
        public TNode? Node { get; set; }

        object? ITreeNode.Payload => Node;

        Type ITreeNode.PayloadType => Node?.GetType() ?? typeof(TNode);

        public NodeWrapper() { }

        public NodeWrapper(TNode node, string? name = null)
        {
            Node = node;
            Name = name ?? NodeNaming.GetDefaultName(node.GetType());
        }
    }

    /// <summary>
    /// 预生成管线节点包装器。执行失败时由包装器补齐 NodeId / NodeName，
    /// 业务节点不再需要自己维护这些字段。
    /// </summary>
    public sealed class PreGenerationNodeWrapper : NodeWrapper<IPreGenerationNode>
    {
        public PreGenerationNodeWrapper() { }

        public PreGenerationNodeWrapper(IPreGenerationNode node, string? name = null) : base(node, name) { }

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            if (Node == null)
                return NodeResult.Failure(
                    NodeErrorCodes.PayloadMissing,
                    $"Node payload is missing (wrapper '{Name}', id '{Id}').",
                    nodeId: Id,
                    nodeName: Name);

            var previousId = context.CurrentNodeId;
            var previousName = context.CurrentNodeName;
            context.CurrentNodeId = Id;
            context.CurrentNodeName = Name;

            try
            {
                var result = await Node.ExecuteAsync(context);
                if (!result.Success)
                {
                    result.NodeId ??= Id;
                    result.NodeName ??= Name;
                }
                return result;
            }
            finally
            {
                context.CurrentNodeId = previousId;
                context.CurrentNodeName = previousName;
            }
        }
    }

    /// <summary>
    /// 后生成管线节点包装器。
    /// </summary>
    public sealed class PostGenerationNodeWrapper : NodeWrapper<IPostGenerationNode>
    {
        public PostGenerationNodeWrapper() { }

        public PostGenerationNodeWrapper(IPostGenerationNode node, string? name = null) : base(node, name) { }

        public async Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            if (Node == null)
            {
                var missing = PostNodeResult.Failure(
                    NodeErrorCodes.PayloadMissing,
                    $"Node payload is missing (wrapper '{Name}', id '{Id}').");
                missing.NodeId = Id;
                missing.NodeName = Name;
                return missing;
            }

            var result = await Node.ExecuteAsync(context);
            if (!result.Success)
            {
                result.NodeId ??= Id;
                result.NodeName ??= Name;
            }
            return result;
        }
    }

    /// <summary>
    /// 渲染管线节点包装器。业务节点抛出的 <see cref="RenderNodeException"/>
    /// 若未携带节点信息，由包装器补全。
    /// </summary>
    public sealed class RenderModifierNodeWrapper : NodeWrapper<IRenderModifierNode>
    {
        public RenderModifierNodeWrapper() { }

        public RenderModifierNodeWrapper(IRenderModifierNode node, string? name = null) : base(node, name) { }

        public void Execute(RenderNodeExecutionContext context)
        {
            if (Node == null)
                throw new RenderNodeException(
                    NodeErrorCodes.PayloadMissing,
                    $"Node payload is missing (wrapper '{Name}', id '{Id}').",
                    Id,
                    Name);

            var previousId = context.CurrentNodeId;
            var previousName = context.CurrentNodeName;
            context.CurrentNodeId = Id;
            context.CurrentNodeName = Name;

            try
            {
                Node.Execute(context);
            }
            catch (RenderNodeException ex) when (ex.NodeId == null && ex.NodeName == null)
            {
                throw new RenderNodeException(ex.Code, ex.Message, Id, Name);
            }
            finally
            {
                context.CurrentNodeId = previousId;
                context.CurrentNodeName = previousName;
            }
        }
    }

    /// <summary>
    /// 节点默认显示名：优先使用 <see cref="NodeInfoAttribute.LabelKey"/>，否则使用类型名。
    /// </summary>
    public static class NodeNaming
    {
        public static string GetDefaultName(Type nodeType)
            => nodeType.GetCustomAttribute<NodeInfoAttribute>()?.LabelKey ?? nodeType.Name;
    }

    /// <summary>
    /// 管线节点接口与包装器类型的映射，避免各处手写 switch。
    /// </summary>
    public static class NodeWrapperTypes
    {
        private static readonly Dictionary<Type, Type> Map = new()
        {
            [typeof(IPreGenerationNode)] = typeof(PreGenerationNodeWrapper),
            [typeof(IPostGenerationNode)] = typeof(PostGenerationNodeWrapper),
            [typeof(IRenderModifierNode)] = typeof(RenderModifierNodeWrapper),
        };

        public static Type Get(Type nodeInterfaceType)
        {
            if (Map.TryGetValue(nodeInterfaceType, out var wrapperType))
                return wrapperType;

            throw new ArgumentException(
                $"'{nodeInterfaceType.FullName}' is not a known node pipeline interface.",
                nameof(nodeInterfaceType));
        }

        public static IReadOnlyCollection<Type> Interfaces => Map.Keys;
    }
}
