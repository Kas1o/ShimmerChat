using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShimmerChatBuiltin.ImageGeneration
{
    /// <summary>
    /// 自定义工作流模板引擎。
    /// <para>
    /// 语法只有一种：<c>{{参数名}}</c>，参数名来自工作流声明的工具参数。
    /// 替换时根据占位符是否位于 JSON 字符串字面量内决定转义方式：
    /// 字符串内做 JSON 转义，字符串外输出合法 JSON 字面量（数字 / true / false / 带引号字符串）。
    /// 因此同一份模板既能写 <c>"steps": {{steps}}</c>，也能写 <c>"text": "{{prompt}}"</c>。
    /// </para>
    /// <para>
    /// 模板里的占位符必须全部由参数表声明，参数表里的每个参数也必须在模板中出现；
    /// 任何一边多出来都视为配置错误，由 <see cref="ImageGenerationConfigManager.Validate"/> 拦截。
    /// </para>
    /// </summary>
    public static class WorkflowTemplateEngine
    {
        private const string OpenToken = "{{";
        private const string CloseToken = "}}";

        /// <summary>扫描模板中的占位符名（保留重复项）。模板语法错误时返回错误说明。</summary>
        public static (IReadOnlyList<string>? Names, string? Error) FindPlaceholders(string template)
        {
            var names = new List<string>();
            var error = Walk(template, output: null, (name, _) => names.Add(name));
            return error == null ? (names, null) : (null, error);
        }

        /// <summary>
        /// 把模型参数 JSON 绑定到声明的参数表。
        /// 未声明的参数、缺失的必填参数、类型不符或超出范围都会返回错误，不做静默修正。
        /// </summary>
        public static (Dictionary<string, object>? Values, string? Error) Bind(
            IReadOnlyList<ImageWorkflowParameter> parameters, JObject arguments)
        {
            var declared = parameters
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);

            var unknown = arguments.Properties()
                .Where(p => !declared.Contains(p.Name))
                .Select(p => p.Name)
                .ToList();

            if (unknown.Count > 0)
            {
                var expected = parameters.Count == 0
                    ? "(none)"
                    : string.Join(", ", parameters.Select(p => p.Name));

                return (null,
                    $"Unknown parameter(s): {string.Join(", ", unknown)}. Declared parameters: {expected}.");
            }

            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var parameter in parameters)
            {
                var token = arguments[parameter.Name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    if (parameter.Required)
                        return (null, $"Missing required parameter '{parameter.Name}'.");

                    var (defaultValue, defaultError) =
                        ImageWorkflowParameterRules.ParseText(parameter, parameter.DefaultValue);
                    if (defaultError != null)
                        return (null, defaultError);

                    values[parameter.Name] = defaultValue!;
                    continue;
                }

                var (value, error) = ConvertToken(parameter, token);
                if (error != null)
                    return (null, error);

                values[parameter.Name] = value!;
            }

            return (values, null);
        }

        /// <summary>套用模板并解析为工作流 JSON。任何未替换的占位符 / 非法 JSON 都返回错误。</summary>
        public static (JObject? Workflow, string? Error) Render(
            string template, IReadOnlyDictionary<string, object> values)
        {
            var builder = new StringBuilder(template.Length + 128);
            string? missing = null;

            var walkError = Walk(template, builder, (name, inString) =>
            {
                if (missing != null) return;

                if (!values.TryGetValue(name, out var value))
                {
                    missing = name;
                    return;
                }

                builder.Append(FormatValue(value, inString));
            });

            if (walkError != null)
                return (null, walkError);

            if (missing != null)
                return (null, $"Workflow template uses undeclared placeholder '{missing}'.");

            try
            {
                if (JToken.Parse(builder.ToString()) is not JObject workflow)
                    return (null, "Rendered workflow is not a JSON object.");

                return (workflow, null);
            }
            catch (JsonException ex)
            {
                return (null, $"Rendered workflow is not valid JSON: {ex.Message}");
            }
        }

        /// <summary>
        /// 通用扫描：复制字面量、识别占位符并回调。返回模板语法错误（未闭合 / 非法名称）。
        /// </summary>
        private static string? Walk(string template, StringBuilder? output, Action<string, bool> onPlaceholder)
        {
            var inString = false;

            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];

                if (inString)
                {
                    // JSON 字符串里的转义序列原样保留，避免把 \" 当成字符串结束
                    if (c == '\\' && i + 1 < template.Length)
                    {
                        output?.Append(c).Append(template[i + 1]);
                        i++;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                        output?.Append(c);
                        continue;
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inString = true;
                        output?.Append(c);
                        continue;
                    }
                }

                if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    var close = template.IndexOf(CloseToken, i + OpenToken.Length, StringComparison.Ordinal);
                    if (close < 0)
                        return $"Unterminated placeholder starting at index {i}.";

                    var name = template[(i + OpenToken.Length)..close].Trim();
                    if (!ImageWorkflowParameterRules.NamePattern.IsMatch(name))
                    {
                        return $"Invalid placeholder '{{{{{name}}}}}' at index {i}. " +
                               "Parameter names must start with a letter or underscore and contain only letters, digits and underscores.";
                    }

                    onPlaceholder(name, inString);
                    i = close + CloseToken.Length - 1;
                    continue;
                }

                output?.Append(c);
            }

            return null;
        }

        private static (object? Value, string? Error) ConvertToken(
            ImageWorkflowParameter parameter, JToken token)
        {
            switch (parameter.Type)
            {
                case ImageWorkflowParameterType.String:
                case ImageWorkflowParameterType.Enum:
                    if (token.Type != JTokenType.String)
                        return (null, $"Parameter '{parameter.Name}' must be a string; got {token.Type}.");
                    return ImageWorkflowParameterRules.ParseText(parameter, token.Value<string>());

                case ImageWorkflowParameterType.Integer:
                    if (token.Type is JTokenType.Integer or JTokenType.Float)
                        return ImageWorkflowParameterRules.ParseText(
                            parameter, token.Value<decimal>().ToString(CultureInfo.InvariantCulture));
                    if (token.Type == JTokenType.String)
                        return ImageWorkflowParameterRules.ParseText(parameter, token.Value<string>());
                    return (null, $"Parameter '{parameter.Name}' must be an integer; got {token.Type}.");

                case ImageWorkflowParameterType.Number:
                    if (token.Type is JTokenType.Integer or JTokenType.Float)
                        return ImageWorkflowParameterRules.ParseText(
                            parameter, token.Value<double>().ToString("R", CultureInfo.InvariantCulture));
                    if (token.Type == JTokenType.String)
                        return ImageWorkflowParameterRules.ParseText(parameter, token.Value<string>());
                    return (null, $"Parameter '{parameter.Name}' must be a number; got {token.Type}.");

                case ImageWorkflowParameterType.Boolean:
                    if (token.Type == JTokenType.Boolean)
                        return ImageWorkflowParameterRules.ParseText(
                            parameter, token.Value<bool>() ? "true" : "false");
                    if (token.Type == JTokenType.String)
                        return ImageWorkflowParameterRules.ParseText(parameter, token.Value<string>());
                    return (null, $"Parameter '{parameter.Name}' must be a boolean; got {token.Type}.");

                default:
                    return (null, $"Parameter '{parameter.Name}' has unsupported type {parameter.Type}.");
            }
        }

        /// <summary>占位符在字符串内时只输出转义后的内容，在字符串外时输出完整 JSON 字面量。</summary>
        private static string FormatValue(object value, bool inString)
        {
            if (value is not (string or long or double or bool))
                throw new InvalidOperationException(
                    $"Unsupported workflow parameter value type: {value.GetType().FullName}.");

            if (inString)
            {
                var text = value switch
                {
                    string s => s,
                    long l => l.ToString(CultureInfo.InvariantCulture),
                    double d => d.ToString("R", CultureInfo.InvariantCulture),
                    bool b => b ? "true" : "false",
                    _ => throw new InvalidOperationException()
                };

                var quoted = JsonConvert.ToString(text);
                return quoted[1..^1];
            }

            return value switch
            {
                string s => JsonConvert.ToString(s),
                long l => l.ToString(CultureInfo.InvariantCulture),
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                _ => throw new InvalidOperationException()
            };
        }
    }
}
