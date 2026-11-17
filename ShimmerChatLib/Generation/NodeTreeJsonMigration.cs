using Newtonsoft.Json.Linq;
using ShimmerChatLib.Components;

namespace ShimmerChatLib.Generation;

/// <summary>
/// 节点树反序列化结果。失败时不静默返回 null：调用方必须显示 <see cref="Error"/> 并回退，
/// 以便用户看到数据损坏而不是以为树是空的。
/// </summary>
public sealed class NodeDeserializationResult
{
    /// <summary>反序列化得到的根包装器；为空 JSON 时为 null。</summary>
    public ITreeNode? Root { get; init; }

    /// <summary>失败原因；成功时为 null。</summary>
    public string? Error { get; init; }

    /// <summary>是否从旧格式（节点对象自带 Id/Name）迁移而来。</summary>
    public bool Migrated { get; init; }

    /// <summary>是否为空白/空对象 JSON（不算失败，但也没有树）。</summary>
    public bool IsEmpty { get; init; }

    public bool Success => Error == null;

    public static NodeDeserializationResult Ok(ITreeNode root, bool migrated = false)
        => new() { Root = root, Migrated = migrated };

    public static NodeDeserializationResult Empty()
        => new() { IsEmpty = true };

    public static NodeDeserializationResult Failed(string error)
        => new() { Error = error };
}

/// <summary>
/// 节点树 JSON 懒迁移：把旧格式（业务节点对象自带 Id/Name，根/子节点直接是节点对象）
/// 转换为新格式（包装器持有 Id/Name/IsExpanded，业务节点位于 <c>Node</c> 属性下）。
/// 迁移只发生在读取阶段，保存时写回新格式；迁移检测幂等，可以重复执行。
/// </summary>
public static class NodeTreeJsonMigration
{
    public const string PayloadProperty = "Node";

    /// <summary>判断给定 JSON 对象是否是旧格式节点树。</summary>
    public static bool IsLegacy(JObject obj) => obj[PayloadProperty] is not JObject;

    /// <summary>
    /// 把旧格式节点树转换为新格式。未知节点类型会抛出异常，由序列化器转成显式失败，
    /// 不允许用一个空树掩盖损坏的数据。
    /// </summary>
    public static JObject MigrateLegacyTree(
        JObject legacyRoot,
        Type rootWrapperType,
        IReadOnlyDictionary<string, Type> typeMap)
    {
        return WrapNode(legacyRoot, rootWrapperType, typeMap);
    }

    private static JObject WrapNode(JObject payload, Type wrapperType, IReadOnlyDictionary<string, Type> typeMap)
    {
        if (!typeof(ITreeNode).IsAssignableFrom(wrapperType))
            throw new InvalidOperationException($"Wrapper type '{wrapperType.FullName}' does not implement ITreeNode.");

        var payloadType = ResolvePayloadType(payload, typeMap)
            ?? throw new InvalidOperationException(
                $"Legacy node tree contains an unknown node type: {payload["$type"]?.Value<string>() ?? "(missing $type)"}.");

        var wrapper = new JObject
        {
            ["Id"] = payload["Id"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(payload["Id"]?.Value<string>())
                ? payload["Id"]!.DeepClone()
                : Guid.NewGuid().ToString(),
            ["Name"] = payload["Name"]?.Type == JTokenType.String
                ? payload["Name"]!.DeepClone()
                : "",
            ["IsExpanded"] = true,
        };

        // Id/Name 属于包装器，不能留在业务节点对象里，否则保存时会写回旧结构。
        payload.Remove("Id");
        payload.Remove("Name");

        MigrateChildProperties(payload, payloadType, typeMap);

        wrapper[PayloadProperty] = payload;
        return wrapper;
    }

    private static void MigrateChildProperties(
        JObject payload,
        Type payloadType,
        IReadOnlyDictionary<string, Type> typeMap)
    {
        foreach (var prop in payloadType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!prop.CanRead || !payload.TryGetValue(prop.Name, out var value))
                continue;

            if (value is JArray array && TreeNodeReflection.IsListOfTreeNode(prop))
            {
                var elementType = TreeNodeReflection.GetTreeNodeElementType(prop.PropertyType)
                    ?? throw new InvalidOperationException(
                        $"Cannot determine tree node element type for '{payloadType.FullName}.{prop.Name}'.");

                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is not JObject child)
                        continue;

                    if (IsLegacy(child))
                        array[i] = WrapNode(child, elementType, typeMap);
                }
            }
            else if (value is JObject single && TreeNodeReflection.IsSingleTreeNode(prop))
            {
                if (IsLegacy(single))
                    payload[prop.Name] = WrapNode(single, prop.PropertyType, typeMap);
            }
        }
    }

    private static Type? ResolvePayloadType(JObject payload, IReadOnlyDictionary<string, Type> typeMap)
    {
        var raw = payload["$type"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (typeMap.TryGetValue(raw, out var type))
            return type;

        // 兼容旧序列化器/插件写出的 "Namespace.Type, Assembly" 形式。
        var comma = raw.IndexOf(',');
        if (comma > 0 && typeMap.TryGetValue(raw[..comma], out type))
            return type;

        return null;
    }
}
