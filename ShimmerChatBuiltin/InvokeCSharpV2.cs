using Microsoft.CodeAnalysis.CSharp.Scripting;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin
{
    /// <summary>
    /// IToolV2 版本的 InvokeCSharp
    /// </summary>
    public class InvokeCSharpV2 : IToolV2
    {
        public string Name => "EvalCsharp";
        public string Description => "Eval C# using CSharpScript.EvaluateAsync.";

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
