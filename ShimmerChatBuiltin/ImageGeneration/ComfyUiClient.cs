using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShimmerChatLib;

namespace ShimmerChatBuiltin.ImageGeneration
{
    /// <summary>创建 ComfyUI 客户端所需的连接参数。已由配置校验保证取值合法。</summary>
    public sealed record ComfyUiClientOptions(
        string BaseUrl,
        TimeSpan RequestTimeout,
        TimeSpan MaxWait,
        TimeSpan PollInterval);

    /// <summary>ComfyUI 返回的一张图像。内容是原始字节，落盘由调用方经 IImageAttachmentService 完成。</summary>
    public sealed class ComfyUiImage
    {
        public required string FileName { get; init; }
        public required string ContentType { get; init; }
        public required byte[] Content { get; init; }
    }

    /// <summary>一次 ComfyUI 生成的结论。失败时 Error 给模型可读说明，Details 给 DebugOutput。</summary>
    public sealed class ComfyUiGenerationResult
    {
        public bool Success { get; private init; }
        public string Error { get; private init; } = "";
        public string? Details { get; private init; }
        public List<ComfyUiImage> Images { get; private init; } = [];

        public static ComfyUiGenerationResult Ok(List<ComfyUiImage> images) =>
            new() { Success = true, Images = images };

        public static ComfyUiGenerationResult Fail(string error, string? details = null) =>
            new() { Success = false, Error = error, Details = details };
    }

    /// <summary>
    /// ComfyUI 协议客户端：POST /prompt → 轮询 GET /history/{id} → GET /view 取图。
    /// 实现只负责协议与失败汇报，不做重试、不吞异常，也不直接写文件。
    /// </summary>
    public interface IComfyUiClient
    {
        Task<ComfyUiGenerationResult> GenerateAsync(JObject workflow, CancellationToken ct);
    }

    public sealed class ComfyUiClient : IComfyUiClient
    {
        private readonly HttpClient _http;
        private readonly ComfyUiClientOptions _options;
        private readonly string _baseUrl;

        public ComfyUiClient(HttpClient http, ComfyUiClientOptions options)
        {
            _http = http;
            _options = options;
            _baseUrl = options.BaseUrl.Trim().TrimEnd('/');
        }

