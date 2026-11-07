using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.API
{
    /// <summary>一个可切换的嵌入服务配置。</summary>
    public sealed class EmbeddingProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
    }

    /// <summary>
    /// 嵌入配置的多配置存储。数据落在 KV 的 EmbeddingAPI 空间：
    /// profiles = JSON 数组，active = 当前配置 id。旧版的 uri/apikey/modelname 会自动迁移。
    /// </summary>
    public static class EmbeddingProfileStore
    {
        private const string Space = "EmbeddingAPI";
        private const string ProfilesKey = "profiles";
        private const string ActiveKey = "active";

        public static List<EmbeddingProfile> Load(IKVDataService kv) => Load(kv, out _);

        /// <summary>
        /// 读取配置列表；JSON 损坏时通过 error 返回明确原因，不再伪装成「没有配置」。
        /// </summary>
        public static List<EmbeddingProfile> Load(IKVDataService kv, out string? error)
        {
            error = null;
            var raw = kv.Read(Space, ProfilesKey);
            if (string.IsNullOrWhiteSpace(raw)) return [];

            try
            {
                return JsonSerializer.Deserialize<List<EmbeddingProfile>>(raw) ?? [];
            }
            catch (JsonException ex)
            {
                error = $"嵌入配置数据损坏，无法解析：{ex.Message}";
                return [];
            }
        }

        public static void Save(IKVDataService kv, IReadOnlyList<EmbeddingProfile> profiles)
        {
            kv.Write(Space, ProfilesKey, JsonSerializer.Serialize(profiles));
        }

        public static string? GetActiveId(IKVDataService kv) => kv.Read(Space, ActiveKey);

        public static void SetActive(IKVDataService kv, string id) => kv.Write(Space, ActiveKey, id);

        /// <summary>读取配置列表；若为空则尝试把旧版单配置迁移成一条记录。</summary>
        public static List<EmbeddingProfile> EnsureMigrated(IKVDataService kv) => EnsureMigrated(kv, out _);

        /// <summary>读取配置列表，并把数据损坏等错误通过 error 返回给调用方。</summary>
        public static List<EmbeddingProfile> EnsureMigrated(IKVDataService kv, out string? error)
        {
            var profiles = Load(kv, out error);
            if (error is not null) return profiles;
            if (profiles.Count > 0) return profiles;

            var uri = kv.Read(Space, "uri");
            var model = kv.Read(Space, "modelname");
            if (string.IsNullOrWhiteSpace(uri) && string.IsNullOrWhiteSpace(model)) return [];

            var profile = new EmbeddingProfile
            {
                Id = "default",
                Name = "默认配置",
                BaseUrl = uri ?? string.Empty,
                ApiKey = kv.Read(Space, "apikey") ?? string.Empty,
                ModelName = model ?? string.Empty
            };

            profiles.Add(profile);
            Save(kv, profiles);
            SetActive(kv, profile.Id);
            return profiles;
        }

        public static EmbeddingProfile? GetActive(IKVDataService kv)
        {
            var profiles = EnsureMigrated(kv);
            if (profiles.Count == 0) return null;

            var activeId = GetActiveId(kv);
            return profiles.FirstOrDefault(x => x.Id == activeId);
        }

        public static string NewId() => Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>把嵌入配置暴露成节点编辑器的下拉选项。</summary>
    public sealed class EmbeddingProfileOptionsProvider : INodePropertyOptionsProvider
    {
        public string Key => "embedding_profiles";

        public IReadOnlyList<NodePropertyOption> GetOptions(IServiceProvider services)
        {
            if (services.GetService(typeof(IKVDataService)) is not IKVDataService kv) return [];

            var profiles = EmbeddingProfileStore.EnsureMigrated(kv, out var error);
            if (error is not null)
            {
                if (services.GetService(typeof(ILogger<EmbeddingProfileOptionsProvider>)) is ILogger<EmbeddingProfileOptionsProvider> logger)
                    logger.LogError("加载嵌入配置下拉选项失败：{Error}", error);
                return [];
            }

            var activeId = EmbeddingProfileStore.GetActiveId(kv);
            return profiles
                .Select(profile =>
                {
                    var suffix = profile.Id == activeId ? " ★" : string.Empty;
                    return new NodePropertyOption(profile.Id, $"{profile.Name} ({profile.ModelName}){suffix}");
                })
                .ToList();
        }
    }
}
