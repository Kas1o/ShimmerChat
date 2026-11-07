using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShimmerChatBuiltin.API;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Memory
{
    /// <summary>
    /// 当前嵌入模型配置。modelKey 由端点 + 模型名 + 度量决定：
    /// 同名模型换端点会被视为不同嵌入空间，从而落到不同集合。
    /// </summary>
    public sealed record MemoryModelConfig(
        string BaseUrl,
        string ApiKey,
        string ModelName,
        string ProfileId = "",
        string ProfileName = "")
    {
        public string ModelKey => ComputeKey(BaseUrl, ModelName);

        public string CollectionName => "memory_" + ModelKey[..16];

        public string DisplayName => string.IsNullOrEmpty(ProfileName) ? ModelName : ProfileName;

        public static string ComputeKey(string baseUrl, string modelName)
        {
            var normalized = $"{NormalizeBaseUrl(baseUrl)}|{modelName}|cosine";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        }

        public static string NormalizeBaseUrl(string baseUrl) => baseUrl.Trim().TrimEnd('/');

        /// <summary>
        /// 解析配置。profileId 为空时使用当前激活配置；显式指定时按 id 精确匹配。
        /// </summary>
        public static (MemoryModelConfig? Config, string? Error) Load(IKVDataService kv, string? profileId = null)
        {
            var profiles = EmbeddingProfileStore.EnsureMigrated(kv, out var loadError);
            if (loadError is not null)
                return (null, loadError);

            if (profiles.Count > 0)
            {
                EmbeddingProfile? profile;

                if (!string.IsNullOrWhiteSpace(profileId))
                {
                    profile = profiles.FirstOrDefault(x => string.Equals(x.Id, profileId, StringComparison.Ordinal));
                    if (profile is null)
                        return (null, $"嵌入配置 '{profileId}' 不存在。");
                }
                else
                {
                    var activeId = EmbeddingProfileStore.GetActiveId(kv);
                    if (string.IsNullOrWhiteSpace(activeId))
                        return (null, "尚未选择激活的嵌入配置，请在设置中选择一份。");

                    profile = profiles.FirstOrDefault(x => x.Id == activeId);
                    if (profile is null)
                        return (null, $"当前激活的嵌入配置 '{activeId}' 不存在，请在设置中重新选择。");
                }

                if (string.IsNullOrWhiteSpace(profile.BaseUrl))
                    return (null, $"嵌入配置 '{profile.Name}' 缺少 BaseUrl。");
                if (string.IsNullOrWhiteSpace(profile.ModelName))
                    return (null, $"嵌入配置 '{profile.Name}' 缺少模型名。");

                return (new MemoryModelConfig(
                    profile.BaseUrl,
                    profile.ApiKey,
                    profile.ModelName,
                    profile.Id,
                    profile.Name), null);
            }

            // 迁移后仍为空：兼容从未初始化过的旧数据路径。
            if (!string.IsNullOrWhiteSpace(profileId))
                return (null, $"嵌入配置 '{profileId}' 不存在。");

            var uri = kv.Read("EmbeddingAPI", "uri");
            var model = kv.Read("EmbeddingAPI", "modelname");
            if (string.IsNullOrWhiteSpace(uri))
                return (null, "尚未配置任何嵌入服务。");
            if (string.IsNullOrWhiteSpace(model))
                return (null, "尚未配置嵌入模型名。");

            return (new MemoryModelConfig(
                uri!,
                kv.Read("EmbeddingAPI", "apikey") ?? string.Empty,
                model!,
                string.Empty,
                "legacy"), null);
        }
    }

    /// <summary>
    /// 批量嵌入客户端。一次请求携带多条文本，避免逐条调用。
    /// </summary>
    public sealed class MemoryEmbeddingClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
        private readonly MemoryModelConfig _config;

        public MemoryEmbeddingClient(MemoryModelConfig config) => _config = config;

        public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
        {
            if (inputs.Count == 0) return [];

            var url = $"{MemoryModelConfig.NormalizeBaseUrl(_config.BaseUrl)}/embeddings";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrEmpty(_config.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

            request.Content = JsonContent.Create(new { model = _config.ModelName, input = inputs });

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"嵌入接口返回 {(int)response.StatusCode}：{Truncate(body, 400)}");

            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("嵌入接口返回结构非法：缺少 data 数组。");

            var results = new float[inputs.Count][];

            foreach (var item in data.EnumerateArray())
            {
                var index = item.TryGetProperty("index", out var indexElement) ? indexElement.GetInt32() : 0;
                if (index < 0 || index >= results.Length)
                    throw new InvalidDataException($"嵌入接口返回了越界 index：{index}。");

                if (!item.TryGetProperty("embedding", out var embedding) || embedding.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("嵌入接口返回结构非法：缺少 embedding 数组。");

                var vector = new float[embedding.GetArrayLength()];
                var cursor = 0;
                foreach (var value in embedding.EnumerateArray()) vector[cursor++] = value.GetSingle();
                results[index] = vector;
            }

            if (results.Any(x => x is null || x.Length == 0))
                throw new InvalidDataException("嵌入接口返回不完整：存在空向量。");

            return results;
        }

        /// <summary>探测维度，供首次建集合使用。</summary>
        public async Task<int> ProbeDimensionsAsync(CancellationToken ct = default)
        {
            var vectors = await EmbedAsync(["dimension-probe"], ct).ConfigureAwait(false);
            return vectors[0].Length;
        }

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : value[..max] + "…";
    }
}
