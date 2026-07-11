using System.Reflection;
using ShimmerChatLib.Panel;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class PluginPanelServiceV1 : IPluginPanelService
    {
        private readonly IPluginLoaderService _pluginLoaderService;
        private readonly List<PluginPanelInfo> _pluginPanels = new();
        private bool _panelsLoaded = false;

        public PluginPanelServiceV1(IPluginLoaderService pluginLoaderService)
        {
            _pluginLoaderService = pluginLoaderService;
        }

        public void LoadAllPluginPanels()
        {
            lock (_pluginPanels)
            {
                if (_panelsLoaded)
                    return;

                try
                {
                    _pluginPanels.Clear();

                    var panelTypes = _pluginLoaderService.GetTypesWithAttribute<PluginPanelAttribute>();

                    Console.WriteLine($"找到 {panelTypes.Count} 个面板类型");
                    AddPanelsFromTypes(panelTypes);

                    _pluginPanels.Sort((a, b) => a.Order.CompareTo(b.Order));
                    _panelsLoaded = true;

                    Console.WriteLine($"成功加载了 {_pluginPanels.Count} 个插件面板");
                    foreach (var panel in _pluginPanels)
                        Console.WriteLine($"  - {panel.NameKey}: {panel.PanelType.FullName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载插件面板时出错: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        private void EnsurePanelsLoaded()
        {
            if (!_panelsLoaded)
                LoadAllPluginPanels();
        }

        private void AddPanelsFromTypes(IEnumerable<Type> panelTypes)
        {
            foreach (var panelType in panelTypes)
            {
                try
                {
                    var attribute = panelType.GetCustomAttribute<PluginPanelAttribute>();
                    if (attribute != null && IsValidComponent(panelType))
                    {
                        _pluginPanels.Add(new PluginPanelInfo(
                            attribute.NameKey,
                            attribute.DescriptionKey,
                            attribute.Icon,
                            attribute.Order,
                            panelType,
                            attribute.PanelDisplayPlace
                        ));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理面板类型 {panelType.Name} 时出错: {ex.Message}");
                }
            }
        }

        private static bool IsValidComponent(Type type)
        {
            return type.IsClass && !type.IsAbstract && !type.IsInterface;
        }

        public List<PluginPanelInfo> GetPluginPanels()
        {
            EnsurePanelsLoaded();
            return new List<PluginPanelInfo>(_pluginPanels);
        }

        public Type? GetPluginPanelType(string panelName)
        {
            EnsurePanelsLoaded();
            return _pluginPanels.FirstOrDefault(p => p.NameKey.Equals(panelName, StringComparison.OrdinalIgnoreCase))?.PanelType;
        }

        public void RefreshPluginPanels()
        {
            _panelsLoaded = false;
            LoadAllPluginPanels();
        }
    }
}
