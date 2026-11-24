using System.Text.Json;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;

namespace ShimmerChatBuiltin.Memory;

/// <summary>
/// 向量后端配置在界面上的只读呈现：配置文件位置、应编辑的 JSON 片段、数据目录的解析结果。
/// 面板只负责显示；所有推导都集中在这里，便于直接测试。
/// </summary>
public static class MemoryBackendInfo
{
    public const string SectionName = "VectorStore";

    /// <summary>
    /// 存储配置文件位置：Data/storage_settings.json。
    /// </summary>
    public static string ResolveConfigPath(DataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.StorageSettingsFile;
    }

    /// <summary>
    /// 生成当前配置对应的 JSON 片段，供用户直接对照 / 复制回 storage_settings.json。
    /// 值来自启动时加载的配置对象，因此片段与运行中的行为一致。
    /// </summary>
    public static string BuildSnippet(VectorStoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var section = new { config.Provider };

        return JsonSerializer.Serialize(
            new { VectorStore = section },
            new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 数据实际位置。InMemory 不落盘，返回 null 表示"该项不适用"；
    /// Provider 取值非法时仍然抛错，不做静默兜底。
    /// </summary>
    public static string? ResolveDataRoot(VectorStoreConfig config, DataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(paths);

        return config.GetProvider() switch
        {
            VectorStoreProvider.LiteDb => paths.VectorDatabase,
            VectorStoreProvider.File => paths.LocalVectorDirectory,
            VectorStoreProvider.InMemory => null,
            _ => throw new InvalidOperationException($"未实现的向量存储 provider：{config.Provider}")
        };
    }
}
