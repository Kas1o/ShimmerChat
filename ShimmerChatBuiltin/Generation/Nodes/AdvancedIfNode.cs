using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using SharperLLM.Util;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 高级条件分支节点：接受 C# 布尔表达式，可访问 Fragments、SharedState、KVData。
    /// </summary>
    [NodeInfo("node.advanced_condition", Icon = "◆", Color = "var(--node-branch)", CategoryKeys = ["category.flow", "category.branching"])]
    public class AdvancedIfNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Advanced If";

        [NodeProperty("prop.adv_if.condition", HintKey = "prop.adv_if.condition.hint", MultiLine = true, Order = 1)]
        public string Condition { get; set; } = "";

        [NodeProperty("prop.adv_if.usings", HintKey = "prop.adv_if.usings.hint", Order = 2)]
        public string Usings { get; set; } = "";

        [NodeProperty("prop.adv_if.then", Order = 3)]
        public IGenerationNode? Then { get; set; }

        [NodeProperty("prop.adv_if.else", Order = 4)]
        public IGenerationNode? Else { get; set; }

        private static readonly ScriptOptions DefaultScriptOptions = ScriptOptions.Default
            .WithImports("System.Linq", "SharperLLM.Util", "System.Collections.Generic")
            .WithReferences(typeof(Enumerable).Assembly, typeof(PromptBuilder).Assembly);

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var globals = new IfGlobals
            {
                Fragments = context.Env.Transient.Fragments,
                SharedState = context.Env.Transient.SharedState,
                KVData = context.Env.Persistent.KVData
            };

            var options = DefaultScriptOptions;
            if (!string.IsNullOrWhiteSpace(Usings))
            {
                var extra = Usings.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
                options = options.WithImports(options.Imports.Concat(extra));
            }

            bool result;
            try
            {
                result = await CSharpScript.EvaluateAsync<bool>(Condition, options, globals);
            }
            catch (CompilationErrorException ex)
            {
                return NodeResult.Failure(
                    NodeErrorCodes.ParseError,
                    $"AdvancedIfNode: compilation error — {string.Join("; ", ex.Diagnostics)}",
                    ex.ToString(), Id, Name);
            }
            catch (Exception ex)
            {
                return NodeResult.Failure(
                    NodeErrorCodes.ParseError,
                    $"AdvancedIfNode: runtime error — {ex.Message}",
                    ex.ToString(), Id, Name);
            }

            if (result && Then != null)
                return await ExecuteChild(Then, context);
            if (!result && Else != null)
                return await ExecuteChild(Else, context);

            return NodeResult.SuccessResult();
        }

        private async Task<NodeResult> ExecuteChild(IGenerationNode child, NodeExecutionContext context)
        {
            var result = await child.ExecuteAsync(context);
            if (!result.Success)
            {
                result.NodeId ??= Id;
                result.NodeName ??= Name;
            }
            return result;
        }
    }

    /// <summary>
    /// AdvancedIfNode 脚本可访问的全局变量。
    /// </summary>
    public class IfGlobals
    {
        public List<ContextSegment> Fragments { get; set; } = new();
        public Dictionary<string, object> SharedState { get; set; } = new();
        public IKVDataService KVData { get; set; } = default!;
    }
}
