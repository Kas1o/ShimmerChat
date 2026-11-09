using Meziantou.Framework.SyntaxHighlighting;
using ShimmerChatBuiltin.Options;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc.Node.Render.Options
{
    /// <summary>
    /// 语法高亮语言下拉。语言表直接来自高亮库（SyntaxHighlighter.GetSupportedLanguages），
    /// 不在这里维护白名单，库升级后自动跟上。
    /// </summary>
    public sealed class HighlightLanguageOptionsProvider : INodePropertyOptionsProvider
    {
        public string Key => "highlight_languages";

        public IReadOnlyList<NodePropertyOption> GetOptions(IServiceProvider services)
        {
            try
            {
                return SyntaxHighlighter.GetSupportedLanguages()
                    .OrderBy(language => language, StringComparer.OrdinalIgnoreCase)
                    .Select(language => new NodePropertyOption(language, language))
                    .ToList();
            }
            catch (Exception ex)
            {
                OptionsProviderLog.Error(services, GetType(),
                    ex, "读取语法高亮语言列表失败，该属性的选项将为空。");
                return [];
            }
        }
    }
}
