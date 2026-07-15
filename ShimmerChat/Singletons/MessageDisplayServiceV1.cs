using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class RenderStepResult
    {
        public required string StepName { get; set; }
        public required string Content { get; set; }
        public bool IsError { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class RenderResult
    {
        public MarkupString Html { get; set; }
        public bool HasError { get; set; }
        public string? ErrorMessage { get; set; }
        public string? FailedStepName { get; set; }
        public List<RenderStepResult> IntermediateResults { get; set; } = new();
    }

    public class MessageDisplayServiceV1 : IMessageDisplayService
    {
        private readonly IRenderModifierManager _renderModifierManager;
        private readonly IKVDataService _kvData;
        private readonly ILogger<MessageDisplayServiceV1> _logger;

        private const string PluginId = "MessageDisplayService";
        private const string DebugModeKey = "debug_mode_enabled";

        public bool DebugModeEnabled { get; set; }

        public MessageDisplayServiceV1(IRenderModifierManager renderModifierManager,
            IKVDataService kvData, ILogger<MessageDisplayServiceV1> logger)
        {
            _renderModifierManager = renderModifierManager;
            _kvData = kvData;
            _logger = logger;

            var json = _kvData.Read(PluginId, DebugModeKey);
            if (!string.IsNullOrEmpty(json))
            {
                try { DebugModeEnabled = Newtonsoft.Json.JsonConvert.DeserializeObject<bool>(json); }
                catch { }
            }
        }

        public void SaveDebugModeSetting()
        {
            _kvData.Write(PluginId, DebugModeKey,
                Newtonsoft.Json.JsonConvert.SerializeObject(DebugModeEnabled));
        }

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

        private static string BuildErrorHtml(string currentContent,
            List<RenderStepResult> intermediateResults, string errorMessage, string failedStepName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<div style='border: 2px solid #dc3545; border-radius: 8px; padding: 16px; margin: 8px 0; background-color: #f8d7da;'>");
            sb.AppendLine("<h4 style='color: #721c24; margin-top: 0;'>Render Error</h4>");
            sb.AppendLine($"<p style='color: #721c24;'><strong>Node:</strong> {failedStepName}</p>");
            sb.AppendLine($"<p style='color: #721c24;'><strong>Error:</strong> {System.Web.HttpUtility.HtmlEncode(errorMessage)}</p>");

            if (intermediateResults.Count > 0)
            {
                sb.AppendLine("<div style='margin-top: 16px;'>");
                sb.AppendLine("<h5 style='color: #721c24;'>Pipeline steps:</h5>");
                sb.AppendLine("<div style='max-height: 400px; overflow-y: auto;'>");

                for (int i = 0; i < intermediateResults.Count; i++)
                {
                    var step = intermediateResults[i];
                    var borderColor = step.IsError ? "#dc3545" : "#6c757d";
                    var bgColor = step.IsError ? "#f8d7da" : "#f8f9fa";

                    sb.AppendLine($"<div style='border: 1px solid {borderColor}; border-radius: 4px; padding: 8px; margin-bottom: 8px; background-color: {bgColor};'>");
                    sb.AppendLine($"<div style='font-weight: bold; color: {borderColor}; margin-bottom: 4px;'>Step {i}: {step.StepName}</div>");

                    if (step.IsError && !string.IsNullOrEmpty(step.ErrorMessage))
                        sb.AppendLine($"<div style='color: #dc3545; font-size: 0.9em; margin-bottom: 4px;'>Error: {System.Web.HttpUtility.HtmlEncode(step.ErrorMessage)}</div>");

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
