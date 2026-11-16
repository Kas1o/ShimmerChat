using ShimmerChatBuiltin.ImageGeneration;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Options
{
    /// <summary>
    /// 文生图配置 → 节点属性下拉选项。
    /// 读取与设置页、执行期完全相同的 <see cref="ImageGenerationConfigManager"/>，
    /// 不在这里维护第二份校验规则。配置损坏时返回空集合并写日志。
    /// </summary>
    public sealed class ImageGenerationConfigOptionsProvider : INodePropertyOptionsProvider
    {
        /// <inheritdoc/>
        public string Key => "image_generation_configs";

        public IReadOnlyList<NodePropertyOption> GetOptions(IServiceProvider services)
        {
            if (services.GetService(typeof(IKVDataService)) is not IKVDataService kv)
            {
                OptionsProviderLog.Error(services, GetType(),
                    $"选项提供者 {Key} 需要 IKVDataService，但服务容器里没有注册；该属性的选项将为空。");
                return [];
            }

            ImageGenerationConfig? config;
            string? error;
            try
            {
                (config, error) = ImageGenerationConfigManager.Load(kv);
            }
            catch (Exception ex)
            {
                OptionsProviderLog.Error(services, GetType(), ex,
                    "读取文生图配置失败，节点属性「文生图配置」的选项将为空。");
                return [];
            }

            if (config == null)
            {
                OptionsProviderLog.Error(services, GetType(),
                    $"文生图配置无效（{error}），节点属性「文生图配置」的选项将为空。");
                return [];
            }

            if (config.Profiles.Count == 0)
                return [];

            var disabled = OptionsProviderLoc.Get(services, "options.disabled", "Disabled");
            var enabledGroup = OptionsProviderLoc.Get(services, "options.image_config.enabled", "Enabled");
            var disabledGroup = OptionsProviderLoc.Get(services, "options.image_config.disabled", "Disabled");
            var customMode = OptionsProviderLoc.Get(services, "options.image_config.custom", "Custom workflow");
            var defaultMode = OptionsProviderLoc.Get(services, "options.image_config.default", "Default workflow");

            return config.Profiles.Select(profile =>
            {
                var label = string.IsNullOrWhiteSpace(profile.Name) ? profile.Id : profile.Name;
                var descriptor = ImageApiProviderCatalog.Find(profile.Provider);
                var providerName = descriptor == null
                    ? profile.Provider.ToString()
                    : OptionsProviderLoc.Get(services, descriptor.NameKey, descriptor.DisplayName);

                var mode = profile.Provider == ImageApiProvider.ComfyUi
                           && profile.WorkflowMode == ImageWorkflowMode.Custom
                    ? customMode
                    : defaultMode;

                var description = $"{providerName} · {mode} · {profile.BaseUrl}";
                if (!string.IsNullOrWhiteSpace(profile.Description))
                    description = $"{description} · {profile.Description}";

                if (!profile.Enabled)
                    description = $"{disabled} · {description}";

                return new NodePropertyOption(
                    profile.Id,
                    label,
                    profile.Enabled ? enabledGroup : disabledGroup,
                    description,
                    [
                        profile.Id,
                        profile.Name,
                        ImageGenerationConfigManager.GetToolName(profile.Id),
                        providerName,
                        descriptor?.DisplayName ?? profile.Provider.ToString(),
                        profile.BaseUrl,
                        .. (profile.CustomWorkflowParameters ?? [])
                            .Where(p => p != null)
                            .Select(p => p.Name)
                    ]);
            }).ToList();
        }
    }
}
