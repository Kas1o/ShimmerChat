using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShimmerChat.Singletons.VectorStore;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChat.Tests.VectorStore;

public sealed class VectorStoreRegistrationTests
{
    private static IConfiguration BuildConfiguration(string provider)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VectorStore:Provider"] = provider,
                ["VectorStore:RootPath"] = Path.Combine(Path.GetTempPath(), "shimmer-vector-registration"),
                ["VectorStore:Scope"] = "PerOwner"
            })
            .Build();
    }

    [Fact]
    public void AddShimmerVectorStore_InMemory_ResolvesInMemoryStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShimmerVectorStore(BuildConfiguration("InMemory"));

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IVectorStoreService>();

        store.Should().BeOfType<InMemoryVectorStore>();
        provider.GetRequiredService<VectorStoreConfig>().Provider.Should().Be("InMemory");
    }

    [Fact]
    public void AddShimmerVectorStore_UnknownProvider_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddShimmerVectorStore(BuildConfiguration("NotAProvider"));

        act.Should().Throw<InvalidOperationException>();
    }
}
