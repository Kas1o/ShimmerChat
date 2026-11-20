using ShimmerChatLib.Panel;

namespace ShimmerChatLib.Interface
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
        /// 根据名称获取插件面板完整信息（含 Flush 等宿主渲染所需标记）
        /// </summary>
        /// <param name="panelName">面板名称</param>
        /// <returns>面板信息，如果不存在返回null</returns>
        PluginPanelInfo? GetPluginPanel(string panelName);

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
        /// 面板名称本地化 Key
        /// </summary>
        public string NameKey { get; set; }

        /// <summary>
        /// 面板描述本地化 Key
        /// </summary>
        public string DescriptionKey { get; set; }

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

		public PanelDisplayPlace PanelDisplayPlace { get; set; }

        /// <summary>
        /// 面板自带 Config UI 固定操作面板，宿主不渲染外层标题与卡片外壳。
        /// 见 <see cref="ShimmerChatLib.Panel.PluginPanelAttribute.Flush"/>。
        /// </summary>
        public bool Flush { get; set; }

		/// <summary>
		/// 构造函数
		/// </summary>
		/// <param name="nameKey">面板名称本地化 Key</param>
		/// <param name="descriptionKey">面板描述本地化 Key</param>
		/// <param name="icon">面板图标</param>
		/// <param name="order">面板顺序</param>
		/// <param name="panelType">面板类型</param>
		public PluginPanelInfo(string nameKey, string descriptionKey, string? icon, int order, Type panelType, PanelDisplayPlace panelDisplayPlace)
        {
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
            Icon = icon;
            Order = order;
            PanelType = panelType;
            PanelDisplayPlace = panelDisplayPlace;
        }

        /// <summary>
        /// 构造函数（含 Flush 标记）
        /// </summary>
        public PluginPanelInfo(string nameKey, string descriptionKey, string? icon, int order, Type panelType, PanelDisplayPlace panelDisplayPlace, bool flush)
            : this(nameKey, descriptionKey, icon, order, panelType, panelDisplayPlace)
        {
            Flush = flush;
        }
    }
}