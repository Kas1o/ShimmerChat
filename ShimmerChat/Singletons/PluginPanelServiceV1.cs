using Microsoft.Extensions.Logging;
using System.Reflection;
using ShimmerChatLib.Panel;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class PluginPanelServiceV1 : IPluginPanelService
    {
        private readonly IPluginLoaderService _pluginLoaderService;
        private readonly List<PluginPanelInfo> _pluginPanels = new();
        private readonly ILogger<PluginPanelServiceV1> _logger;
        private bool _panelsLoaded = false;

        public PluginPanelServiceV1(IPluginLoaderService pluginLoaderService, ILogger<PluginPanelServiceV1> logger)
        {
            _pluginLoaderService = pluginLoaderService;
            _logger = logger;
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

                    _logger.LogInformation("找到 {Count} 个面板类型", panelTypes.Count);
                    AddPanelsFromTypes(panelTypes);

                    _pluginPanels.Sort((a, b) => a.Order.CompareTo(b.Order));
                    _panelsLoaded = true;

                    _logger.LogInformation("成功加载了 {Count} 个插件面板", _pluginPanels.Count);
                    foreach (var panel in _pluginPanels)
                        _logger.LogInformation("  - {Name}: {Type}", panel.NameKey, panel.PanelType.FullName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "加载插件面板时出错: {Message}", ex.Message);
                    _logger.LogError(ex, "{StackTrace}", ex.StackTrace);
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
                    _logger.LogError(ex, "处理面板类型 {TypeName} 时出错: {Message}", panelType.Name, ex.Message);
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
