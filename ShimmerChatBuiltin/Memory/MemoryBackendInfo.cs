using System.Text.Json;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChatBuiltin.Memory;

/// <summary>
/// 向量后端配置在界面上的只读呈现：配置文件位置、应编辑的 JSON 片段、数据目录的解析结果。
/// 面板只负责显示；所有推导都集中在这里，便于直接测试。
/// </summary>
public static class MemoryBackendInfo
{
    public const string FileName = "appsettings.json";
    public const string SectionName = "VectorStore";

    /// <summary>
    /// 找出实际被加载的那份 appsettings.json。
    /// 开发时宿主的工作目录是项目目录，发布后是应用目录；两者顺序尝试，
    /// 都不存在时返回第一个候选路径，由调用方提示文件缺失。
    /// </summary>
    public static string ResolveConfigPath(string contentRootPath, string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(contentRootPath, FileName)),
            Path.GetFullPath(Path.Combine(baseDirectory, FileName))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return candidates[0];
    }

    /// <summary>
    /// 生成当前配置对应的 JSON 片段，供用户直接对照 / 复制回 appsettings.json。
    /// 值来自启动时加载的配置对象，因此片段与运行中的行为一致。
    /// </summary>
    public static string BuildSnippet(VectorStoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var section = new
        {
            config.Provider,
            config.Scope,
            config.RootPath
        };

        return JsonSerializer.Serialize(
            new { VectorStore = section },
            new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 数据目录的绝对路径。InMemory 不落盘，返回 null 表示"该项不适用"；
    /// Provider 取值非法时仍然抛错，不做静默兜底。
    /// </summary>
    public static string? ResolveDataRoot(VectorStoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.GetProvider() != VectorStoreProvider.LiteDb) return null;

        return Path.GetFullPath(
            Path.IsPathRooted(config.RootPath)
                ? config.RootPath
                : Path.Combine(
                    AppContext.BaseDirectory,
                    config.RootPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
    }
}
