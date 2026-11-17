using Microsoft.Extensions.Logging;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class PreGenerationNodeSerializer : NodeJsonSerializerBase, IPreGenerationNodeSerializer
    {
        protected override Type RootWrapperType => typeof(PreGenerationNodeWrapper);

        public PreGenerationNodeSerializer(IPluginLoaderService pluginLoader, ILogger<PreGenerationNodeSerializer> logger)
            : base(pluginLoader, typeof(IPreGenerationNode), logger)
        {
        }

        public string Serialize(PreGenerationNodeWrapper root) => SerializeCore(root);

        public PreGenerationNodeWrapper? Deserialize(string json)
            => DeserializeTree(json).Root as PreGenerationNodeWrapper;

        public NodeDeserializationResult DeserializeTree(string json) => DeserializeCore(json);

        string ITreeNodeSerializer.Serialize(ITreeNode root) => SerializeCore(root);

        ITreeNode? ITreeNodeSerializer.Deserialize(string json) => Deserialize(json);
    }
}