        public async Task<ComfyUiGenerationResult> GenerateAsync(JObject workflow, CancellationToken ct)
        {
            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overallCts.CancelAfter(_options.MaxWait);

            try
            {
                return await GenerateCoreAsync(workflow, overallCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return ComfyUiGenerationResult.Fail(
                    $"ComfyUI generation did not finish within {_options.MaxWait.TotalSeconds:0.#}s.");
            }
        }

        private async Task<ComfyUiGenerationResult> GenerateCoreAsync(JObject workflow, CancellationToken ct)
        {
            var (promptId, queueError, queueDetails) = await QueuePromptAsync(workflow, ct);
            if (promptId == null)
                return ComfyUiGenerationResult.Fail(queueError!, queueDetails);

            return await WaitAndCollectAsync(promptId, ct);
        }

        private async Task<(string? PromptId, string? Error, string? Details)> QueuePromptAsync(
            JObject workflow, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["prompt"] = workflow,
                ["client_id"] = Guid.NewGuid().ToString("N")
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/prompt")
            {
                Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };

            var (body, error) = await SendTextAsync(request, ct, "queueing the prompt");
            if (body == null)
                return (null, error, null);

            JObject json;
            try
            {
                json = JObject.Parse(body);
            }
            catch (JsonException ex)
            {
                return (null, "ComfyUI returned a non-JSON response while queueing the prompt.",
                    ex.Message + Environment.NewLine + body);
            }

            var nodeErrors = json["node_errors"];
            var hasNodeErrors = nodeErrors is JObject nodeErrorObject && nodeErrorObject.Count > 0
                                || nodeErrors is JArray nodeErrorArray && nodeErrorArray.Count > 0;

            if (hasNodeErrors)
            {
                return (null, "ComfyUI rejected the workflow because some nodes are invalid.",
                    nodeErrors!.ToString(Formatting.None));
            }

            if (json["error"] != null)
            {
                return (null, $"ComfyUI rejected the workflow: {DescribeError(json["error"]!)}",
                    json["error"]!.ToString(Formatting.None));
            }

            var promptId = json["prompt_id"]?.ToString();
            if (string.IsNullOrWhiteSpace(promptId))
                return (null, "ComfyUI did not return a prompt_id.", body);

            return (promptId, null, null);
        }

        private async Task<ComfyUiGenerationResult> WaitAndCollectAsync(
            string promptId, CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                using var request = new HttpRequestMessage(
                    HttpMethod.Get, $"{_baseUrl}/history/{Uri.EscapeDataString(promptId)}");

                var (body, error) = await SendTextAsync(request, ct, "reading the prompt history");
                if (body == null)
                    return ComfyUiGenerationResult.Fail(error!);

                JObject history;
                try
                {
                    history = JObject.Parse(body);
                }
                catch (JsonException ex)
                {
                    return ComfyUiGenerationResult.Fail(
                        "ComfyUI returned a non-JSON history response.",
                        ex.Message + Environment.NewLine + body);
                }

                if (history[promptId] is JObject entry)
                {
                    var status = entry["status"] as JObject;
                    var statusText = status?["status_str"]?.ToString();
                    var outputs = entry["outputs"] as JObject;

                    if (string.Equals(statusText, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        return ComfyUiGenerationResult.Fail(
                            "ComfyUI execution failed.",
                            DescribeHistoryError(status, entry));
                    }

                    var completed = status?["completed"]?.Value<bool>() == true
                                    || string.Equals(statusText, "success", StringComparison.OrdinalIgnoreCase);

                    // 老版本 ComfyUI 可能只回 outputs 而没有 status，这时以 outputs 出现作为完成信号。
                    if (completed || (status == null && outputs is { Count: > 0 }))
                    {
                        var references = CollectImageReferences(outputs);
                        if (references.Count == 0)
                        {
                            return ComfyUiGenerationResult.Fail(
                                "ComfyUI finished but the workflow produced no output images. " +
                                "Make sure it ends with a SaveImage node.",
                                entry.ToString(Formatting.None));
                        }

                        var images = new List<ComfyUiImage>(references.Count);
                        foreach (var reference in references)
                        {
                            var (image, downloadError, downloadDetails) =
                                await DownloadImageAsync(reference, ct);
                            if (image == null)
                                return ComfyUiGenerationResult.Fail(downloadError!, downloadDetails);

                            images.Add(image);
                        }

                        return ComfyUiGenerationResult.Ok(images);
                    }
                }

                await Task.Delay(_options.PollInterval, ct);
            }
        }

        private async Task<(ComfyUiImage? Image, string? Error, string? Details)> DownloadImageAsync(
            ImageReference reference, CancellationToken ct)
        {
            var url = $"{_baseUrl}/view" +
                      $"?filename={Uri.EscapeDataString(reference.FileName)}" +
                      $"&subfolder={Uri.EscapeDataString(reference.Subfolder)}" +
                      $"&type={Uri.EscapeDataString(reference.Type)}";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.RequestTimeout);

            try
            {
                using var response = await _http.GetAsync(url, timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                    return (null,
                        $"ComfyUI returned HTTP {(int)response.StatusCode} while downloading '{reference.FileName}'.",
                        ExtractErrorBody(errorBody));
                }

                var content = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
                if (content.Length == 0)
                    return (null, $"ComfyUI returned an empty image for '{reference.FileName}'.", null);

                var mimeType = ResolveMimeType(
                    response.Content.Headers.ContentType?.MediaType, reference.FileName);

                if (mimeType == null)
                {
                    return (null,
                        $"ComfyUI returned an unsupported image type for '{reference.FileName}' " +
                        $"(content type: {response.Content.Headers.ContentType?.MediaType ?? "none"}).",
                        null);
                }

                return (new ComfyUiImage
                {
                    FileName = reference.FileName,
                    ContentType = mimeType,
                    Content = content
                }, null, null);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (null,
                    $"ComfyUI did not return '{reference.FileName}' within " +
                    $"{_options.RequestTimeout.TotalSeconds:0.#}s.", null);
            }
            catch (HttpRequestException ex)
            {
                return (null, $"Failed to download generated image: {ex.Message}", ex.ToString());
            }
        }

