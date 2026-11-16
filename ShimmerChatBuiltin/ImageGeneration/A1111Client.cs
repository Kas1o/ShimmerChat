using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShimmerChatLib;

namespace ShimmerChatBuiltin.ImageGeneration
{
    /// <summary>创建 A1111 客户端所需的连接参数。已由配置校验保证取值合法。</summary>
    public sealed record A1111ClientOptions(
        string BaseUrl,
        string ApiKey,
        TimeSpan MaxWait);

    /// <summary>A1111 返回的一张图像。内容是原始字节，落盘由调用方经 IImageAttachmentService 完成。</summary>
    public sealed class A1111Image
    {
        public required string FileName { get; init; }
        public required string ContentType { get; init; }
        public required byte[] Content { get; init; }
    }

    /// <summary>一次 A1111 生成的结论。失败时 Error 给模型可读说明，Details 给 DebugOutput。</summary>
    public sealed class A1111GenerationResult
    {
        public bool Success { get; private init; }
        public string Error { get; private init; } = "";
        public string? Details { get; private init; }
        public List<A1111Image> Images { get; private init; } = [];

        public static A1111GenerationResult Ok(List<A1111Image> images) =>
            new() { Success = true, Images = images };

        public static A1111GenerationResult Fail(string error, string? details = null) =>
            new() { Success = false, Error = error, Details = details };
    }

    /// <summary>一次 A1111 txt2img 的结构化请求。由工具根据配置与工具参数解析得到。</summary>
    public sealed record A1111Txt2ImgRequest(
        string Prompt,
        string NegativePrompt,
        string Model,
        string Sampler,
        int Width,
        int Height,
        int Steps,
        double CfgScale,
        long Seed,
        int BatchSize);

    /// <summary>
    /// A1111 协议客户端：POST /sdapi/v1/txt2img，响应中的 base64 图像直接解码。
    /// 实现只负责协议与失败汇报，不做重试、不吞异常，也不直接写文件。
    /// </summary>
    public interface IA1111Client
    {
        Task<A1111GenerationResult> GenerateAsync(A1111Txt2ImgRequest request, CancellationToken ct);
    }

    public sealed class A1111Client : IA1111Client
    {
        private readonly HttpClient _http;
        private readonly A1111ClientOptions _options;
        private readonly string _baseUrl;

        public A1111Client(HttpClient http, A1111ClientOptions options)
        {
            _http = http;
            _options = options;
            _baseUrl = options.BaseUrl.Trim().TrimEnd('/');
        }

        public async Task<A1111GenerationResult> GenerateAsync(
            A1111Txt2ImgRequest request, CancellationToken ct)
        {
            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overallCts.CancelAfter(_options.MaxWait);

            try
            {
                return await GenerateCoreAsync(request, overallCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return A1111GenerationResult.Fail(
                    $"A1111 image generation did not finish within {_options.MaxWait.TotalSeconds:0.#}s.");
            }
        }

        private async Task<A1111GenerationResult> GenerateCoreAsync(
            A1111Txt2ImgRequest request, CancellationToken ct)
        {
            var payload = BuildPayload(request);

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post, $"{_baseUrl}/sdapi/v1/txt2img")
            {
                Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };

            ImageGenerationHttp.ApplyAuthorization(httpRequest, _options.ApiKey);

            string body;
            try
            {
                using var response = await _http.SendAsync(httpRequest, ct);
                body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    return A1111GenerationResult.Fail(
                        $"A1111 returned HTTP {(int)response.StatusCode} ({response.StatusCode}) " +
                        $"while generating the image: {ExtractErrorBody(body)}",
                        body);
                }
            }
            catch (HttpRequestException ex)
            {
                return A1111GenerationResult.Fail(
                    $"A1111 request failed: {ex.Message}", ex.ToString());
            }

            JObject json;
            try
            {
                json = JObject.Parse(body);
            }
            catch (JsonException ex)
            {
                return A1111GenerationResult.Fail(
                    "A1111 returned a non-JSON response.",
                    ex.Message + Environment.NewLine + Truncate(body, 2000));
            }

            if (json["images"] is not JArray images || images.Count == 0)
            {
                return A1111GenerationResult.Fail(
                    "A1111 finished but returned no images.", Truncate(body, 2000));
            }

            var results = new List<A1111Image>(images.Count);
            for (var i = 0; i < images.Count; i++)
            {
                var base64 = images[i]?.Value<string>();
                if (string.IsNullOrWhiteSpace(base64))
                {
                    return A1111GenerationResult.Fail(
                        $"A1111 returned an empty image at index {i}.", Truncate(body, 2000));
                }

                var (content, declaredMime, decodeError) = DecodeImage(base64);
                if (content == null)
                {
                    return A1111GenerationResult.Fail(
                        $"A1111 returned an invalid image at index {i}: {decodeError}",
                        Truncate(body, 2000));
                }

                if (!ImageMimeDetector.TryResolve(declaredMime, content, out var mime, out var mimeError))
                {
                    return A1111GenerationResult.Fail(
                        $"A1111 returned an unsupported image at index {i}: {mimeError}",
                        Truncate(body, 2000));
                }

                var extension = ImageAttachmentRules.MimeToExtension.TryGetValue(mime, out var ext)
                    ? ext
                    : ".png";

                results.Add(new A1111Image
                {
                    FileName = $"a1111_{i + 1:D4}{extension}",
                    ContentType = mime,
                    Content = content
                });
            }

