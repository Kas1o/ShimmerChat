using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ShimmerChat.Singletons.VectorStore;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;

namespace ShimmerChat.Tests.VectorStore;

public sealed class VectorStoreRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "shimmer-vector-registration", Guid.NewGuid().ToString("N"));

    private DataPaths Paths => new(Path.Combine(_root, "Data"));

    [Theory]
    [InlineData("InMemory", typeof(InMemoryVectorStore))]
    [InlineData("LiteDb", typeof(LiteDbVectorStore))]
    [InlineData("File", typeof(FileVectorStore))]
    public void AddShimmerVectorStore_ResolvesConfiguredProvider(string provider, Type expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShimmerVectorStore(new VectorStoreConfig { Provider = provider }, Paths);

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<IVectorStoreService>();

        store.Should().BeOfType(expected);
        serviceProvider.GetRequiredService<VectorStoreConfig>().Provider.Should().Be(provider);
    }

    [Fact]
    public void AddShimmerVectorStore_UnknownProvider_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddShimmerVectorStore(
            new VectorStoreConfig { Provider = "NotAProvider" },
            Paths);

        act.Should().Throw<InvalidOperationException>().WithMessage("*NotAProvider*");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响断言
        }
    }
}
