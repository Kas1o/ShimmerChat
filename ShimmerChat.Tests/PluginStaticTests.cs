using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChat.Singletons;

namespace ShimmerChat.Tests;

public class PluginStaticTests : IDisposable
{
    private readonly List<string> _dirsToClean = new();

    public void Dispose()
    {
        foreach (var dir in _dirsToClean)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* 测试清理失败不阻塞 */ }
        }
    }

    /// <summary>创建临时插件目录：root/{name}/plugin.json + 可选文件。</summary>
    private string CreatePluginDir(string name, string pluginJson, params (string Path, string Content)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "ShimmerChatTests", Guid.NewGuid().ToString("N"));
        var pluginDir = Path.Combine(root, name);
        Directory.CreateDirectory(pluginDir);
        _dirsToClean.Add(root);

        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), pluginJson);
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(pluginDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return pluginDir;
    }

    private static PluginLoadContext Load(string pluginDir)
        => new(pluginDir, NullLogger<PluginLoadContext>.Instance);

    [Fact]
    public void WithStaticDir_ExposesUrlNameAndStaticDirPath()
    {
        var pluginDir = CreatePluginDir("MyCoolPlugin",
            """{ "name": "MyCoolPlugin", "static": "www" }""",
            ("www/index.html", "<html></html>"));

        var ctx = Load(pluginDir);

        ctx.TryLoadFromManifest(pluginDir).Should().BeTrue();
        ctx.UrlName.Should().Be("MyCoolPlugin");
        ctx.StaticDirPath.Should().Be(Path.GetFullPath(Path.Combine(pluginDir, "www")));
    }

    [Fact]
    public void PureStaticPluginWithoutAssembly_Loads()
    {
        var pluginDir = CreatePluginDir("MyCoolPlugin",
            """{ "name": "MyCoolPlugin", "static": "www" }""",
            ("www/app.js", "console.log('hi');"));

        var ctx = Load(pluginDir);

        ctx.TryLoadFromManifest(pluginDir).Should().BeTrue();
        ctx.StaticDirPath.Should().NotBeNull();
    }

    [Fact]
    public void NoAssemblyNoStatic_Rejected()
    {
        var pluginDir = CreatePluginDir("MyCoolPlugin",
            """{ "name": "MyCoolPlugin" }""");

        var ctx = Load(pluginDir);

        ctx.TryLoadFromManifest(pluginDir).Should().BeFalse();
    }

    [Fact]
    public void MissingManifest_Rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShimmerChatTests", Guid.NewGuid().ToString("N"));
        var pluginDir = Path.Combine(root, "EmptyPlugin");
        Directory.CreateDirectory(pluginDir);
        _dirsToClean.Add(root);

        var ctx = Load(pluginDir);

        ctx.TryLoadFromManifest(pluginDir).Should().BeFalse();
    }

    [Fact]
    public void StaticPathEscapesPluginDir_StaticDisabled()
    {
        var pluginDir = CreatePluginDir("MyCoolPlugin",
            """{ "name": "MyCoolPlugin", "static": "../escape" }""");
        // escape 目录位于插件目录之外（兄弟目录），必须存在才会触发穿越校验
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(pluginDir)!, "escape"));

        var ctx = Load(pluginDir);

        ctx.TryLoadFromManifest(pluginDir).Should().BeTrue();
        ctx.UrlName.Should().Be("MyCoolPlugin");
        ctx.StaticDirPath.Should().BeNull();
    }

    [Fact]
    public void StaticPointingAtPluginRoot_Disabled()
    {
        var pluginDir = CreatePluginDir("MyCoolPlugin",
            """{ "name": "MyCoolPlugin", "static": "." }""");

        var ctx = Load(pluginDir);

        ctx.TryLoadFromManifest(pluginDir).Should().BeTrue();
        ctx.StaticDirPath.Should().BeNull();
    }

    [Fact]
    public void StaticDirMissing_StaticDisabled()
    {
        var pluginDir = CreatePluginDir("MyCoolPlugin",
            """{ "name": "MyCoolPlugin", "static": "www" }""");

        var ctx = Load(pluginDir);

        ctx.TryLoadFromManifest(pluginDir).Should().BeTrue();
        ctx.StaticDirPath.Should().BeNull();
    }

    [Fact]
    public void NameWithNonAsciiChars_FallsBackToDirName()
    {
        var pluginDir = CreatePluginDir("MyCoolPlugin",
            """{ "name": "我的插件", "static": "www" }""",
            ("www/index.html", "<html></html>"));

        var ctx = Load(pluginDir);

        ctx.TryLoadFromManifest(pluginDir).Should().BeTrue();
        ctx.UrlName.Should().Be("MyCoolPlugin");
        ctx.StaticDirPath.Should().NotBeNull();
    }

    [Fact]
    public void InvalidName_FallsBackToDirName()
    {
        var pluginDir = CreatePluginDir("MyCoolPlugin",
            """{ "name": ".", "static": "www" }""",
            ("www/index.html", "<html></html>"));

        var ctx = Load(pluginDir);

        ctx.TryLoadFromManifest(pluginDir).Should().BeTrue();
        ctx.UrlName.Should().Be("MyCoolPlugin");
    }

    [Fact]
    public void NameAndDirNameBothInvalid_UrlSegmentNull()
    {
        // 目录名为全中文，sanitize 后为空；manifest name 为 "." 同样无效 → 无可用 URL 段
        var pluginDir = CreatePluginDir("插件",
            """{ "name": ".", "static": "www" }""",
            ("www/index.html", "<html></html>"));

        var ctx = Load(pluginDir);

        ctx.TryLoadFromManifest(pluginDir).Should().BeTrue();
        ctx.UrlName.Should().BeNull();
        ctx.StaticDirPath.Should().NotBeNull();
    }
}
