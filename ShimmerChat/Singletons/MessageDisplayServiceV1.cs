using Markdig;
using Markdig.Extensions.Tables;
using Microsoft.AspNetCore.Components;
using ShimmerChatLib.Interface;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;
using System.Text;
using ShimmerChatLib;

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

        private readonly string PluginsFolder = Path.Combine(AppContext.BaseDirectory, "./Plugins");
        private const string PluginId = "MessageDisplayService";
        private const string ActivatedModifiersKey = "activated_modifiers";
        private const string DebugModeKey = "debug_mode_enabled";

        private readonly IPluginLoaderService _pluginLoaderService;
        private readonly IKVDataService _pluginDataService;

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
        public MessageDisplayServiceV1(IPluginLoaderService pluginLoaderService, IKVDataService pluginDataService)
        {
            _pluginLoaderService = pluginLoaderService;
            _pluginDataService = pluginDataService;

            // 创建并配置MarkdownPipeline
            // 只在服务初始化时创建一次，所有消息组件共享使用
            _markdownPipeline = new MarkdownPipelineBuilder()
                .UsePipeTables() // 启用表格支持
				//.UseBootstrap()
				//.UseAdvancedExtensions()
				.Build();

            LoadAllModifiers();
            LoadActivatedModifiers();
        }

        private void LoadAllModifiers()
        {
            var modifierDict = new Dictionary<string, IMessageRenderModifier>(StringComparer.OrdinalIgnoreCase);

            // 1. 加载 ShimmerChatBuiltin 项目的 RenderModifier
            try
            {
                var builtinAssembly = typeof(ShimmerChatBuiltinTools.Target).Assembly;
                var builtinModifiers = _pluginLoaderService.LoadImplementationsFromAssembly<IMessageRenderModifier>(builtinAssembly);
                
                foreach (var modifier in builtinModifiers)
                {
                    var name = modifier.Info.Name;
                    if (modifierDict.ContainsKey(name))
                        // 简单的日志或忽略重复
                        Console.WriteLine($"MessageRenderModifier名称冲突(Builtin): {name}");
                    else
                        modifierDict[name] = modifier;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载内置MessageRenderModifier失败: {ex.Message}");
            }

            // 2. 加载插件中的 RenderModifier
            try
            {
                var pluginModifiers = _pluginLoaderService.LoadImplementationsFromPlugins<IMessageRenderModifier>(PluginsFolder);
                foreach (var modifier in pluginModifiers)
                {
                    var name = modifier.Info.Name;
                    if (modifierDict.ContainsKey(name))
                         Console.WriteLine($"MessageRenderModifier名称冲突(Plugin): {name}");
                    else
                        modifierDict[name] = modifier;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载插件MessageRenderModifier失败: {ex.Message}");
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
                Console.WriteLine($"加载激活的MessageRenderModifier失败: {ex.Message}");
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
                Console.WriteLine($"保存激活的MessageRenderModifier失败: {ex.Message}");
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
                Console.WriteLine($"保存调试模式设置失败: {ex.Message}");
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

        /// <summary>
        /// 渲染消息并返回详细的渲染结果，包括中间步骤和错误信息
        /// </summary>
        /// <param name="markdownText">要渲染的Markdown文本</param>
        /// <param name="chat">当前的Chat对象</param>
        /// <param name="agent">当前的Agent对象</param>
        /// <returns>包含详细信息的渲染结果</returns>
        public RenderResult RenderWithDetails(string markdownText, Chat? chat = null, Agent? agent = null)
        {
            string processedText = markdownText ?? "";
            var intermediateResults = new List<RenderStepResult>();
            var result = new RenderResult
            {
                IntermediateResults = intermediateResults
            };

            // 记录初始状态
            if (DebugModeEnabled)
            {
                intermediateResults.Add(new RenderStepResult
                {
                    StepName = "Initial",
                    Content = processedText
                });
            }

            // 应用激活的修改器
            foreach (var activatedModifier in ActivatedModifiers)
            {
                if (!activatedModifier.IsEnabled) continue;

                var modifier = LoadedModifiers.FirstOrDefault(m => m.Info.Name == activatedModifier.Name);
                if (modifier != null)
                {
                    try
                    {
                        processedText = modifier.Modify(processedText, activatedModifier.Value, chat, agent);

                        if (DebugModeEnabled)
                        {
                            intermediateResults.Add(new RenderStepResult
                            {
                                StepName = $"Modifier: {activatedModifier.Name}",
                                Content = processedText
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = $"Error applying modifier '{activatedModifier.Name}': {ex.Message}";
                        Console.WriteLine(errorMsg);

                        if (DebugModeEnabled)
                        {
                            intermediateResults.Add(new RenderStepResult
                            {
                                StepName = $"Modifier: {activatedModifier.Name}",
                                Content = processedText,
                                IsError = true,
                                ErrorMessage = ex.Message
                            });
                        }

                        // 构建错误信息HTML
                        var errorHtml = BuildErrorHtml(processedText, intermediateResults, errorMsg, activatedModifier.Name);
                        result.Html = (MarkupString)errorHtml;
                        result.HasError = true;
                        result.ErrorMessage = errorMsg;
                        result.FailedStepName = activatedModifier.Name;
                        return result;
                    }
                }
            }

            // 使用共享的MarkdownPipeline实例进行渲染
            try
            {
                var html = Markdig.Markdown.ToHtml(processedText, _markdownPipeline);

                if (DebugModeEnabled)
                {
                    intermediateResults.Add(new RenderStepResult
                    {
                        StepName = "Markdown Rendering",
                        Content = html
                    });
                }

                result.Html = (MarkupString)html;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error during Markdown rendering: {ex.Message}";
                Console.WriteLine(errorMsg);

                if (DebugModeEnabled)
                {
                    intermediateResults.Add(new RenderStepResult
                    {
                        StepName = "Markdown Rendering",
                        Content = processedText,
                        IsError = true,
                        ErrorMessage = ex.Message
                    });
                }

                // 构建错误信息HTML
                var errorHtml = BuildErrorHtml(processedText, intermediateResults, errorMsg, "Markdown Rendering");
                result.Html = (MarkupString)errorHtml;
                result.HasError = true;
                result.ErrorMessage = errorMsg;
                result.FailedStepName = "Markdown Rendering";
                return result;
            }

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

            // 如果启用了调试模式，显示所有中间步骤
            if (DebugModeEnabled && intermediateResults.Count > 0)
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