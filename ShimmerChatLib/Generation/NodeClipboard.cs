using System.Reflection;
using ShimmerChatLib.Components;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 跨树的节点剪贴板，基于节点 JSON 序列化实现复制/粘贴。
    /// 全局静态剪贴板，用于跨管线编辑器页面的复制。
    /// 每个管线编辑器页面也可以通过 <see cref="Components.TreeEditorContext"/> 使用自己的剪贴板。
    /// </summary>
    public static class NodeClipboard
    {
        private static string? _json;

        public static bool HasContent => _json != null;

        public static void Copy(ITreeNodeSerializer serializer, ITreeNode node)
        {
            _json = serializer.Serialize(node);
        }

        public static ITreeNode? Paste(ITreeNodeSerializer serializer)
        {
            if (_json == null) return null;
            var node = serializer.Deserialize(_json);
            if (node != null) TreeNodeReflection.RegenerateIds(node);
            return node;
        }

        public static void Clear()
        {
            _json = null;
        }
    }
}
