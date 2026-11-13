using ShimmerChatBuiltin.ImageGeneration;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Options
{
    /// <summary>
    /// 文生图自定义工作流 → 节点属性下拉选项。
    /// 读取与设置页、执行期完全相同的 <see cref="ImageGenerationConfigManager"/>，
    /// 不在这里维护第二份工作流校验规则。配置损坏时返回空集合并写日志。
    /// </summary>
    public sealed class ImageGenerationWorkflowOptionsProvider : INodePropertyOptionsProvider
    {
        /// <inheritdoc/>
        public string Key => "image_generation_workflows";

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
                    "读取文生图配置失败，节点属性「指定自定义工作流」的选项将为空。");
                return [];
            }

            if (config == null)
            {
                OptionsProviderLog.Error(services, GetType(),
                    $"文生图配置无效（{error}），节点属性「指定自定义工作流」的选项将为空。");
                return [];
            }

            if (config.CustomWorkflows.Count == 0)
                return [];

            var disabled = OptionsProviderLoc.Get(services, "options.disabled", "Disabled");
            var enabledGroup = OptionsProviderLoc.Get(services, "options.image_workflow.enabled", "Enabled");
            var disabledGroup = OptionsProviderLoc.Get(services, "options.image_workflow.disabled", "Disabled");

            return config.CustomWorkflows.Select(workflow =>
            {
                var label = string.IsNullOrWhiteSpace(workflow.Name) ? workflow.Id : workflow.Name;
                var description = string.IsNullOrWhiteSpace(workflow.Description)
                    ? ImageGenerationToolV2.GetCustomToolName(workflow.Id)
                    : workflow.Description;

                if (!workflow.Enabled)
                    description = $"{disabled} · {description}";

                return new NodePropertyOption(
                    workflow.Id,
                    label,
                    workflow.Enabled ? enabledGroup : disabledGroup,
                    description,
                    [
                        workflow.Id,
                        workflow.Name,
                        ImageGenerationToolV2.GetCustomToolName(workflow.Id),
                        .. (workflow.Parameters ?? []).Where(p => p != null).Select(p => p.Name)
                    ]);
            }).ToList();
        }
    }
}
