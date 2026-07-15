using Markdig;
using Markdig.Extensions.Tables;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;
using Newtonsoft.Json;
using System.Text;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 渲染步骤中间结果
    /// </summary>
    public class RenderStepResult
    {
        public required string StepName { get; set; }
        public required string Content { get; set; }
        public bool IsError { get; set; } = false;
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 渲染结果
    /// </summary>
    public class RenderResult
    {
        public MarkupString Html { get; set; }
        public bool HasError { get; set; } = false;
        public string? ErrorMessage { get; set; }
        public string? FailedStepName { get; set; }
        public List<RenderStepResult> IntermediateResults { get; set; } = new();
    }

    /// <summary>
    /// 消息显示渲染服务的具体实现
    /// 使用单例模式管理Markdown渲染管道，避免重复创建消耗资源
    /// </summary>
    public class MessageDisplayServiceV1 : IMessageDisplayService
    {
        // 共享的MarkdownPipeline实例，只需创建一次
        private readonly MarkdownPipeline _markdownPipeline;

        private const string PluginId = "MessageDisplayService";
        private const string ActivatedModifiersKey = "activated_modifiers";
        private const string DebugModeKey = "debug_mode_enabled";

        private readonly IPluginLoaderService _pluginLoaderService;
        private readonly IKVDataService _pluginDataService;
        private readonly IRenderModifierManager _renderModifierManager;
        private readonly ILogger<MessageDisplayServiceV1> _logger;

        public List<IMessageRenderModifier> LoadedModifiers { get; private set; } = new();
        public List<ActivatedMessageRenderModifier> ActivatedModifiers { get; private set; } = new();

        /// <summary>
        /// 是否启用调试模式（返回完整渲染流程中间结果）
        /// </summary>
        public bool DebugModeEnabled { get; set; } = false;

        /// <summary>
        /// 构造函数
        /// 初始化Markdown渲染管道，配置所需的扩展
        /// </summary>
        public MessageDisplayServiceV1(IPluginLoaderService pluginLoaderService, IKVDataService pluginDataService,
            IRenderModifierManager renderModifierManager, ILogger<MessageDisplayServiceV1> logger)
        {
            _pluginLoaderService = pluginLoaderService;
            _pluginDataService = pluginDataService;
            _renderModifierManager = renderModifierManager;
            _logger = logger;

            // 保留 MarkdownPipeline 作为回退
            _markdownPipeline = new MarkdownPipelineBuilder()
                .UsePipeTables()
                .Build();

            LoadAllModifiers();
            LoadActivatedModifiers();
        }

        private void LoadAllModifiers()
        {
            var modifierDict = new Dictionary<string, IMessageRenderModifier>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var allModifiers = _pluginLoaderService.LoadImplementations<IMessageRenderModifier>();
                foreach (var modifier in allModifiers)
                {
                    var name = modifier.Info.Name;
                    if (modifierDict.ContainsKey(name))
                        _logger.LogWarning("MessageRenderModifier名称冲突: {Name}", name);
                    else
                        modifierDict[name] = modifier;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载MessageRenderModifier失败: {Message}", ex.Message);
            }

            LoadedModifiers = modifierDict.Values.ToList();
        }

        private void LoadActivatedModifiers()
        {
            try
            {
                var json = _pluginDataService.Read(PluginId, ActivatedModifiersKey);
                if (!string.IsNullOrEmpty(json))
                {
                    ActivatedModifiers = JsonConvert.DeserializeObject<List<ActivatedMessageRenderModifier>>(json) ?? new List<ActivatedMessageRenderModifier>();
                }

                // 加载调试模式设置
                var debugModeJson = _pluginDataService.Read(PluginId, DebugModeKey);
                if (!string.IsNullOrEmpty(debugModeJson))
                {
                    DebugModeEnabled = JsonConvert.DeserializeObject<bool>(debugModeJson);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载激活的MessageRenderModifier失败: {Message}", ex.Message);
                ActivatedModifiers = new List<ActivatedMessageRenderModifier>();
            }
        }

        public void SaveActivatedModifiers()
        {
            try
            {
                var json = JsonConvert.SerializeObject(ActivatedModifiers);
                _pluginDataService.Write(PluginId, ActivatedModifiersKey, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存激活的MessageRenderModifier失败: {Message}", ex.Message);
            }
        }

        public void SaveDebugModeSetting()
        {
            try
            {
                var json = JsonConvert.SerializeObject(DebugModeEnabled);
                _pluginDataService.Write(PluginId, DebugModeKey, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存调试模式设置失败: {Message}", ex.Message);
            }
        }

        public void ActivateModifier(string modifierName, string inputValue)
        {
            // 验证Modifier是否存在
            var modifier = LoadedModifiers.FirstOrDefault(m => m.Info.Name == modifierName);
            if (modifier != null)
            {
                ActivatedModifiers.Add(new ActivatedMessageRenderModifier
                {
                    Name = modifierName,
                    Value = inputValue
                });
                SaveActivatedModifiers();
            }
        }

        public void RemoveActivatedModifier(int index)
        {
            if (index >= 0 && index < ActivatedModifiers.Count)
            {
                ActivatedModifiers.RemoveAt(index);
                SaveActivatedModifiers();
            }
        }

        public void ReorderActivatedModifier(int oldIndex, int newIndex)
        {
            if (oldIndex >= 0 && oldIndex < ActivatedModifiers.Count && newIndex >= 0 && newIndex < ActivatedModifiers.Count)
            {
                var item = ActivatedModifiers[oldIndex];
                ActivatedModifiers.RemoveAt(oldIndex);
                ActivatedModifiers.Insert(newIndex, item);
                SaveActivatedModifiers();
            }
        }

        /// <summary>
        /// 将Markdown文本渲染为HTML标记，并应用上下文修改器
        /// 使用共享的渲染管道，提高性能
        /// </summary>
        /// <param name="markdownText">要渲染的Markdown文本</param>
        /// <returns>渲染后的HTML标记</returns>
        public MarkupString Render(string markdownText, Chat? chat = null, Agent? agent = null)
        {
            var result = RenderWithDetails(markdownText, chat, agent);
            return result.Html;
        }

        public RenderResult RenderWithDetails(string markdownText, Chat? chat = null, Agent? agent = null)
        {
            var processedText = markdownText ?? "";
            var intermediateResults = new List<RenderStepResult>();
            var result = new RenderResult { IntermediateResults = intermediateResults };

            intermediateResults.Add(new RenderStepResult
            {
                StepName = "Initial",
                Content = processedText
            });

            var (nodeResult, changeLog) = _renderModifierManager.RenderWithLog(
                agent, processedText, chat);

            foreach (var change in changeLog)
            {
                intermediateResults.Add(new RenderStepResult
                {
                    StepName = $"{change.NodeType}: {change.NodeName}",
                    Content = change.After
                });
            }

            if (!nodeResult.Success)
            {
                var errorMsg = $"{nodeResult.Code}: {nodeResult.Message}";
                var errorHtml = BuildErrorHtml(processedText, intermediateResults,
                    errorMsg, nodeResult.NodeName ?? "Unknown");
                result.Html = (MarkupString)errorHtml;
                result.HasError = true;
                result.ErrorMessage = errorMsg;
                result.FailedStepName = nodeResult.NodeName;
                return result;
            }

            result.Html = (MarkupString)nodeResult.Content;
            return result;
        }

        /// <summary>
        /// 构建包含错误信息和中间结果的HTML
        /// </summary>
        private string BuildErrorHtml(string currentContent, List<RenderStepResult> intermediateResults, string errorMessage, string failedStepName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<div style='border: 2px solid #dc3545; border-radius: 8px; padding: 16px; margin: 8px 0; background-color: #f8d7da;'>");
            sb.AppendLine("<h4 style='color: #721c24; margin-top: 0;'>⚠️ 渲染错误</h4>");
            sb.AppendLine($"<p style='color: #721c24;'><strong>出错模块:</strong> {failedStepName}</p>");
            sb.AppendLine($"<p style='color: #721c24;'><strong>错误信息:</strong> {System.Web.HttpUtility.HtmlEncode(errorMessage)}</p>");

            // 显示当前已完成的结果
            sb.AppendLine("<div style='margin-top: 16px;'>");
            sb.AppendLine("<h5 style='color: #721c24;'>当前渲染结果（出错前）:</h5>");
            sb.AppendLine("<div style='border: 1px solid #dc3545; border-radius: 4px; padding: 12px; background-color: #fff;'>");
            sb.AppendLine(System.Web.HttpUtility.HtmlEncode(currentContent));
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");

            // 显示所有中间步骤
            if (intermediateResults.Count > 0)
            {
                sb.AppendLine("<div style='margin-top: 16px;'>");
                sb.AppendLine("<h5 style='color: #721c24;'>渲染流程中间结果:</h5>");
                sb.AppendLine("<div style='max-height: 400px; overflow-y: auto;'>");

                for (int i = 0; i < intermediateResults.Count; i++)
                {
                    var step = intermediateResults[i];
                    var borderColor = step.IsError ? "#dc3545" : "#6c757d";
                    var bgColor = step.IsError ? "#f8d7da" : "#f8f9fa";

                    sb.AppendLine($"<div style='border: 1px solid {borderColor}; border-radius: 4px; padding: 8px; margin-bottom: 8px; background-color: {bgColor};'>");
                    sb.AppendLine($"<div style='font-weight: bold; color: {borderColor}; margin-bottom: 4px;'>步骤 {i}: {step.StepName}</div>");

                    if (step.IsError && !string.IsNullOrEmpty(step.ErrorMessage))
                    {
                        sb.AppendLine($"<div style='color: #dc3545; font-size: 0.9em; margin-bottom: 4px;'>错误: {System.Web.HttpUtility.HtmlEncode(step.ErrorMessage)}</div>");
                    }

                    sb.AppendLine("<pre style='margin: 0; white-space: pre-wrap; word-break: break-word; font-size: 0.85em; background-color: #fff; padding: 8px; border-radius: 4px;'>");
                    sb.AppendLine(System.Web.HttpUtility.HtmlEncode(step.Content));
                    sb.AppendLine("</pre>");
                    sb.AppendLine("</div>");
                }

                sb.AppendLine("</div>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</div>");

            return sb.ToString();
        }
    }
}