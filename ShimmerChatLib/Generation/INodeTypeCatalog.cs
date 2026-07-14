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
/// 扫描所有 IGenerationNode 实现类型，提供统一的节点类型目录。
/// 扫描委托给 <see cref="IPluginLoaderService"/>，确保插件加载的节点也能被发现。
/// </summary>
public interface INodeTypeCatalog
{
    IReadOnlyList<NodeTypeMetadata> GetAllNodeTypes();
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

    private static List<NodeTypeMetadata> ScanAll(IPluginLoaderService pluginLoader, ILogger<NodeTypeCatalog> logger)
    {
        var types = pluginLoader.GetImplementingTypes(typeof(IGenerationNode));
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