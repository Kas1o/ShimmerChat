using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.ImageGeneration
{
    /// <summary>
    /// 文生图工具集节点：把「默认工作流」工具与设置面板中启用的自定义工作流工具
    /// 加入本次生成的工具列表。每个自定义工作流暴露为独立工具
    /// （<c>generate_image_{id}</c>），工具形参来自该工作流的参数声明。
    /// <para>
    /// 配置不合法 / 默认工作流没选模型 / 当前会话没有图像存储服务时直接失败，
    /// 不添加一个执行时必然失败的工具。
    /// </para>
    /// </summary>
    [NodeInfo("node.image_generation_tool_set", Icon = "🎨", Color = "var(--node-tool)",
        CategoryKeys = ["category.tool", "category.image_generation"],
        DescriptionKey = "node.image_generation_tool_set.desc")]
    public class ImageGenerationToolSetNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Image Generation Tools";

        /// <summary>是否暴露默认工作流工具 <c>generate_image</c>。</summary>
        [NodeProperty("prop.image_generation_tool_set.enable_default",
            HintKey = "prop.image_generation_tool_set.enable_default.hint", Order = 1)]
        public bool EnableDefaultWorkflow { get; set; } = true;

        /// <summary>是否暴露所有启用的自定义工作流工具。</summary>
        [NodeProperty("prop.image_generation_tool_set.enable_custom",
            HintKey = "prop.image_generation_tool_set.enable_custom.hint", Order = 2)]
        public bool EnableCustomWorkflows { get; set; } = true;

        /// <summary>
        /// 指定只暴露某一个自定义工作流；留空表示沿用旧行为（暴露全部已启用工作流）。
        /// 值来自「文生图」设置面板中的工作流 Id。
        /// </summary>
        [NodeProperty("prop.image_generation_tool_set.custom_workflow",
            HintKey = "prop.image_generation_tool_set.custom_workflow.hint", Order = 3,
            OptionsKey = "image_generation_workflows", OptionsSearchable = true, ShowOptionDescription = true,
            OptionsDefaultLabelKey = "prop.image_generation_tool_set.custom_workflow.default")]
        public string CustomWorkflowId { get; set; } = "";

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
                    details: configError, nodeId: Id, nodeName: Name));
            }

            if (!EnableDefaultWorkflow && !EnableCustomWorkflows)
            {
                WriteLog(context, $"[{Name}] both tool sources are disabled; nothing was added.");
                return Task.FromResult(NodeResult.SuccessResult());
            }

            if (attachments == null)
            {
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ConfigNotFound,
                    loc["node_err.image_generation_attachments_unavailable"],
                    nodeId: Id, nodeName: Name));
            }

            var addedNames = new HashSet<string>(StringComparer.Ordinal);

            if (EnableDefaultWorkflow)
            {
                if (string.IsNullOrWhiteSpace(config.DefaultWorkflow.Model))
                {
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.ConfigNotFound,
                        loc["node_err.image_generation_model_missing"],
                        nodeId: Id, nodeName: Name));
                }

                var tool = new ImageGenerationToolV2(kvData, attachments, customWorkflowId: null);
                if (!addedNames.Add(tool.GetDefinition().name))
                {
                    return Task.FromResult(DuplicateToolFailure(loc, tool.GetDefinition().name));
                }

                context.Env.Transient.Tools.Add(tool);
            }

            if (EnableCustomWorkflows)
            {
                List<CustomImageWorkflow> selected;
                if (string.IsNullOrWhiteSpace(CustomWorkflowId))
                {
                    // 留空 = 旧行为：暴露设置页里所有已启用的自定义工作流。
                    selected = config.CustomWorkflows.Where(w => w.Enabled).ToList();
                    if (selected.Count == 0)
                    {
                        WriteLog(context,
                            $"[{Name}] no custom image workflow is enabled; only the default tool (if enabled) was added.");
                    }
                }
                else
                {
                    var workflow = config.FindCustomWorkflow(CustomWorkflowId.Trim());
                    if (workflow == null)
                    {
                        return Task.FromResult(NodeResult.Failure(
                            NodeErrorCodes.ConfigNotFound,
                            loc.Format("node_err.image_generation_workflow_not_found", CustomWorkflowId),
                            nodeId: Id, nodeName: Name));
                    }

                    if (!workflow.Enabled)
                    {
                        return Task.FromResult(NodeResult.Failure(
                            NodeErrorCodes.ConfigNotFound,
                            loc.Format("node_err.image_generation_workflow_disabled", workflow.Name, workflow.Id),
                            nodeId: Id, nodeName: Name));
                    }

                    selected = [workflow];
                }

                foreach (var workflow in selected)
                {
                    var tool = new ImageGenerationToolV2(kvData, attachments, workflow.Id);
                    var toolName = tool.GetDefinition().name;

                    if (!addedNames.Add(toolName))
                    {
                        return Task.FromResult(DuplicateToolFailure(loc, toolName));
                    }

                    context.Env.Transient.Tools.Add(tool);
                }
            }

            return Task.FromResult(NodeResult.SuccessResult());
        }

        private NodeResult DuplicateToolFailure(ILocService loc, string toolName) =>
            NodeResult.Failure(
                NodeErrorCodes.ConfigNotFound,
                loc.Format("node_err.image_generation_duplicate_tool", toolName),
                nodeId: Id, nodeName: Name);

        private void WriteLog(PreNodeExecutionContext context, string message) =>
            context.Env.Persistent.DebugOutput.Write("ImageGenerationToolSetNode", "ImageGeneration", message);
    }
}
