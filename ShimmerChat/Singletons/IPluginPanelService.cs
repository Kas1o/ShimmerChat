using ShimmerChatLib.Tool;

namespace ShimmerChat.Singletons
{
    public interface IPluginPanelService
    {
        /// <summary>
        /// 加载所有插件面板
        /// </summary>
        void LoadAllPluginPanels();

        /// <summary>
        /// 获取所有已加载的插件面板信息
        /// </summary>
        /// <returns>插件面板信息列表</returns>
        List<PluginPanelInfo> GetPluginPanels();

        /// <summary>
        /// 根据名称获取插件面板类型
        /// </summary>
        /// <param name="panelName">面板名称</param>
        /// <returns>面板类型，如果不存在返回null</returns>
        Type? GetPluginPanelType(string panelName);

        /// <summary>
        /// 刷新插件面板列表
        /// </summary>
        void RefreshPluginPanels();
    }

    /// <summary>
    /// 插件面板信息
    /// </summary>
    public class PluginPanelInfo
    {
        /// <summary>
        /// 面板名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 面板描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 面板图标
        /// </summary>
        public string? Icon { get; set; }

        /// <summary>
        /// 面板顺序
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// 面板类型
        /// </summary>
        public Type PanelType { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="name">面板名称</param>
        /// <param name="description">面板描述</param>
        /// <param name="icon">面板图标</param>
        /// <param name="order">面板顺序</param>
        /// <param name="panelType">面板类型</param>
        public PluginPanelInfo(string name, string description, string? icon, int order, Type panelType)
        {
            Name = name;
            Description = description;
            Icon = icon;
            Order = order;
            PanelType = panelType;
        }
    }
}