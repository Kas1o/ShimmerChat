using ShimmerChatBuiltin.Options;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.SubAgent.Options
{
    /// <summary>SubAgent 配置下拉：副标题带上输出模式与预设来源，替代原先三个自定义编辑器里的摘要行。</summary>
    public sealed class SubAgentConfigOptionsProvider : KVListOptionsProvider<SubAgentConfig>
    {
        public override string Key => "subagent_configs";

        protected override string SpaceId => "SubAgent";

        protected override string StoreKey => "configs";

        // 节点按 Guid 字符串匹配配置（SubAgentNode.ConfigGuid）。
        protected override string GetId(SubAgentConfig item, IServiceProvider services) => item.Guid.ToString();

        protected override string GetLabel(SubAgentConfig item, IServiceProvider services) => item.Name;

        protected override string? GetDescription(SubAgentConfig item, IServiceProvider services)
        {
            var output = OptionsProviderLoc.Format(services, "editor_subagent.output_label", "Output: {0}",
                string.IsNullOrWhiteSpace(item.OutputMode) ? "-" : item.OutputMode);

            var preset = item.UseSharedPreset
                ? OptionsProviderLoc.Format(services, "editor_subagent.preset_label", " | Preset: {0}",
                    string.IsNullOrWhiteSpace(item.ModifierPresetId)
                        ? OptionsProviderLoc.Get(services, "editor_subagent.none", "none")
                        : item.ModifierPresetId)
                : OptionsProviderLoc.Get(services, "editor_subagent.private_tree_label", " | Private tree");

            return output + preset;
        }

        protected override IReadOnlyList<string>? GetSearchTerms(SubAgentConfig item, IServiceProvider services) =>
            string.IsNullOrWhiteSpace(item.ModifierPresetId) ? null : [item.ModifierPresetId];
    }

    /// <summary>
    /// SubAgent 输出模式下拉。属性留空表示「继承配置中的值」，
    /// 这里的选项就是 SubAgentFormatter 实际支持的三个取值。
    /// </summary>
    public sealed class SubAgentOutputModeOptionsProvider : INodePropertyOptionsProvider
    {
        public string Key => "subagent_output_modes";

        public IReadOnlyList<NodePropertyOption> GetOptions(IServiceProvider services) =>
        [
            new("LastMessage", "LastMessage", null,
                OptionsProviderLoc.Get(services, "options.subagent_output.last_message",
                    "Inject the last message only")),
            new("FullJson", "FullJson", null,
                OptionsProviderLoc.Get(services, "options.subagent_output.full_json",
                    "Inject the full message JSON")),
            new("None", "None", null,
                OptionsProviderLoc.Get(services, "options.subagent_output.none",
                    "Inject nothing"))
        ];
    }
}
