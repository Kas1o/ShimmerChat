using System.Text.RegularExpressions;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 对文本内容执行正则表达式替换。每个节点处理一条替换规则，
    /// 多条规则通过添加多个节点实现。
    /// </summary>
    [NodeInfo("node.regex_replace",
        Icon = "🔤",
        Color = "var(--node-tool)",
        CategoryKeys = ["category.render"],
        DescriptionKey = "node.regex_replace.desc")]
    public class RegexReplaceNode : IRenderModifierNode
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        [NodeProperty("prop.node.name", Order = -100)]
        public string Name { get; set; } = "Regex Replace";

        [NodeProperty("prop.regex.pattern", Order = 10)]
        public string Pattern { get; set; } = "";

        [NodeProperty("prop.regex.replacement", Order = 20)]
        public string Replacement { get; set; } = "";

        [NodeProperty("prop.regex.ignore_case", Order = 30)]
        public bool IgnoreCase { get; set; }

        [NodeProperty("prop.regex.multiline", Order = 40)]
        public bool Multiline { get; set; }

        [NodeProperty("prop.regex.singleline", Order = 50)]
        public bool Singleline { get; set; }

        public Task<RenderNodeResult> ExecuteAsync(RenderNodeExecutionContext context)
        {
            if (string.IsNullOrEmpty(Pattern))
                return Task.FromResult(RenderNodeResult.SuccessResult(context.Env.GetContent()));

            try
            {
                var options = RegexOptions.None;
                if (IgnoreCase) options |= RegexOptions.IgnoreCase;
                if (Multiline) options |= RegexOptions.Multiline;
                if (Singleline) options |= RegexOptions.Singleline;

                var result = Regex.Replace(context.Env.GetContent(), Pattern, Replacement ?? "", options);
                return Task.FromResult(RenderNodeResult.SuccessResult(result));
            }
            catch (RegexParseException)
            {
                return Task.FromResult(RenderNodeResult.SuccessResult(context.Env.GetContent()));
            }
        }
    }
}
