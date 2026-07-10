using Microsoft.CodeAnalysis.CSharp.Scripting;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin
{
    /// <summary>
    /// IAutoCreateToolV2 版本。无外部依赖，Create 直接 new。
    /// </summary>
    public class InvokeCSharpV2 : IAutoCreateToolV2
    {
        public static string NameKey => "tool.eval_csharp";
        public static string DescriptionKey => "tool.eval_csharp.desc";
        public static string[] CategoryKeys => ["category.code"];

        public static IAutoCreateToolV2 Create(PersistentEnv env) => new InvokeCSharpV2();

        public Tool GetDefinition() => new()
        {
            description = "Eval Csharp using CSharpScript.EvaluateAsync",
            name = "EvalCsharp",
            parameters =
            [
                (new ToolParameter { name = "code", type = ParameterType.String, description = "C# expression to eval." }, true),
                (new ToolParameter { name = "using", type = ParameterType.String, description = "Namespaces separated by comma." }, true)
            ]
        };

        public async Task<string> ExecuteAsync(string input)
        {
            var arguments = JsonConvert.DeserializeObject<Dictionary<string, string>>(input);
            if (arguments != null &&
                arguments.TryGetValue("code", out var code) &&
                arguments.TryGetValue("using", out var @using))
            {
                var scriptOptions = Microsoft.CodeAnalysis.Scripting.ScriptOptions.Default;
                if (!string.IsNullOrWhiteSpace(@using))
                    scriptOptions = scriptOptions.WithImports(@using.Split(',').Select(s => s.Trim()));

                try
                {
                    return (await CSharpScript.EvaluateAsync<dynamic>(code, scriptOptions)).ToString();
                }
                catch (Exception ex) { return ex.Message; }
            }
            throw new InvalidOperationException("Invalid input: code and using required.");
        }
    }
}
