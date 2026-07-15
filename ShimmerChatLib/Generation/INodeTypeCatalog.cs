using System.Reflection;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Generation;

/// <summary>
/// 节点类型元数据，供添加节点菜单使用。
/// </summary>
public record NodeTypeMetadata(
    string LabelKey,
    Type Type,
    string Icon,
    string Color,
    string? DescriptionKey,
    string FullCategory
);

/// <summary>
/// 扫描所有 ITreeNode 实现类型，提供统一的节点类型目录。
/// 支持按管线接口类型（IPreGenerationNode / IPostGenerationNode / IRenderModifierNode）过滤。
/// 扫描委托给 <see cref="IPluginLoaderService"/>，确保插件加载的节点也能被发现。
/// </summary>
public interface INodeTypeCatalog
{
    /// <summary>获取所有已发现的节点类型（跨所有管线）</summary>
    IReadOnlyList<NodeTypeMetadata> GetAllNodeTypes();

    /// <summary>获取实现指定节点接口的节点类型</summary>
    IReadOnlyList<NodeTypeMetadata> GetNodeTypes(Type nodeInterfaceType);
}

public class NodeTypeCatalog : INodeTypeCatalog
{
    private readonly Lazy<IReadOnlyList<NodeTypeMetadata>> _types;
    private readonly ILogger<NodeTypeCatalog> _logger;

    public NodeTypeCatalog(IPluginLoaderService pluginLoader, ILogger<NodeTypeCatalog> logger)
    {
        _logger = logger;
        _types = new Lazy<IReadOnlyList<NodeTypeMetadata>>(() => ScanAll(pluginLoader, _logger));
    }

    public IReadOnlyList<NodeTypeMetadata> GetAllNodeTypes() => _types.Value;

    public IReadOnlyList<NodeTypeMetadata> GetNodeTypes(Type nodeInterfaceType)
    {
        return _types.Value.Where(t => nodeInterfaceType.IsAssignableFrom(t.Type)).ToList();
    }

    private static List<NodeTypeMetadata> ScanAll(IPluginLoaderService pluginLoader, ILogger<NodeTypeCatalog> logger)
    {
        var types = pluginLoader.GetImplementingTypes(typeof(ITreeNode));
        var result = new List<NodeTypeMetadata>(types.Count);

        foreach (var type in types)
        {
            try
            {
                var info = type.GetCustomAttribute<NodeInfoAttribute>();
                result.Add(new NodeTypeMetadata(
                    info?.LabelKey ?? type.Name,
                    type,
                    info?.Icon ?? "●",
                    info?.Color ?? "var(--node-debug)",
                    info?.DescriptionKey,
                    info?.CategoryKeys is { Length: > 0 } cats ? string.Join("/", cats) : "category.general"
                ));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NodeTypeCatalog scan error ({TypeName}): {Message}", type.FullName, ex.Message);
            }
        }

        return result;
    }
}