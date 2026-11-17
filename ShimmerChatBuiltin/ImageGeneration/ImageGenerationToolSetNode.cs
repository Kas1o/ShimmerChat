using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.ImageGeneration
{
    /// <summary>
    /// 文生图工具集节点：把设置面板中启用的一份或多份 <see cref="ImageGenerationProfile"/>
    /// 变成独立工具（<c>generate_image_{id}</c>，Id 为 <c>default</c> 时是 <c>generate_image</c>）。
    /// <para>
    /// 不指定配置时暴露所有启用的配置；指定后只暴露该配置。配置不存在或已停用时节点失败，
    /// 不静默跳过。配置不合法 / 缺少模型 / 当前会话没有图像存储服务时同样直接失败，
    /// 不添加一个执行时必然失败的工具。
    /// </para>
    /// </summary>
    [NodeInfo("node.image_generation_tool_set", Icon = "🎨", Color = "var(--node-tool)",
        CategoryKeys = ["category.tool", "category.image_generation"],
        DescriptionKey = "node.image_generation_tool_set.desc")]
    public class ImageGenerationToolSetNode : IPreGenerationNode
    {

        /// <summary>
        /// 指定只暴露某一份配置；留空表示暴露所有已启用的配置。
        /// 值来自「文生图」设置面板中的配置 Id。
        /// </summary>
        [NodeProperty("prop.image_generation_tool_set.configuration",
            HintKey = "prop.image_generation_tool_set.configuration.hint", Order = 1,
            OptionsKey = "image_generation_configs", OptionsSearchable = true, ShowOptionDescription = true,
            OptionsDefaultLabelKey = "prop.image_generation_tool_set.configuration.default")]
        public string ConfigurationId { get; set; } = "";

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;
            var kvData = context.Env.Persistent.KVData;
            var attachments = context.Env.Persistent.ImageAttachments;

            var (config, configError) = ImageGenerationConfigManager.Load(kvData);
            if (config == null)
            {
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc["node_err.image_generation_config_invalid"],
                    details: configError));
            }

            if (attachments == null)
            {
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ConfigNotFound,
                    loc["node_err.image_generation_attachments_unavailable"]));
            }

            List<ImageGenerationProfile> selected;
            if (string.IsNullOrWhiteSpace(ConfigurationId))
            {
                // 留空 = 多配置模式：把设置页里所有启用的配置都暴露给模型。
                selected = config.Profiles.Where(p => p.Enabled).ToList();
                if (selected.Count == 0)
                {
                    WriteLog(context,
                        $"[{context.CurrentNodeName}] no enabled image generation configuration was found; nothing was added.");
                    return Task.FromResult(NodeResult.SuccessResult());
                }
            }
            else
            {
                var profile = config.FindProfile(ConfigurationId.Trim());
                if (profile == null)
                {
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.ConfigNotFound,
                        loc.Format("node_err.image_generation_config_not_found", ConfigurationId)));
                }

                if (!profile.Enabled)
                {
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.ConfigNotFound,
                        loc.Format("node_err.image_generation_config_disabled", profile.Name, profile.Id)));
                }

                selected = [profile];
            }

            var addedNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var profile in selected)
            {
                if (profile.Provider == ImageApiProvider.ComfyUi
                    && profile.WorkflowMode == ImageWorkflowMode.Default
                    && string.IsNullOrWhiteSpace(profile.DefaultWorkflow.Model))
                {
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.ConfigNotFound,
                        loc.Format("node_err.image_generation_model_missing", profile.Name)));
                }

                var tool = new ImageGenerationToolV2(kvData, attachments, profile.Id);
                var toolName = tool.GetDefinition().name;

                if (!addedNames.Add(toolName))
                    return Task.FromResult(DuplicateToolFailure(loc, toolName));

                context.Env.Transient.Tools.Add(tool);
            }

            return Task.FromResult(NodeResult.SuccessResult());
        }

        private NodeResult DuplicateToolFailure(ILocService loc, string toolName) =>
            NodeResult.Failure(
                NodeErrorCodes.ConfigNotFound,
                loc.Format("node_err.image_generation_duplicate_tool", toolName));

        private void WriteLog(PreNodeExecutionContext context, string message) =>
            context.Env.Persistent.DebugOutput.Write("ImageGenerationToolSetNode", "ImageGeneration", message);
    }
}
