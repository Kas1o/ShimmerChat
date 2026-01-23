using Markdig;
using Markdig.Extensions.Tables;
using Microsoft.AspNetCore.Components;
using ShimmerChatLib.Interface;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;
using ShimmerChatLib;

namespace ShimmerChat.Singletons
{
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
        
        private readonly IPluginLoaderService _pluginLoaderService;
        private readonly IKVDataService _pluginDataService;

        public List<IMessageRenderModifier> LoadedModifiers { get; private set; } = new();
        public List<ActivatedMessageRenderModifier> ActivatedModifiers { get; private set; } = new();

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
                    ActivatedModifiers = JsonSerializer.Deserialize<List<ActivatedMessageRenderModifier>>(json) ?? new List<ActivatedMessageRenderModifier>();
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
                var json = JsonSerializer.Serialize(ActivatedModifiers);
                _pluginDataService.Write(PluginId, ActivatedModifiersKey, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存激活的MessageRenderModifier失败: {ex.Message}");
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
            string processedText = markdownText ?? "";

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
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error applying modifier {activatedModifier.Name}: {ex.Message}");
                    }
                }
            }

            // 使用共享的MarkdownPipeline实例进行渲染
            return (MarkupString)Markdig.Markdown.ToHtml(processedText, _markdownPipeline);
        }
    }
}