        private async Task<(string? Body, string? Error)> SendTextAsync(
            HttpRequestMessage request, CancellationToken ct, string operation)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.RequestTimeout);

            try
            {
                using var response = await _http.SendAsync(request, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    return (null,
                        $"ComfyUI returned HTTP {(int)response.StatusCode} ({response.StatusCode}) " +
                        $"while {operation}: {ExtractErrorBody(body)}");
                }

                return (body, null);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (null,
                    $"ComfyUI did not respond within {_options.RequestTimeout.TotalSeconds:0.#}s while {operation}.");
            }
            catch (HttpRequestException ex)
            {
                return (null, $"ComfyUI request failed while {operation}: {ex.Message}");
            }
        }

        private static List<ImageReference> CollectImageReferences(JObject? outputs)
        {
            var references = new List<ImageReference>();
            if (outputs == null)
                return references;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in outputs.Properties())
            {
                if (node.Value is not JObject nodeOutput)
                    continue;

                if (nodeOutput["images"] is not JArray images)
                    continue;

                foreach (var image in images.OfType<JObject>())
                {
                    var fileName = image["filename"]?.ToString();
                    if (string.IsNullOrWhiteSpace(fileName))
                        continue;

                    var reference = new ImageReference(
                        fileName,
                        image["subfolder"]?.ToString() ?? "",
                        image["type"]?.ToString() ?? "output");

                    var key = $"{reference.Type}/{reference.Subfolder}/{reference.FileName}";
                    if (seen.Add(key))
                        references.Add(reference);
                }
            }

            return references;
        }

        private static string DescribeError(JToken error)
        {
            if (error is JObject obj)
            {
                var message = obj["message"]?.ToString();
                var details = obj["details"]?.ToString();
                var type = obj["type"]?.ToString();

                if (!string.IsNullOrWhiteSpace(message))
                {
                    return string.IsNullOrWhiteSpace(details)
                        ? message!
                        : $"{message} ({details})";
                }

                if (!string.IsNullOrWhiteSpace(type))
                    return type!;
            }

            return error.ToString(Formatting.None);
        }

        private static string DescribeHistoryError(JObject? status, JObject entry)
        {
            if (status?["messages"] is JArray messages)
            {
                var parts = new List<string>();
                foreach (var message in messages)
                {
                    if (message is not JArray pair || pair.Count < 2)
                        continue;

                    var kind = pair[0].ToString();
                    var data = pair[1];

                    var text = data is JObject dataObject
                        ? FirstNonEmpty(
                            dataObject["exception_message"]?.ToString(),
                            dataObject["message"]?.ToString(),
                            dataObject["details"]?.ToString())
                        : null;

                    parts.Add(string.IsNullOrWhiteSpace(text) ? kind : $"{kind}: {text}");
                }

                if (parts.Count > 0)
                    return string.Join(Environment.NewLine, parts) +
                           Environment.NewLine + status.ToString(Formatting.None);
            }

            return entry.ToString(Formatting.None);
        }

        private static string ExtractErrorBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "(empty response body)";

            try
            {
                var token = JToken.Parse(body);
                if (token is JObject obj && obj["error"] != null)
                    return DescribeError(obj["error"]!);
            }
            catch (JsonException)
            {
                // 非 JSON 错误体按原文截断返回
            }

            const int maxLength = 500;
            return body.Length > maxLength ? body[..maxLength] + "…" : body;
        }

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        private static string? ResolveMimeType(string? contentType, string fileName)
        {
            if (ImageAttachmentRules.IsSupportedMimeType(contentType))
                return contentType!.Split(';')[0].Trim().ToLowerInvariant();

            var extension = Path.GetExtension(fileName);
            var match = ImageAttachmentRules.MimeToExtension.FirstOrDefault(
                pair => string.Equals(pair.Value, extension, StringComparison.OrdinalIgnoreCase));

            return match.Key;
        }

        private readonly record struct ImageReference(string FileName, string Subfolder, string Type);
    }

    /// <summary>
    /// 共享 HttpClient。超时由每次请求自己的 CTS 控制，因此不设置 HttpClient.Timeout。
    /// </summary>
    public static class ImageGenerationHttp
    {
        public static HttpClient Client { get; } = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    /// <summary>设置面板用的连接探测：读取 ComfyUI 的模型 / 采样器下拉数据。</summary>
    public static class ComfyUiProbe
    {
        public sealed record SamplerOptions(IReadOnlyList<string> Samplers, IReadOnlyList<string> Schedulers);

        public static Task<(IReadOnlyList<string>? Values, string? Error)> FetchCheckpointModelsAsync(
            string baseUrl, TimeSpan timeout, CancellationToken ct = default) =>
            FetchComboAsync(baseUrl, "CheckpointLoaderSimple", "ckpt_name", timeout, ct);

        public static async Task<(SamplerOptions? Options, string? Error)> FetchSamplerOptionsAsync(
            string baseUrl, TimeSpan timeout, CancellationToken ct = default)
        {
            var (samplers, samplerError) =
                await FetchComboAsync(baseUrl, "KSampler", "sampler_name", timeout, ct);
            if (samplers == null)
                return (null, samplerError);

            var (schedulers, schedulerError) =
                await FetchComboAsync(baseUrl, "KSampler", "scheduler", timeout, ct);
            if (schedulers == null)
                return (null, schedulerError);

            return (new SamplerOptions(samplers, schedulers), null);
        }

        private static async Task<(IReadOnlyList<string>? Values, string? Error)> FetchComboAsync(
            string baseUrl, string nodeName, string inputName, TimeSpan timeout, CancellationToken ct)
        {
            var url = $"{baseUrl.Trim().TrimEnd('/')}/object_info/{nodeName}";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                using var response = await ImageGenerationHttp.Client.GetAsync(url, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    return (null,
                        $"ComfyUI returned HTTP {(int)response.StatusCode} for /object_info/{nodeName}: " +
                        Truncate(body, 300));
                }

                JObject root;
                try
                {
                    root = JObject.Parse(body);
                }
                catch (JsonException ex)
                {
                    return (null, $"ComfyUI returned non-JSON object info: {ex.Message}");
                }

                var token = root.SelectToken($"{nodeName}.input.required.{inputName}")
                            ?? root.SelectToken($"{nodeName}.input.optional.{inputName}");

                return ParseCombo(token, nodeName, inputName);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (null,
                    $"ComfyUI did not respond within {timeout.TotalSeconds:0.#}s for /object_info/{nodeName}.");
            }
            catch (HttpRequestException ex)
            {
                return (null, $"Failed to reach ComfyUI for /object_info/{nodeName}: {ex.Message}");
            }
        }

        private static (IReadOnlyList<string>? Values, string? Error) ParseCombo(
            JToken? token, string nodeName, string inputName)
        {
            if (token is not JArray array || array.Count == 0)
            {
                return (null,
                    $"ComfyUI object info for {nodeName}.{inputName} is missing or not an array.");
            }

            IReadOnlyList<string>? values = null;

            if (array[0] is JArray direct)
            {
                values = direct.Values<string?>()
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!)
                    .ToList();
            }
            else if (array[0] is JObject combo
                     && (combo["options"] ?? combo["values"]) is JArray options)
            {
                values = options.Values<string?>()
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!)
                    .ToList();
            }
            else
            {
                var strings = array
                    .Where(t => t.Type == JTokenType.String)
                    .Select(t => t.Value<string>()!)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                if (strings.Count > 0)
                    values = strings;
            }

            return values is { Count: > 0 }
                ? (values, null)
                : (null, $"ComfyUI object info for {nodeName}.{inputName} contains no values.");
        }

        private static string Truncate(string? text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "(empty)";
            return text.Length > maxChars ? text[..maxChars] + "…" : text;
        }
    }
}
