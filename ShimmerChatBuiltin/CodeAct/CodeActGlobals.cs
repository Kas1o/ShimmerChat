using System.Text;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatBuiltin.FileSystem;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.CodeAct
{
    /// <summary>
    /// CodeAct 脚本内可以直接抛出的预期内失败。工具层把它翻译成结构化
    /// <see cref="ToolResult.Fail"/>，而不是当成脚本崩溃。
    /// </summary>
    public sealed class CodeActException : Exception
    {
        public CodeActException(string code, string message, string? details = null)
            : base(message)
        {
            Code = code;
            Details = details;
        }

        /// <summary>错误码（<see cref="ToolErrorCodes"/> 中的常量）。</summary>
        public string Code { get; }

        /// <summary>技术细节，写进 DebugOutput，不直接给模型。</summary>
        public string? Details { get; }
    }

    /// <summary>
    /// CodeAct 脚本的 globals 对象：脚本里可直接使用 <see cref="Tools"/>、<see cref="CallAsync"/>、
    /// <see cref="AddImageAsync"/>、<see cref="Ct"/>、<see cref="Env"/> 等成员。
    /// <para>
    /// <b>这是进程内、全权限的代码执行</b>：脚本与宿主共享同一 AppDomain，
    /// 不做沙箱。它存在的意义就是让模型组合调用已有工具、读写宿主环境，
    /// 因此这里只负责提供把手与显式错误，不试图限制脚本能力。
    /// </para>
    /// <para>
    /// <b>取消是协作式的</b>：<see cref="Ct"/> 就是本轮工具调用的令牌，会传给被调用的工具；
    /// 但 Roslyn 无法中断用户代码里的同步循环。脚本若长时间计算，必须自行调用
    /// <c>Ct.ThrowIfCancellationRequested()</c>，文档与工具描述都写明了这一点。
    /// </para>
    /// </summary>
    public sealed class CodeActGlobals
    {
        /// <summary>单次脚本最多发起多少次嵌套工具调用，防止脚本失控刷工具。</summary>
        public const int MaxNestedToolCalls = 128;

        /// <summary>单次脚本最多随结果回传多少张图像，防止消息体积失控。</summary>
        public const int MaxImages = 16;

        private readonly IReadOnlyList<IToolV2> _allTools;
        private readonly bool _imageOutputEnabled;
        private readonly CancellationToken _ct;
        private readonly string _parentCallId;
        private readonly int _roundIndex;
        private readonly IImageAttachmentService? _imageAttachments;
        private readonly List<(Tool Definition, IToolV2 Tool)> _tools = new();
        private readonly List<MessageImage> _images = new();
        private readonly List<string> _imageUrls = new();
        private readonly StringBuilder _output = new();
        private readonly SemaphoreSlim _toolGate = new(1, 1);
        private int _nestedCalls;
        private int _omittedImages;
        private int _limitSkippedImages;

        public CodeActGlobals(
            PersistentEnv env,
            IReadOnlyList<IToolV2> tools,
            bool imageOutputEnabled,
            CancellationToken ct,
            string parentCallId,
            int roundIndex)
        {
            Env = env;
            _allTools = tools;
            _imageOutputEnabled = imageOutputEnabled;
            _ct = ct;
            _parentCallId = parentCallId;
            _roundIndex = roundIndex;
            _imageAttachments = env.ImageAttachments;

            BuildToolIndex();
        }

        /// <summary>持久化环境（KVData / 对话 / Agent / 服务入口）。</summary>
        public PersistentEnv Env { get; }

        public Chat Chat => Env.Chat;

        public Agent Agent => Env.Agent;

        public IKVDataService KVData => Env.KVData;

        /// <summary>本轮工具调用的取消令牌。异步调用与长循环都应使用它。</summary>
        public CancellationToken Ct => _ct;

        /// <summary>self 引用，便于脚本写成 <c>CodeAct.CallAsync(...)</c> 提高可读性。</summary>
        public CodeActGlobals CodeAct => this;

        /// <summary>本轮可调用的工具名（去重，不包含 CodeAct 自身）。</summary>
        public IReadOnlyList<string> Tools => _tools.Select(t => t.Definition.name).ToList();

        /// <summary>已随本次结果回传的图像 URL（与 <see cref="AddImageAsync"/> 的返回值一致）。</summary>
        public IReadOnlyList<string> AttachedImageUrls => _imageUrls.ToList();

        /// <summary><c>Print</c> 累积的文本；工具层会把它附在返回值之后一起交给模型。</summary>
        public string Output => _output.ToString();

        /// <summary>被调用工具返回、但因“关闭图像输出”而没有回传的图像数量。</summary>
        internal int OmittedImageCount => _omittedImages;

        /// <summary>超过 <see cref="MaxImages"/> 而没有被回传的图像数量。</summary>
        internal int LimitSkippedImageCount => _limitSkippedImages;

        /// <summary>实际随结果回传的图像。</summary>
        internal IReadOnlyList<MessageImage> Images => _images;

        /// <summary>把文本写入工具输出（不写控制台，避免污染宿主进程的标准输出）。</summary>
        public void Print(object? value)
        {
            _output.AppendLine(value?.ToString() ?? "null");
        }

        /// <summary>查看某个工具的名称、描述与参数声明，供脚本在调用前自查。</summary>
        public string GetToolInfo(string toolName)
        {
            var (definition, _) = FindTool(toolName);

            var sb = new StringBuilder();
            sb.Append("name: ").AppendLine(definition.name);
            sb.Append("description: ").AppendLine(definition.description ?? "");

            if (definition.parameters is not { Count: > 0 })
            {
                sb.AppendLine("parameters: none");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine("parameters:");
            foreach (var (parameter, required) in definition.parameters)
            {
                sb.Append("- ").Append(parameter.name)
                    .Append(" (").Append(parameter.type)
                    .Append(required ? ", required" : ", optional").Append("): ")
                    .AppendLine(parameter.description ?? "");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 调用本轮可用的一个工具并返回其文本结果。
        /// 工具返回 <see cref="ToolResultStatus.Failed"/> 时抛出 <see cref="CodeActException"/>，
        /// 让脚本显式处理失败，而不是把错误文本当成功继续执行。
        /// </summary>
        public async Task<string> CallAsync(string toolName, object? arguments = null)
        {
            var result = await CallToolAsync(toolName, arguments);
            if (result.Status == ToolResultStatus.Failed)
            {
                throw new CodeActException(
                    result.ErrorCode ?? ToolErrorCodes.ServiceError,
                    result.ErrorMessage ?? $"Tool '{toolName}' failed.",
                    result.Details);
            }

            return result.Content ?? string.Empty;
        }

        /// <summary>
        /// 调用本轮可用的一个工具并返回完整 <see cref="ToolResult"/>。
        /// 需要读取失败码或自行降级时使用它；工具返回的图像会自动随结果回传。
        /// </summary>
        public async Task<ToolResult> CallToolAsync(string toolName, object? arguments = null)
        {
            _ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(toolName))
                throw new CodeActException(ToolErrorCodes.InvalidArguments, "Tool name is required.");

            var (definition, tool) = FindTool(toolName);

            if (Interlocked.Increment(ref _nestedCalls) > MaxNestedToolCalls)
            {
                throw new CodeActException(
                    ToolErrorCodes.CodeExecutionError,
                    $"Nested tool call limit reached ({MaxNestedToolCalls} calls per script).");
            }

            var argumentsJson = SerializeArguments(arguments);
            var callId = $"{_parentCallId}#{_nestedCalls}";

            ToolResult result;
            await _toolGate.WaitAsync(_ct);
            try
            {
                var invocation = new ToolInvocationContext
                {
                    ToolCallId = callId,
                    ToolName = definition.name,
                    ArgumentsJson = argumentsJson,
                    RoundIndex = _roundIndex
                };

                result = tool is IToolV3 v3
                    ? await v3.ExecuteAsync(invocation, _ct)
                    : ToolResult.Ok(await tool.ExecuteAsync(argumentsJson));
            }
            finally
            {
                _toolGate.Release();
            }

            ForwardImages(result);

            Env.DebugOutput.Write("CodeAct", "ToolCall",
                $"Round {_roundIndex}: '{definition.name}' (#{_nestedCalls}) " +
                $"{(result.Status == ToolResultStatus.Completed ? "completed" : $"failed: {result.ErrorCode}")}");

            return result;
        }

        /// <summary>
        /// 读取本地图像文件并附加到本次工具结果，返回可直接写进回复的显示 URL。
        /// MIME 类型按文件头识别；显式传入 <paramref name="mimeType"/> 时必须与文件头一致。
        /// </summary>
        public async Task<string> AddImageAsync(string path, string? mimeType = null)
        {
            EnsureImageOutputEnabled();

            if (string.IsNullOrWhiteSpace(path))
                throw new CodeActException(ToolErrorCodes.InvalidArguments, "'path' is required.");

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                throw new CodeActException(ToolErrorCodes.InvalidArguments,
                    $"Invalid image path: '{path}'.", ex.ToString());
            }

            if (Directory.Exists(fullPath))
                throw new CodeActException(ToolErrorCodes.InvalidArguments,
                    $"Path is a directory, not an image file: {fullPath}");

            if (!File.Exists(fullPath))
                throw new CodeActException(ToolErrorCodes.InvalidArguments, $"File not found: {fullPath}");

            var file = new FileInfo(fullPath);
            if (file.Length == 0)
                throw new CodeActException(ToolErrorCodes.InvalidArguments, $"Image file is empty: {fullPath}");

            if (file.Length > ImageAttachmentRules.MaxBytes)
            {
                throw new CodeActException(ToolErrorCodes.InvalidArguments,
                    $"Image file is too large: {fullPath} is {file.Length} bytes, " +
                    $"the limit is {ImageAttachmentRules.MaxBytes} bytes.");
            }

            byte[] content;
            try
            {
                content = await File.ReadAllBytesAsync(fullPath, _ct);
            }
            catch (OperationCanceledException) when (_ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CodeActException(ToolErrorCodes.ServiceError,
                    $"Failed to read image file: {fullPath}", ex.ToString());
            }

            return await SaveImageAsync(content, mimeType, fullPath);
        }

        /// <summary>
        /// 把内存中的图像字节附加到本次工具结果，返回显示 URL。
        /// 只接受聊天图像规则支持的格式（PNG / JPEG / WebP / GIF），格式按文件头校验。
        /// </summary>
        public async Task<string> AddImageAsync(byte[] content, string mimeType)
        {
            EnsureImageOutputEnabled();

            if (content is not { Length: > 0 })
                throw new CodeActException(ToolErrorCodes.InvalidArguments, "Image content is empty.");

            return await SaveImageAsync(content, mimeType, "<byte[]>");
        }

        private void EnsureImageOutputEnabled()
        {
            if (!_imageOutputEnabled)
            {
                throw new CodeActException(
                    ToolErrorCodes.ImageOutputDisabled,
                    "Image output is disabled for this CodeAct node. Enable it in the node properties to attach images.");
            }
        }

        private async Task<string> SaveImageAsync(byte[] content, string? declaredMimeType, string source)
        {
            if (content.Length > ImageAttachmentRules.MaxBytes)
            {
                throw new CodeActException(ToolErrorCodes.InvalidArguments,
                    $"Image is too large: {content.Length} bytes, the limit is {ImageAttachmentRules.MaxBytes} bytes.");
            }

            var detectedMimeType = FileSystemImageReadToolV2.DetectImageMimeType(content);
            if (detectedMimeType == null)
            {
                throw new CodeActException(ToolErrorCodes.InvalidArguments,
                    $"Unsupported or unrecognized image format in {source}. " +
                    $"Supported formats: {string.Join(", ", ImageAttachmentRules.SupportedMimeTypes)}.");
            }

            if (!string.IsNullOrWhiteSpace(declaredMimeType))
            {
                var declared = declaredMimeType.Trim();
                if (!ImageAttachmentRules.IsSupportedMimeType(declared))
                {
                    throw new CodeActException(ToolErrorCodes.InvalidArguments,
                        $"Unsupported image MIME type: '{declared}'. " +
                        $"Supported formats: {string.Join(", ", ImageAttachmentRules.SupportedMimeTypes)}.");
                }

                if (!string.Equals(declared, detectedMimeType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CodeActException(ToolErrorCodes.InvalidArguments,
                        $"Declared image MIME type '{declared}' does not match the file content ('{detectedMimeType}').");
                }
            }

            if (_imageAttachments == null)
            {
                throw new CodeActException(ToolErrorCodes.ConfigInvalid,
                    "Image storage is unavailable in the current session, so images cannot be attached.");
            }

            if (_images.Count >= MaxImages)
            {
                throw new CodeActException(ToolErrorCodes.OutputWriteFailed,
                    $"Image limit reached: at most {MaxImages} images can be attached to one CodeAct result.");
            }

            MessageImage saved;
            try
            {
                using var stream = new MemoryStream(content, writable: false);
                saved = await _imageAttachments.SaveGeneratedAsync(stream, detectedMimeType, _ct);
            }
            catch (OperationCanceledException) when (_ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CodeActException(ToolErrorCodes.OutputWriteFailed,
                    $"Failed to save image from {source}.", ex.ToString());
            }

            var url = _imageAttachments.GetDisplayUrl(saved);
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new CodeActException(ToolErrorCodes.OutputWriteFailed,
                    "The image was saved but no display URL could be resolved.",
                    $"FileName={saved.FileName}, MimeType={saved.MimeType}");
            }

            _images.Add(saved);
            _imageUrls.Add(url);
            return url;
        }

        /// <summary>被调用工具带回的图像自动随本次结果回传；关闭图像输出时只计数并留痕。</summary>
        private void ForwardImages(ToolResult result)
        {
            if (result.Images is not { Count: > 0 } images)
                return;

            if (!_imageOutputEnabled)
            {
                _omittedImages += images.Count;
                return;
            }

            foreach (var image in images)
            {
                if (_images.Count >= MaxImages)
                {
                    _limitSkippedImages++;
                    continue;
                }

                _images.Add(image);
                _imageUrls.Add(_imageAttachments?.GetDisplayUrl(image) ?? "");
            }
        }

        private void BuildToolIndex()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var tool in _allTools)
            {
                // CodeAct 工具不进入自己的可调用列表，避免脚本递归调用自身。
                if (tool is CodeActToolV2)
                    continue;

                Tool definition;
                try
                {
                    definition = tool.GetDefinition();
                }
                catch (Exception ex)
                {
                    // 边界：第三方工具的元数据可能抛异常。记录完整错误并跳过，
                    // 不能让一个坏工具拖垮整个脚本环境。
                    Env.DebugOutput.Write("CodeAct", "ToolDefinition",
                        $"Failed to read tool definition from '{tool.GetType().FullName}': {ex}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.name) || !seen.Add(definition.name))
                    continue;

                _tools.Add((definition, tool));
            }
        }

        private (Tool Definition, IToolV2 Tool) FindTool(string toolName)
        {
            foreach (var entry in _tools)
            {
                if (string.Equals(entry.Definition.name, toolName, StringComparison.Ordinal))
                    return entry;
            }

            var available = _tools.Count == 0
                ? "(none)"
                : string.Join(", ", _tools.Select(t => t.Definition.name));

            throw new CodeActException(ToolErrorCodes.InvalidArguments,
                $"Tool '{toolName}' is not available. Available tools: {available}.");
        }

        /// <summary>
        /// 字符串参数按“原始 JSON”处理，方便脚本直接写 <c>CallAsync("x", "{...}")</c>；
        /// 其它对象统一序列化成 JSON。非法 JSON 会由目标工具显式报参数错误。
        /// </summary>
        private static string SerializeArguments(object? arguments) => arguments switch
        {
            null => "{}",
            string raw when !string.IsNullOrWhiteSpace(raw) => raw,
            string => "{}",
            _ => JsonConvert.SerializeObject(arguments)
        };
    }
}
