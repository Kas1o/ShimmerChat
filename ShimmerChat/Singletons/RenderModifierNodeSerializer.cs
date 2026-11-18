using Microsoft.Extensions.Logging;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 渲染修改节点树序列化器。扫描所有 IRenderModifierNode 实现构建类型白名单。
    /// </summary>
    public class RenderModifierNodeSerializer : NodeJsonSerializerBase, ITreeNodeSerializer, IRenderModifierNodeSerializer
    {
        protected override Type RootWrapperType => typeof(RenderModifierNodeWrapper);

        public RenderModifierNodeSerializer(IPluginLoaderService pluginLoader, ILogger<RenderModifierNodeSerializer> logger)
            : base(pluginLoader, typeof(IRenderModifierNode), logger)
        {
        }

        public string Serialize(RenderModifierNodeWrapper root) => SerializeCore(root);

        public RenderModifierNodeWrapper? Deserialize(string json)
            => DeserializeTree(json).Root as RenderModifierNodeWrapper;

        public NodeDeserializationResult DeserializeTree(string json) => DeserializeCore(json);

        string ITreeNodeSerializer.Serialize(ITreeNode root) => SerializeCore(root);

        ITreeNode? ITreeNodeSerializer.Deserialize(string json) => Deserialize(json);
    }
}
