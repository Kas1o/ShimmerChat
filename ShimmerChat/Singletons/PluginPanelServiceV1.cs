using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ShimmerChatLib.Panel;
using ShimmerChatLib.Tool;
using ShimmerChatBuiltinTools;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class PluginPanelServiceV1 : IPluginPanelService
    {
        private readonly IPluginLoaderService _pluginLoaderService;
        private readonly List<PluginPanelInfo> _pluginPanels = new();
        private readonly string _pluginsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        private bool _panelsLoaded = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="pluginLoaderService">插件加载服务</param>
        public PluginPanelServiceV1(IPluginLoaderService pluginLoaderService)
        {
            _pluginLoaderService = pluginLoaderService;
        }

        /// <summary>
        /// 加载所有插件面板
        /// </summary>
        public void LoadAllPluginPanels()
        {
            // 使用锁确保只加载一次
            lock (_pluginPanels)
            {
                if (_panelsLoaded)
                {
                    return; // 已经加载过了，直接返回
                }
                
                try
                {
                    // 清空现有面板列表
                    _pluginPanels.Clear();

                    Console.WriteLine("开始加载插件面板...");
                    
                    // 1. 加载内置的插件面板（来自ShimmerChatBuiltinTools）
                    try
                    {
                        Console.WriteLine("加载内置插件面板...");
                        var builtinAssembly = typeof(ShimmerChatBuiltinTools.Target).Assembly;
                        var builtinPanelTypes = _pluginLoaderService.GetTypesWithAttributeFromAssembly<PluginPanelAttribute>(builtinAssembly);
                        
                        Console.WriteLine($"找到 {builtinPanelTypes.Count} 个内置面板类型");
                        AddPanelsFromTypes(builtinPanelTypes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"加载内置面板时出错: {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                    }
                    
                    // 2. 加载外部插件面板
                    try
                    {
                        Console.WriteLine("加载外部插件面板...");
                        var pluginPanelTypes = _pluginLoaderService.GetTypesWithAttributeFromPlugins<PluginPanelAttribute>(_pluginsFolder);
                        
                        Console.WriteLine($"找到 {pluginPanelTypes.Count} 个外部面板类型");
                        AddPanelsFromTypes(pluginPanelTypes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"加载外部插件面板时出错: {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                    }

                // 按顺序排序
                _pluginPanels.Sort((a, b) => a.Order.CompareTo(b.Order));
                _panelsLoaded = true;

                Console.WriteLine($"成功加载了 {_pluginPanels.Count} 个插件面板");
                foreach (var panel in _pluginPanels)
                {
                    Console.WriteLine($"  - {panel.Name}: {panel.PanelType.FullName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载插件面板时出错: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            }
        }
        
        /// <summary>
        /// 确保插件面板已加载
        /// </summary>
        private void EnsurePanelsLoaded()
        {
            if (!_panelsLoaded)
            {
                Console.WriteLine("开始加载插件面板...");
                LoadAllPluginPanels();
            }
        }

        /// <summary>
        /// 从类型列表中添加面板
        /// </summary>
        /// <param name="panelTypes">面板类型列表</param>
        private void AddPanelsFromTypes(IEnumerable<Type> panelTypes)
        {
            foreach (var panelType in panelTypes)
            {
                try
                {
                    Console.WriteLine($"检查面板类型: {panelType.FullName}");
                    var attribute = panelType.GetCustomAttributes(typeof(PluginPanelAttribute), false).FirstOrDefault() as PluginPanelAttribute;
                    if (attribute != null)
                    {
                        Console.WriteLine($"  找到属性: Name='{attribute.Name}', Description='{attribute.Description}'");
                        if (IsValidComponent(panelType))
                        {
                            Console.WriteLine($"  有效组件，添加到列表");
                            _pluginPanels.Add(new PluginPanelInfo(
                                attribute.Name,
                                attribute.Description,
                                attribute.Icon,
                                attribute.Order,
                                panelType,
                                attribute.PanelDisplayPlace
                            ));
                        }
                        else
                        {
                            Console.WriteLine($"  无效组件，跳过");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  未找到PluginPanelAttribute");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理面板类型 {panelType.Name} 时出错: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        /// <summary>
        /// 检查类型是否为有效的组件
        /// </summary>
        /// <param name="type">要检查的类型</param>
        /// <returns>是否为有效的组件</returns>
        private bool IsValidComponent(Type type)
        {
            // 检查是否为类、非抽象、可实例化
            return type.IsClass && !type.IsAbstract && !type.IsInterface;
        }

        /// <summary>
        /// 获取所有已加载的插件面板信息
        /// </summary>
        /// <returns>插件面板信息列表</returns>
        public List<PluginPanelInfo> GetPluginPanels()
        {
            EnsurePanelsLoaded();
            return new List<PluginPanelInfo>(_pluginPanels);
        }

        /// <summary>
        /// 根据名称获取插件面板类型
        /// </summary>
        /// <param name="panelName">面板名称</param>
        /// <returns>面板类型，如果不存在返回null</returns>
        public Type? GetPluginPanelType(string panelName)
        {
            EnsurePanelsLoaded();
            return _pluginPanels.FirstOrDefault(p => p.Name.Equals(panelName, StringComparison.OrdinalIgnoreCase))?.PanelType;
        }

        /// <summary>
        /// 刷新插件面板列表
        /// </summary>
        public void RefreshPluginPanels()
        {
            _panelsLoaded = false;
            LoadAllPluginPanels();
        }
    }
}