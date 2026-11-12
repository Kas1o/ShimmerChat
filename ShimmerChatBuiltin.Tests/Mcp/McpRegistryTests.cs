using System.Diagnostics;
using ShimmerChatBuiltin.Mcp;

namespace ShimmerChatBuiltin.Tests.Mcp;

/// <summary>
/// 连接指纹决定「什么时候必须重连」。暴露策略与日志级别不该影响连接，
/// 否则用户在设置页勾一个工具就会触发子进程重启。
/// </summary>
public class McpRegistryFingerprintTests
{
    private static McpEndpointConfig Stdio(string command = "npx", params string[] args) => new()
    {
        Id = "srv",
        Name = "Srv",
        Transport = McpTransportType.Stdio,
        Command = command,
        Arguments = [.. args]
    };

    [Fact]
    public void Fingerprint_IsStableForSameConnectionSettings()
    {
        var a = McpRegistry.ComputeFingerprint(Stdio("npx", "-y", "server-filesystem"));
        var b = McpRegistry.ComputeFingerprint(Stdio("npx", "-y", "server-filesystem"));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Fingerprint_ChangesWhenCommandChanges()
    {
        Assert.NotEqual(
            McpRegistry.ComputeFingerprint(Stdio("npx")),
            McpRegistry.ComputeFingerprint(Stdio("uvx")));
    }

    [Fact]
    public void Fingerprint_ChangesWhenArgumentsChange()
    {
        Assert.NotEqual(
            McpRegistry.ComputeFingerprint(Stdio("npx", "-y", "a")),
            McpRegistry.ComputeFingerprint(Stdio("npx", "-y", "b")));
    }

    [Fact]
    public void Fingerprint_ChangesWhenEnvironmentChanges()
    {
        var a = Stdio("npx");
        var b = Stdio("npx");
        b.Environment = ["TOKEN=1"];

        Assert.NotEqual(McpRegistry.ComputeFingerprint(a), McpRegistry.ComputeFingerprint(b));
    }

    [Fact]
    public void Fingerprint_IgnoresExposurePolicy()
    {
        var a = Stdio("npx");
        var b = Stdio("npx");

        b.ExposeTools = false;
        b.ToolsEnabledByDefault = false;
        b.ExposeResourceReadTool = true;
        b.ResourceInjection = McpResourceInjection.All;
        b.ResourceCharBudget = 1;
        b.SetToolEnabled("read", false);

        Assert.Equal(McpRegistry.ComputeFingerprint(a), McpRegistry.ComputeFingerprint(b));
    }

    [Fact]
    public void Fingerprint_IgnoresLogLevel()
    {
        var a = Stdio("npx");
        var b = Stdio("npx");
        b.LogLevel = McpLogLevel.Debug;

        Assert.Equal(McpRegistry.ComputeFingerprint(a), McpRegistry.ComputeFingerprint(b));
    }

    [Fact]
    public void Fingerprint_ChangesWhenTransportChanges()
    {
        var stdio = Stdio("npx");
        var http = new McpEndpointConfig
        {
            Id = "srv",
            Name = "Srv",
            Transport = McpTransportType.Http,
            Url = "https://example.com/mcp"
        };

        Assert.NotEqual(McpRegistry.ComputeFingerprint(stdio), McpRegistry.ComputeFingerprint(http));
    }

    [Fact]
    public void Fingerprint_ChangesWhenHttpHeadersChange()
    {
        var a = new McpEndpointConfig
        {
            Id = "srv",
            Name = "Srv",
            Transport = McpTransportType.Http,
            Url = "https://example.com/mcp"
        };

        var b = new McpEndpointConfig
        {
            Id = "srv",
            Name = "Srv",
            Transport = McpTransportType.Http,
            Url = "https://example.com/mcp",
            Headers = ["Authorization: Bearer x"]
        };

        Assert.NotEqual(McpRegistry.ComputeFingerprint(a), McpRegistry.ComputeFingerprint(b));
    }
}

/// <summary>
/// 连接失败必须留下短期记忆：否则每个调用方都要各自等满握手超时。
/// 用一个必然启动失败的 STDIO 命令来验证，不依赖任何外部服务。
/// </summary>
public class McpRegistryFailureBackoffTests : IDisposable
{
    private const string MissingCommand = "shimmerchat-test-nonexistent-binary-xyz";

    public void Dispose() => McpRegistry.Instance.ResetForTests();

    [Fact]
    public async Task Acquire_RecordsFailureAndShortCircuitsWithinBackoff()
    {
        McpRegistry.Instance.ResetForTests();

        var config = new McpEndpointConfig
        {
            Id = "missing",
            Name = "Missing",
            Transport = McpTransportType.Stdio,
            Command = MissingCommand,
            InitializationTimeoutSeconds = 1
        };

        var first = await Assert.ThrowsAsync<McpConnectException>(
            () => McpRegistry.Instance.AcquireAsync(config, null, CancellationToken.None));
        Assert.Contains("Failed to connect", first.Message, StringComparison.OrdinalIgnoreCase);

        var sw = Stopwatch.StartNew();
        var second = await Assert.ThrowsAsync<McpConnectException>(
            () => McpRegistry.Instance.AcquireAsync(config, null, CancellationToken.None));
        sw.Stop();

        // 命中负缓存：报错里明确说明处于退避窗口，且几乎瞬时返回。
        Assert.Contains("backoff", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"second attempt took {sw.Elapsed}, expected an immediate short-circuit");
    }

    [Fact]
    public async Task Invalidate_ClearsFailureBackoff()
    {
        McpRegistry.Instance.ResetForTests();

        var config = new McpEndpointConfig
        {
            Id = "missing2",
            Name = "Missing 2",
            Transport = McpTransportType.Stdio,
            Command = MissingCommand,
            InitializationTimeoutSeconds = 1
        };

        await Assert.ThrowsAsync<McpConnectException>(
            () => McpRegistry.Instance.AcquireAsync(config, null, CancellationToken.None));

        McpRegistry.Instance.Invalidate(config.Key);

        // 失效后不再走负缓存：这次会真的再尝试连接一次（仍然失败，但原因不是退避）。
        var retried = await Assert.ThrowsAsync<McpConnectException>(
            () => McpRegistry.Instance.AcquireAsync(config, null, CancellationToken.None));

        Assert.DoesNotContain("backoff", retried.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Acquire_DoesNotCacheConfigurationErrors()
    {
        McpRegistry.Instance.ResetForTests();

        var config = new McpEndpointConfig
        {
            Id = "bad-id!",
            Name = "Bad",
            Transport = McpTransportType.Stdio,
            Command = "npx"
        };

        await Assert.ThrowsAsync<McpConfigException>(
            () => McpRegistry.Instance.AcquireAsync(config, null, CancellationToken.None));

        // 配置错误是本地可判定的，不应该被负缓存成「连接失败」。
        var again = await Assert.ThrowsAsync<McpConfigException>(
            () => McpRegistry.Instance.AcquireAsync(config, null, CancellationToken.None));

        Assert.DoesNotContain("backoff", again.Message, StringComparison.OrdinalIgnoreCase);
    }
}
