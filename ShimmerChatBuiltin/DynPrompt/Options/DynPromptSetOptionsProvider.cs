using ShimmerChatBuiltin.Options;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.DynPrompt.Options
{
    /// <summary>
    /// DynPrompt 集合下拉。节点的 SetName 是按名字匹配的（不是 Id），
    /// 所以 Value 用集合名；规则名作为额外搜索词，方便按内容找集合。
    /// </summary>
    public sealed class DynPromptSetOptionsProvider : KVListOptionsProvider<DynPromptSet>
    {
        public override string Key => "dyn_prompt_sets";

        protected override string SpaceId => "DynPrompt";

        protected override string StoreKey => "DynPromptSets";

        protected override string GetId(DynPromptSet item, IServiceProvider services) => item.Name;

        protected override string GetLabel(DynPromptSet item, IServiceProvider services) => item.Name;

        protected override string? GetDescription(DynPromptSet item, IServiceProvider services) =>
            OptionsProviderLoc.Format(services, "options.dyn_prompt_set.desc", "{0} rules", item.Terms.Count);

        protected override IReadOnlyList<string>? GetSearchTerms(DynPromptSet item, IServiceProvider services) =>
            item.Terms.Select(t => t.Name).ToList();
    }
}
