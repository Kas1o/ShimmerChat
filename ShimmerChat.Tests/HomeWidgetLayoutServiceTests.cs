using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChat.Singletons;
using ShimmerChatLib.HomeWidget;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 首页布局持久化服务的行为测试：默认布局、往返、以及所有应当显式失败的输入。
/// </summary>
public class HomeWidgetLayoutServiceTests
{
    private const string Space = "Home";
    private const string Key = "WidgetLayout";

    private static readonly Assembly HostAssembly = typeof(Program).Assembly;
    private static readonly Assembly LibAssembly = typeof(IHomeWidget).Assembly;
    private static readonly Assembly BuiltinAssembly = typeof(ShimmerChatBuiltin.Target).Assembly;

    private static HomeWidgetLayoutService CreateService(IKVDataService kv)
    {
        var registry = new HomeWidgetRegistry(
            new StubPluginLoader(LibAssembly, BuiltinAssembly, HostAssembly),
            NullLogger<HomeWidgetRegistry>.Instance);

        return new HomeWidgetLayoutService(kv, registry, NullLogger<HomeWidgetLayoutService>.Instance);
    }

    [Fact]
    public void Load_WithoutSavedLayout_ReturnsDefaultsAndMarksFirstRun()
    {
        var service = CreateService(new MemoryKV());

        var result = service.Load();

        result.Success.Should().BeTrue();
        result.IsFirstRun.Should().BeTrue();
        result.Widgets.Select(w => w.WidgetId).Should().Equal("getting-started", "github-release");
        result.Widgets.Select(w => w.InstanceId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SaveAndLoad_RoundTripsInstanceSettings()
    {
        var kv = new MemoryKV();
        var service = CreateService(kv);
        var widgets = new List<HomeWidgetInstance>
        {
            new()
            {
                InstanceId = Guid.NewGuid(),
                WidgetId = "agent-quick",
                Settings = new Dictionary<string, string> { ["agentGuid"] = Guid.NewGuid().ToString() }
            },
            new() { InstanceId = Guid.NewGuid(), WidgetId = "github-release" }
        };

        var save = service.Save(widgets);
        save.Success.Should().BeTrue();

        var loaded = service.Load();
        loaded.Success.Should().BeTrue();
        loaded.IsFirstRun.Should().BeFalse();
        loaded.Widgets.Should().HaveCount(2);
        loaded.Widgets[0].WidgetId.Should().Be("agent-quick");
        loaded.Widgets[0].Settings["agentGuid"].Should().Be(widgets[0].Settings["agentGuid"]);
        loaded.Widgets[1].WidgetId.Should().Be("github-release");
    }

    [Fact]
    public void Load_InvalidJson_ReturnsExplicitFailure()
    {
        var kv = new MemoryKV();
        kv.Data[(Space, Key)] = "{ not json";

        var result = CreateService(kv).Load();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(HomeWidgetLayoutErrorCodes.InvalidJson);
        result.Widgets.Should().BeEmpty();
    }

    [Fact]
    public void Load_UnsupportedVersion_ReturnsExplicitFailure()
    {
        var kv = new MemoryKV();
        kv.Data[(Space, Key)] = """{"Version":99,"Widgets":[]}""";

        var result = CreateService(kv).Load();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(HomeWidgetLayoutErrorCodes.UnsupportedVersion);
    }

    [Fact]
    public void Load_EntryWithoutWidgetId_ReturnsExplicitFailure()
    {
        var kv = new MemoryKV();
        var instanceId = Guid.NewGuid();
        kv.Data[(Space, Key)] = JsonSerializer.Serialize(new
        {
            Version = 1,
            Widgets = new[] { new { InstanceId = instanceId, WidgetId = "", Settings = new Dictionary<string, string>() } }
        });

        var result = CreateService(kv).Load();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(HomeWidgetLayoutErrorCodes.InvalidEntry);
    }

    [Fact]
    public void Load_EntryWithoutInstanceId_ReturnsExplicitFailure()
    {
        var kv = new MemoryKV();
        kv.Data[(Space, Key)] = JsonSerializer.Serialize(new
        {
            Version = 1,
            Widgets = new[] { new { InstanceId = Guid.Empty, WidgetId = "agent-quick", Settings = new Dictionary<string, string>() } }
        });

        var result = CreateService(kv).Load();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(HomeWidgetLayoutErrorCodes.InvalidEntry);
    }

    [Fact]
    public void Load_EntryMissingInstanceId_ReturnsExplicitFailure()
    {
        var kv = new MemoryKV();
        kv.Data[(Space, Key)] = """{"Version":1,"Widgets":[{"WidgetId":"agent-quick","Settings":{}}]}""";

        var result = CreateService(kv).Load();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(HomeWidgetLayoutErrorCodes.InvalidEntry);
    }

    [Fact]
    public void Load_DuplicateInstanceIds_ReturnsExplicitFailure()
    {
        var kv = new MemoryKV();
        var instanceId = Guid.NewGuid();
        kv.Data[(Space, Key)] = JsonSerializer.Serialize(new
        {
            Version = 1,
            Widgets = new[]
            {
                new { InstanceId = instanceId, WidgetId = "agent-quick", Settings = new Dictionary<string, string>() },
                new { InstanceId = instanceId, WidgetId = "github-release", Settings = new Dictionary<string, string>() }
            }
        });

        var result = CreateService(kv).Load();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(HomeWidgetLayoutErrorCodes.DuplicateInstance);
    }

    [Fact]
    public void Load_KeepsUnknownWidgetIdsForExplicitPlaceholderRendering()
    {
        var kv = new MemoryKV();
        var instanceId = Guid.NewGuid();
        kv.Data[(Space, Key)] = JsonSerializer.Serialize(new
        {
            Version = 1,
            Widgets = new[]
            {
                new { InstanceId = instanceId, WidgetId = "removed-plugin.widget", Settings = new Dictionary<string, string>() }
            }
        });

        var result = CreateService(kv).Load();

        result.Success.Should().BeTrue();
        result.Widgets.Should().ContainSingle(widget => widget.WidgetId == "removed-plugin.widget");
    }

    [Fact]
    public void Load_MissingSettings_TreatsThemAsEmpty()
    {
        var kv = new MemoryKV();
        var instanceId = Guid.NewGuid();
        kv.Data[(Space, Key)] = $$"""{"Version":1,"Widgets":[{"InstanceId":"{{instanceId}}","WidgetId":"agent-quick","Settings":null}]}""";

        var result = CreateService(kv).Load();

        result.Success.Should().BeTrue();
        result.Widgets.Single().Settings.Should().BeEmpty();
    }

    [Fact]
    public void Load_EmptyWidgetList_IsAValidUserLayout()
    {
        var kv = new MemoryKV();
        kv.Data[(Space, Key)] = """{"Version":1,"Widgets":[]}""";

        var result = CreateService(kv).Load();

        result.Success.Should().BeTrue();
        result.Widgets.Should().BeEmpty();
        result.IsFirstRun.Should().BeFalse();
    }

    [Fact]
    public void Save_StorageFailure_ReturnsExplicitFailure()
    {
        var kv = new MemoryKV { ThrowOnWrite = true };
        var service = CreateService(kv);

        var result = service.Save(new List<HomeWidgetInstance>
        {
            new() { InstanceId = Guid.NewGuid(), WidgetId = "getting-started" }
        });

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(HomeWidgetLayoutErrorCodes.StorageFailure);
    }

    [Fact]
    public void Load_StorageFailure_ReturnsExplicitFailure()
    {
        var kv = new MemoryKV { ThrowOnRead = true };

        var result = CreateService(kv).Load();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(HomeWidgetLayoutErrorCodes.StorageFailure);
    }

    [Fact]
    public void Save_DuplicateInstanceIds_ReturnsExplicitFailure()
    {
        var service = CreateService(new MemoryKV());
        var instanceId = Guid.NewGuid();
        var widgets = new List<HomeWidgetInstance>
        {
            new() { InstanceId = instanceId, WidgetId = "agent-quick" },
            new() { InstanceId = instanceId, WidgetId = "github-release" }
        };

        var result = service.Save(widgets);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(HomeWidgetLayoutErrorCodes.DuplicateInstance);
    }

    [Fact]
    public void Save_EntryWithoutWidgetId_ReturnsExplicitFailure()
    {
        var service = CreateService(new MemoryKV());
        var widgets = new List<HomeWidgetInstance>
        {
            new() { InstanceId = Guid.NewGuid(), WidgetId = "" }
        };

        var result = service.Save(widgets);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(HomeWidgetLayoutErrorCodes.InvalidEntry);
    }

    [Fact]
    public void CreateDefaultLayout_UsesRegistryDefaults()
    {
        var service = CreateService(new MemoryKV());

        var defaults = service.CreateDefaultLayout();

        defaults.Select(widget => widget.WidgetId).Should().Equal("getting-started", "github-release");
    }

    private sealed class MemoryKV : IKVDataService
    {
        public Dictionary<(string Space, string Key), string> Data { get; } = new();

        public bool ThrowOnRead { get; init; }
        public bool ThrowOnWrite { get; init; }

        public string? Read(string spaceId, string key)
        {
            if (ThrowOnRead)
                throw new IOException("read failed");

            return Data.TryGetValue((spaceId, key), out var value) ? value : null;
        }

        public void Write(string spaceId, string key, string value)
        {
            if (ThrowOnWrite)
                throw new IOException("write failed");

            Data[(spaceId, key)] = value;
        }
    }

    private sealed class StubPluginLoader(params Assembly[] assemblies) : IPluginLoaderService
    {
        public IEnumerable<Assembly> GetAssemblies() => assemblies;

        public List<T> LoadImplementations<T>() => [];

        public List<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
            => assemblies.SelectMany(GetLoadableTypes)
                .Where(t => t.GetCustomAttributes(typeof(TAttribute), false).Any())
                .ToList();

        public List<Type> GetImplementingTypes(Type interfaceType)
            => assemblies.SelectMany(GetLoadableTypes)
                .Where(t => interfaceType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .ToList();

        public Task InitializePluginsAsync() => Task.CompletedTask;

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Cast<Type>();
            }
        }
    }
}
