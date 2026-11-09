using ShimmerChatBuiltin.ToolManage;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Options
{
    /// <summary>预生成预设（CallNode 内联的节点树模板）。</summary>
    public sealed class PreGenerationPresetOptionsProvider : KVListOptionsProvider<PreGenerationPreset>
    {
        public override string Key => "pre_generation_presets";

        protected override string SpaceId => "GenerationManager";

        protected override string StoreKey => "generation_presets";

        protected override string GetId(PreGenerationPreset item, IServiceProvider services) => item.Id;

        protected override string GetLabel(PreGenerationPreset item, IServiceProvider services) => item.Name;
    }

    /// <summary>后生成预设（PostCallNode 内联的节点树模板）。</summary>
    public sealed class PostGenerationPresetOptionsProvider : KVListOptionsProvider<PostGenerationPreset>
    {
        public override string Key => "post_generation_presets";

        protected override string SpaceId => "PostGenerationManager";

        protected override string StoreKey => "post_generation_presets";

        protected override string GetId(PostGenerationPreset item, IServiceProvider services) => item.Id;

        protected override string GetLabel(PostGenerationPreset item, IServiceProvider services) => item.Name;
    }

    /// <summary>渲染修改预设（RenderCallNode 内联的节点树模板）。</summary>
    public sealed class RenderModifierPresetOptionsProvider : KVListOptionsProvider<RenderModifierPreset>
    {
        public override string Key => "render_modifier_presets";

        protected override string SpaceId => "RenderModifierManager";

        protected override string StoreKey => "render_modifier_presets";

        protected override string GetId(RenderModifierPreset item, IServiceProvider services) => item.Id;

        protected override string GetLabel(RenderModifierPreset item, IServiceProvider services) => item.Name;
    }

    /// <summary>工具预设（ToolPresetNode 加载的工具集合）。</summary>
    public sealed class ToolPresetOptionsProvider : KVListOptionsProvider<ToolPreset>
    {
        public override string Key => "tool_presets";

        protected override string SpaceId => "ToolPresets";

        protected override string StoreKey => "__presets__";

        protected override string GetId(ToolPreset item, IServiceProvider services) => item.Id;

        protected override string GetLabel(ToolPreset item, IServiceProvider services)
        {
            if (!item.IsDefault)
                return item.Name;

            // 默认预设有独立语义（PresetId 留空即使用它），在列表里标出来。
            var badge = OptionsProviderLoc.Get(services, "editor.tool_preset.default_badge", "(default)");
            return $"{item.Name} {badge}";
        }

        protected override string? GetDescription(ToolPreset item, IServiceProvider services) =>
            OptionsProviderLoc.Format(services, "options.tool_preset.desc", "{0} tools",
                item.EnabledToolTypeNames.Count);

        protected override IReadOnlyList<string>? GetSearchTerms(ToolPreset item, IServiceProvider services) =>
            item.EnabledToolTypeNames;
    }
}