            return A1111GenerationResult.Ok(results);
        }

        private static JObject BuildPayload(A1111Txt2ImgRequest request)
        {
            var payload = new JObject
            {
                ["prompt"] = request.Prompt,
                ["negative_prompt"] = request.NegativePrompt,
                ["sampler_name"] = request.Sampler,
                ["width"] = request.Width,
                ["height"] = request.Height,
                ["steps"] = request.Steps,
                ["cfg_scale"] = request.CfgScale,
                ["seed"] = request.Seed,
                ["batch_size"] = request.BatchSize,
                ["n_iter"] = 1,
                // 返回的 base64 已足够，不在服务端输出目录留下副本。
                ["do_not_save_samples"] = true,
                ["do_not_save_grid"] = true
            };

            if (!string.IsNullOrWhiteSpace(request.Model))
            {
                payload["override_settings"] = new JObject
                {
                    ["sd_model_checkpoint"] = request.Model
                };
            }

            return payload;
        }

        private static (byte[]? Content, string? DeclaredMime, string? Error) DecodeImage(string base64)
        {
            var text = base64.Trim();
            string? declaredMime = null;

            if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = text.IndexOf(',');
                if (comma < 0)
                    return (null, null, "data URI has no comma separator.");

                var header = text[5..comma];
                var semicolon = header.IndexOf(';');
                declaredMime = (semicolon >= 0 ? header[..semicolon] : header).Trim();
                text = text[(comma + 1)..];
            }

            try
            {
                var content = Convert.FromBase64String(text);
                return content.Length == 0
                    ? (null, declaredMime, "decoded image is empty.")
                    : (content, declaredMime, null);
            }
            catch (FormatException ex)
            {
                return (null, declaredMime, $"base64 decoding failed: {ex.Message}");
            }
        }

        private static string ExtractErrorBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "(empty response body)";

            try
            {
                var token = JToken.Parse(body);

                if (token is JObject obj)
                {
                    var detail = obj["detail"] ?? obj["error"];
                    if (detail != null)
                        return detail.Type == JTokenType.String
                            ? detail.Value<string>()!
                            : detail.ToString(Formatting.None);
                }
            }
            catch (JsonException)
            {
                // 非 JSON 错误体按原文截断返回
            }

            const int maxLength = 500;
            return body.Length > maxLength ? body[..maxLength] + "…" : body;
        }

        private static string Truncate(string? text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "(empty response body)";
            return text.Length > maxChars ? text[..maxChars] + "…" : text;
        }
    }

    /// <summary>A1111 设置面板用的连接探测：读取模型与采样器下拉数据。</summary>
    public static class A1111Probe
    {
        public static Task<(IReadOnlyList<string>? Values, string? Error)> FetchCheckpointModelsAsync(
            string baseUrl, string apiKey, TimeSpan timeout, CancellationToken ct = default,
            HttpClient? http = null) =>
            FetchNamesAsync(baseUrl, apiKey, "/sdapi/v1/sd-models", timeout, ct,
                ["model_name", "title", "filename"], http);

        public static Task<(IReadOnlyList<string>? Values, string? Error)> FetchSamplersAsync(
            string baseUrl, string apiKey, TimeSpan timeout, CancellationToken ct = default,
            HttpClient? http = null) =>
            FetchNamesAsync(baseUrl, apiKey, "/sdapi/v1/samplers", timeout, ct, ["name"], http);

        private static async Task<(IReadOnlyList<string>? Values, string? Error)> FetchNamesAsync(
            string baseUrl, string apiKey, string path, TimeSpan timeout, CancellationToken ct,
            IReadOnlyList<string> nameProperties, HttpClient? http)
        {
            var url = $"{baseUrl.Trim().TrimEnd('/')}{path}";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ImageGenerationHttp.ApplyAuthorization(request, apiKey);

                using var response = await (http ?? ImageGenerationHttp.Client)
                    .SendAsync(request, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    return (null,
                        $"A1111 returned HTTP {(int)response.StatusCode} for {path}: {Truncate(body, 300)}");
                }

                JArray array;
                try
                {
                    array = JArray.Parse(body);
                }
                catch (JsonException ex)
                {
                    return (null, $"A1111 returned non-JSON data for {path}: {ex.Message}");
                }

                var values = array
                    .OfType<JObject>()
                    .Select(item => nameProperties
                        .Select(property => item[property]?.Value<string>())
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                return values.Count > 0
                    ? (values, null)
                    : (null, $"A1111 returned no usable names for {path}.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (null,
                    $"A1111 did not respond within {timeout.TotalSeconds:0.#}s for {path}.");
            }
            catch (HttpRequestException ex)
            {
                return (null, $"Failed to reach A1111 for {path}: {ex.Message}");
            }
        }

        private static string Truncate(string? text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "(empty)";
            return text.Length > maxChars ? text[..maxChars] + "…" : text;
        }
    }
}
