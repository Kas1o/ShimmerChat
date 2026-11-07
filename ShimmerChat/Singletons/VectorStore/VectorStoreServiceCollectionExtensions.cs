using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChat.Singletons.VectorStore
{
    /// <summary>
    /// 向量存储的 DI 注册。provider 由 appsettings 的 VectorStore 节决定，
    /// 未知取值在启动阶段直接失败，不静默回落到默认实现。
    /// </summary>
    public static class VectorStoreServiceCollectionExtensions
    {
        public static IServiceCollection AddShimmerVectorStore(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var config = configuration.GetSection("VectorStore").Get<VectorStoreConfig>() ?? new VectorStoreConfig();

            var provider = config.GetProvider();
            _ = config.IsPerCollectionScope();

            services.AddSingleton(config);

            switch (provider)
            {
                case VectorStoreProvider.LiteDb:
                    services.AddSingleton<IVectorStoreService, LiteDbVectorStore>();
                    break;
                case VectorStoreProvider.InMemory:
                    services.AddSingleton<IVectorStoreService, InMemoryVectorStore>();
                    break;
                default:
                    throw new InvalidOperationException($"未实现的向量存储 provider：{provider}");
            }

            return services;
        }
    }
}
