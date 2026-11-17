using System.Collections;
using System.Reflection;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Components;

/// <summary>
/// 节点包装器反射辅助方法，供所有节点编辑器组件与迁移逻辑使用。
/// 子节点属性定义在业务节点上（类型为包装器），因此遍历时需要先取 <see cref="ITreeNode.Payload"/>。
/// </summary>
public static class TreeNodeReflection
{
    /// <summary>
    /// 检查属性类型是否为包装器列表（List&lt;T&gt; 或 IList&lt;T&gt;，其中 T : ITreeNode）。
    /// </summary>
    public static bool IsListOfTreeNode(PropertyInfo prop)
        => GetTreeNodeElementType(prop.PropertyType) != null;

    /// <summary>
    /// 获取包装器列表的元素类型；不是包装器列表时返回 null。
    /// </summary>
    public static Type? GetTreeNodeElementType(Type type)
    {
        if (type.IsGenericType)
        {
            var genDef = type.GetGenericTypeDefinition();
            if ((genDef == typeof(List<>) || genDef == typeof(IList<>))
                && typeof(ITreeNode).IsAssignableFrom(type.GenericTypeArguments[0]))
                return type.GenericTypeArguments[0];
        }

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IList<>)
                && typeof(ITreeNode).IsAssignableFrom(iface.GenericTypeArguments[0]))
                return iface.GenericTypeArguments[0];
        }

        return null;
    }

    /// <summary>
    /// 检查属性类型是否为单个包装器。
    /// </summary>
    public static bool IsSingleTreeNode(PropertyInfo prop)
        => typeof(ITreeNode).IsAssignableFrom(prop.PropertyType);

    /// <summary>
    /// 枚举业务节点实例上的直接子包装器（不含递归）。
    /// </summary>
    public static IEnumerable<ITreeNode> GetChildNodes(object payload)
    {
        foreach (var prop in payload.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;

            if (IsListOfTreeNode(prop))
            {
                if (prop.GetValue(payload) is not IList list) continue;
                foreach (var item in list)
                {
                    if (item is ITreeNode child)
                        yield return child;
                }
            }
            else if (IsSingleTreeNode(prop))
            {
                if (prop.GetValue(payload) is ITreeNode child)
                    yield return child;
            }
        }
    }

    /// <summary>
    /// 递归遍历节点树中所有子包装器。
    /// </summary>
    public static void VisitChildren(ITreeNode node, Action<ITreeNode> visitor)
    {
        if (node.Payload is not { } payload)
            return;

        foreach (var child in GetChildNodes(payload))
        {
            visitor(child);
            VisitChildren(child, visitor);
        }
    }

    /// <summary>
    /// 重新生成节点及其所有子孙节点的 Id（复制/粘贴用）。
    /// </summary>
    public static void RegenerateIds(ITreeNode node)
    {
        node.Id = Guid.NewGuid().ToString();

        if (node.Payload is not { } payload)
            return;

        foreach (var child in GetChildNodes(payload))
            RegenerateIds(child);
    }
}
