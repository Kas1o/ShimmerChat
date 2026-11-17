using Microsoft.Extensions.Logging;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 后生成节点树序列化器。扫描所有 IPostGenerationNode 实现构建类型白名单。
    /// </summary>
    public class PostGenerationNodeSerializerService : NodeJsonSerializerBase, IPostGenerationNodeSerializerService
    {
        protected override Type RootWrapperType => typeof(PostGenerationNodeWrapper);

        public PostGenerationNodeSerializerService(IPluginLoaderService pluginLoader, ILogger<PostGenerationNodeSerializerService> logger)
            : base(pluginLoader, typeof(IPostGenerationNode), logger)
        {
        }

        public string Serialize(PostGenerationNodeWrapper root) => SerializeCore(root);

        public PostGenerationNodeWrapper? Deserialize(string json)
            => DeserializeTree(json).Root as PostGenerationNodeWrapper;

        public NodeDeserializationResult DeserializeTree(string json) => DeserializeCore(json);

        string ITreeNodeSerializer.Serialize(ITreeNode root) => SerializeCore(root);

        ITreeNode? ITreeNodeSerializer.Deserialize(string json) => Deserialize(json);
    }
}
