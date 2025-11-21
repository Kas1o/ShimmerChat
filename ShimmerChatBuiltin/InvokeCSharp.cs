using ShimmerChatLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.FunctionCalling;

namespace ShimmerChatBuiltinTools
{
	public class InvokeCSharp : ITool
	{
		public async Task<string> Execute(string input)
		{
			var arguments = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(input);
			if (
				arguments.TryGetValue("code", out string code) &&
				arguments.TryGetValue("using",out string @using)
			)
			{
				var scriptOptions = Microsoft.CodeAnalysis.Scripting.ScriptOptions.Default;
				if (!string.IsNullOrWhiteSpace(@using))
				{
					scriptOptions = scriptOptions.WithImports(@using.Split(',').Select(s => s.Trim()));
				}
				try
				{
					string output = (await CSharpScript.EvaluateAsync<dynamic>(code, scriptOptions)).ToString();
					return output;
				}
				catch(Exception ex)
				{
					return ex.Message;
				}
			}
			throw new InvalidOperationException();
		}

		public Tool GetToolDefinition() => new Tool
		{
			description = "Eval Csharp using CSharpScript.EvaluateAsync",
			name = "EvalCsharp",
			parameters = 
			[
				(new ToolParameter{
					name = "code",
					type = ParameterType.String,
					description = "expression for eval."
				},true),(new ToolParameter{
					name = "using",
					type = ParameterType.String,
					description = "using namespace. (ScriptOptions.WithImports) (separate by ',') (use System At least?)"
				},true)
			]
		};
	}
}
