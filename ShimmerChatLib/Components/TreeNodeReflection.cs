using System.Collections;
using System.Reflection;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Components;

/// <summary>
/// ITreeNode 反射辅助方法，供所有节点编辑器组件使用。
/// 处理泛型不变性问题：List&lt;IGenerationNode&gt; 不是 IList&lt;ITreeNode&gt;。
/// </summary>
public static class TreeNodeReflection
{
    /// <summary>
    /// 检查属性类型是否为 ITreeNode 的列表（List&lt;T&gt; 或 IList&lt;T&gt;，其中 T : ITreeNode）
    /// </summary>
    public static bool IsListOfTreeNode(PropertyInfo prop)
    {
        var type = prop.PropertyType;
        if (type.IsGenericType)
        {
            var genDef = type.GetGenericTypeDefinition();
            if (genDef == typeof(List<>) || genDef == typeof(IList<>))
                return typeof(ITreeNode).IsAssignableFrom(type.GenericTypeArguments[0]);
        }
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IList<>)
                && typeof(ITreeNode).IsAssignableFrom(iface.GenericTypeArguments[0]))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检查属性类型是否为单个 ITreeNode（直接实现或派生）
    /// </summary>
    public static bool IsSingleTreeNode(PropertyInfo prop)
        => typeof(ITreeNode).IsAssignableFrom(prop.PropertyType);

    /// <summary>
    /// 从属性值获取子节点列表（处理泛型不变性，返回非泛型 IList）
    /// 如果属性值为 ITreeNode 列表则返回 IList，否则返回 null。
    /// </summary>
    public static IList? GetChildList(PropertyInfo prop, object instance)
    {
        if (!IsListOfTreeNode(prop)) return null;
        return prop.GetValue(instance) as IList;
    }

    /// <summary>
    /// 递归遍历节点树中所有 ITreeNode 子节点属性。
    /// </summary>
    public static void VisitChildren(ITreeNode node, Action<ITreeNode> visitor)
    {
        foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            var value = prop.GetValue(node);

            if (value is IList list && IsListOfTreeNode(prop))
            {
                foreach (var item in list)
                {
                    if (item is ITreeNode child)
                    {
                        visitor(child);
                        VisitChildren(child, visitor);
                    }
                }
            }
            else if (value is ITreeNode singleChild)
            {
                visitor(singleChild);
                VisitChildren(singleChild, visitor);
            }
        }
    }

    /// <summary>
    /// 重新生成节点及其所有子节点的 Id
    /// </summary>
    public static void RegenerateIds(ITreeNode node)
    {
        var idProp = node.GetType().GetProperty("Id");
        if (idProp != null && idProp.CanWrite && idProp.PropertyType == typeof(string))
            idProp.SetValue(node, Guid.NewGuid().ToString());

        VisitChildren(node, _ => { }); // 触发点仅用于 Id 重设，已在上面处理
        // Actually need to regenerate for each child too
        foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            var value = prop.GetValue(node);
            if (value is IList list && IsListOfTreeNode(prop))
            {
                foreach (var item in list)
                {
                    if (item is ITreeNode child)
                        RegenerateIds(child);
                }
            }
            else if (value is ITreeNode singleChild)
            {
                RegenerateIds(singleChild);
            }
        }
    }
}
