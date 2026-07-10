using System.Reflection;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 跨树的节点剪贴板，基于节点 JSON 序列化实现复制/粘贴。
    /// </summary>
    public static class NodeClipboard
    {
        private static string? _json;
        private static IGenerationNodeSerializer _serializer = null!;

        /// <summary>由宿主在启动时注入。</summary>
        public static void Initialize(IGenerationNodeSerializer serializer) => _serializer = serializer;

        public static bool HasContent => _json != null;

        public static void Copy(IGenerationNode node)
        {
            _json = _serializer.Serialize(node);
        }

        public static IGenerationNode? Paste()
        {
            if (_json == null) return null;
            var node = _serializer.Deserialize(_json);
            if (node != null) RegenerateIds(node);
            return node;
        }

        public static void Clear()
        {
            _json = null;
        }

        private static void RegenerateIds(IGenerationNode node)
        {
            var idProp = node.GetType().GetProperty("Id");
            if (idProp != null && idProp.CanWrite && idProp.PropertyType == typeof(string))
            {
                idProp.SetValue(node, Guid.NewGuid().ToString());
            }

            // Recursively regenerate IDs in child lists
            foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                var value = prop.GetValue(node);
                if (value is IList<IGenerationNode> children)
                {
                    foreach (var child in children)
                        RegenerateIds(child);
                }
                else if (value is IGenerationNode singleChild)
                {
                    RegenerateIds(singleChild);
                }
            }
        }
    }
}
