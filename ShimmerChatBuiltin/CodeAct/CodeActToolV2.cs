using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.CodeAct
{
    /// <summary>
    /// CodeAct 版 C# 工具。与旧的 <see cref="InvokeCSharpV2"/> 并行存在：
    /// 旧工具保持原行为，本工具把“执行代码”升级为“在代码里组合调用本轮所有工具”。
    /// <para>
    /// 工具实例由 <see cref="CodeActNode"/> 创建，持有本轮 <c>TransientEnv.Tools</c> 的
    /// <b>活列表引用</b>：执行时枚举，因此同一条前生成管线里后添加的工具也可见。
    /// 不实现 <see cref="IAutoCreateToolV2"/>——通用工具节点无法拿到本轮工具列表，
    /// 让它出现在工具预设里只会得到一个无法工作的工具。
    /// </para>
    /// <para>
    /// <b>安全边界</b>：脚本在宿主进程内全权限执行，没有沙箱。这是 CodeAct 的设计前提，
    /// 不是配置项；需要隔离时不要把本节点加进生成树。
    /// </para>
    /// </summary>
    public sealed class CodeActToolV2 : IToolV2, IToolV3
    {
        /// <summary>暴露给模型的工具名。</summary>
        public const string ToolName = "codeact_csharp";

        /// <summary>返回给模型的文本上限；超出部分显式截断并标注。</summary>
        public const int MaxResultChars = 64_000;

        /// <summary>编译错误回给模型的文本上限；完整诊断放入 Details。</summary>
        private const int MaxCompilationMessageChars = 8_000;

        private static readonly string[] DefaultImports =
        [
            "System",
            "System.Collections.Generic",
            "System.IO",
            "System.Linq",
            "System.Text",
            "System.Threading",
            "System.Threading.Tasks",
            "Newtonsoft.Json",
            "ShimmerChatLib",
            "ShimmerChatLib.Generation",
            "ShimmerChatLib.Interface",
            "ShimmerChatBuiltin.CodeAct"
        ];

        private readonly IReadOnlyList<IToolV2> _availableTools;
        private readonly PersistentEnv _env;
        private readonly bool _imageOutputEnabled;

        public CodeActToolV2(
            IReadOnlyList<IToolV2> availableTools,
            PersistentEnv env,
            bool imageOutputEnabled = true)
        {
            _availableTools = availableTools;
            _env = env;
            _imageOutputEnabled = imageOutputEnabled;
        }

        public Tool GetDefinition()
        {
            var available = DescribeAvailableTools();

            return new Tool
            {
                name = ToolName,
                description =
                    "Execute a complete C# script in-process with access to the current conversation environment. " +
                    "Top-level statements are allowed. Inside the script:\n" +
                    "- await CallAsync(\"tool_name\", new { arg = value }) invokes a tool and returns its text result; " +
                    "it throws CodeActException when the tool reports a failure.\n" +
                    "- await CallToolAsync(\"tool_name\", args) returns the full ToolResult (Status / ErrorCode / Content) " +
                    "without throwing on tool failure.\n" +
                    "- Tools lists callable tool names; GetToolInfo(\"name\") shows a tool's description and parameters.\n" +
                    "- Images is the result image container. Tool images are NOT added automatically: after " +
                    "var r = await CallToolAsync(...), call Images.Add(r) or Images.Add(r.Images); " +
                    "Images.Add(image) also accepts a single MessageImage. Only images in Images are returned with this result.\n" +
                    (this._imageOutputEnabled
                        ? "- await AddImageAsync(\"C:\\\\path.png\") or AddImageAsync(byte[] bytes, \"image/png\") saves an image " +
                          "and adds it to Images, returning its display URL.\n"
                        : "- Image output is disabled for this tool: Images.Add(...) and AddImageAsync(...) throw.\n") +
                    "- Ct is this round's cancellation token; pass it to async calls and call Ct.ThrowIfCancellationRequested() " +
                    "inside long loops (cancellation of synchronous user code cannot be forced by the host).\n" +
                    "- Env, Chat, Agent and KVData expose the current session. Print(value) appends a line to the tool output.\n" +
                    "The value of the last expression is returned to you; stdout printed via Print is appended after it.\n" +
                    $"Available tools: {available}",
                parameters =
                [
                    (new ToolParameter
                    {
                        name = "code",
                        type = ParameterType.String,
                        description = "Complete C# script to execute. Top-level statements and top-level await are allowed; " +
                                      "the value of the last expression is returned."
                    }, true),
                    (new ToolParameter
                    {
                        name = "using",
                        type = ParameterType.String,
                        description = "Optional comma-separated namespaces added to the default imports " +
                                      "(System, System.IO, System.Linq, System.Text, System.Threading, " +
                                      "System.Threading.Tasks, Newtonsoft.Json, ShimmerChatLib and ShimmerChatLib.Generation)."
                    }, false)
                ]
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!context.TryGetArguments<CodeActArguments>(out var arguments, out var argumentError)
                || arguments == null)
            {
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    argumentError ?? "Invalid arguments for codeact_csharp.");
            }

            var code = arguments.code;
            if (string.IsNullOrWhiteSpace(code))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, "'code' is required and must not be empty.");

            var imports = ParseImports(arguments.@using);

            Script<object> script;
            try
            {
                var options = BuildScriptOptions(imports);
                script = CSharpScript.Create<object>(code, options, typeof(CodeActGlobals));
            }
            catch (Exception ex)
            {
                return ToolResult.Fail(ToolErrorCodes.CodeCompilationError,
                    $"Failed to create the C# script: {ex.Message}", ex.ToString());
            }

            try
            {
                // 先编译再运行：把语法/引用错误变成可读的结构化失败，
                // 而不是让异常从 RunAsync 里冒出来丢失诊断位置。
                var diagnostics = script.Compile(ct);
                var errors = diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();

                if (errors.Count > 0)
                {
                    var full = string.Join("\n", errors.Select(d => d.ToString()));
                    var message = string.Join("\n", errors.Select(d => d.ToString()));
                    return ToolResult.Fail(
                        ToolErrorCodes.CodeCompilationError,
                        "C# code failed to compile:\n" + Shorten(message, MaxCompilationMessageChars),
                        full);
                }

                var globals = new CodeActGlobals(
                    _env,
                    _availableTools,
                    _imageOutputEnabled,
                    ct,
                    context.ToolCallId,
                    context.RoundIndex);

                var state = await script.RunAsync(globals, ct);
                var returnValue = await UnwrapAsync(state.ReturnValue, ct);

                var content = ComposeContent(returnValue, globals);
                return ToolResult.Ok(
                    content,
                    globals.Images.Items is { Count: > 0 } images ? images : null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 用户/宿主取消：交给执行节点按取消处理，绝不伪装成工具失败继续下一轮。
                throw;
            }
            catch (CompilationErrorException ex)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.CodeCompilationError,
                    "C# code failed to compile:\n" + Shorten(ex.Message, MaxCompilationMessageChars),
                    ex.Diagnostics != null
                        ? string.Join("\n", ex.Diagnostics.Select(d => d.ToString()))
                        : ex.ToString());
            }
            catch (CodeActException ex)
            {
                return ToolResult.Fail(ex.Code, ex.Message, ex.Details);
            }
            catch (Exception ex)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.CodeExecutionError,
                    $"C# code threw {ex.GetType().Name}: {ex.Message}",
                    ex.ToString());
            }
        }

        private string ComposeContent(object? returnValue, CodeActGlobals globals)
        {
            var builder = new StringBuilder();

            var formatted = FormatReturnValue(returnValue);
            if (formatted.Length > 0)
                builder.Append(formatted);

            var output = globals.Output;
            if (output.Length > 0)
            {
                if (builder.Length > 0)
                    builder.AppendLine().AppendLine();

                builder.AppendLine("[stdout]");
                builder.Append(output.TrimEnd());
            }

            if (builder.Length == 0)
                builder.Append("Code executed successfully (no output).");

            var content = builder.ToString();
            if (content.Length <= MaxResultChars)
                return content;

            return content[..MaxResultChars] +
                   $"\n[output truncated: {content.Length} chars total, limit is {MaxResultChars}]";
        }

        private string FormatReturnValue(object? value)
        {
            if (value == null)
                return string.Empty;

            if (value is string text)
                return text;

            try
            {
                return JsonConvert.SerializeObject(value, Formatting.Indented);
            }
            catch (Exception ex)
            {
                // 返回值序列化失败不影响脚本本身的成功；留痕后退回 ToString，
                // 避免把“格式化失败”误报成脚本执行失败。
                _env.DebugOutput.Write("CodeAct", "ResultFormat",
                    $"Failed to JSON-serialize script return value of type '{value.GetType().FullName}': {ex}");
                return value.ToString() ?? string.Empty;
            }
        }

        /// <summary>
        /// 脚本最后一条表达式可能是未 await 的 Task（模型经常忘记 await）。
        /// 这里把它补齐并取出结果，避免把 "System.Threading.Tasks.Task`1[...]" 当返回值。
        /// </summary>
        private static async Task<object?> UnwrapAsync(object? value, CancellationToken ct)
        {
            if (value is not Task task)
                return value;

            await task.WaitAsync(ct);

            var type = task.GetType();
            if (!type.IsGenericType)
                return null;

            return type.GetProperty("Result")?.GetValue(task);
        }

        private static ScriptOptions BuildScriptOptions(IReadOnlyList<string> extraImports)
        {
            var options = ScriptOptions.Default
                .WithOptimizationLevel(OptimizationLevel.Release)
                .AddImports(DefaultImports)
                .AddImports(extraImports);

            // ScriptOptions.Default 已带一套基础引用；只补缺失的宿主程序集，按文件路径去重，
            // 避免"同一程序集引用两次"导致编译错误。
            var existing = new HashSet<string>(
                options.MetadataReferences.Select(r => r.Display ?? ""),
                StringComparer.OrdinalIgnoreCase);

            Assembly[] assemblies =
            [
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Task).Assembly,
                typeof(StringBuilder).Assembly,
                typeof(JsonConvert).Assembly,
                typeof(ToolResult).Assembly,
                typeof(CodeActGlobals).Assembly,

                // 常用 BCL 程序集：ScriptOptions.Default 不保证包含它们，
                // 缺引用时脚本会得到“找不到类型”的编译错误。
                typeof(System.Net.Http.HttpClient).Assembly,
                typeof(System.Text.Json.JsonSerializer).Assembly,
                typeof(System.Text.RegularExpressions.Regex).Assembly,
                typeof(System.Diagnostics.Process).Assembly,
                typeof(System.Security.Cryptography.SHA256).Assembly,
                typeof(System.IO.Compression.ZipFile).Assembly,
                typeof(System.Xml.Linq.XDocument).Assembly,
                typeof(System.Xml.XmlDocument).Assembly,
                typeof(System.Collections.Concurrent.ConcurrentDictionary<,>).Assembly
            ];

            foreach (var assembly in assemblies)
            {
                var location = assembly.Location;
                if (string.IsNullOrEmpty(location) || !existing.Add(location))
                    continue;

                options = options.AddReferences(assembly);
            }

            return options;
        }

        private static List<string> ParseImports(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return [];

            return raw
                .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private string DescribeAvailableTools()
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var tool in _availableTools)
            {
                if (tool is CodeActToolV2)
                    continue;

                try
                {
                    var name = tool.GetDefinition().name;
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                        names.Add(name);
                }
                catch (Exception ex)
                {
                    _env.DebugOutput.Write("CodeAct", "ToolDefinition",
                        $"Failed to read tool definition from '{tool.GetType().FullName}': {ex}");
                }
            }

            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }

        private static string Shorten(string text, int maxChars) =>
            text.Length <= maxChars ? text : text[..maxChars] + $"\n[...truncated, {text.Length} chars total]";

        private sealed class CodeActArguments
        {
            public string? code { get; set; }

            // JSON 参数名是 "using"；C# 保留字用 @ 前缀即可让 Newtonsoft 正常匹配。
            public string? @using { get; set; }
        }
    }
}
