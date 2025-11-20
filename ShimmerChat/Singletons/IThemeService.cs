namespace ShimmerChat.Singletons
{
    public interface IThemeService
    {
        /// <summary>
        /// 当前是否为暗色主题
        /// </summary>
        bool IsDarkMode { get; }
        
        /// <summary>
        /// 切换主题
        /// </summary>
        void ToggleTheme();
        
        /// <summary>
        /// 设置主题
        /// </summary>
        /// <param name="isDarkMode">是否为暗色主题</param>
        void SetTheme(bool isDarkMode);
        
        /// <summary>
        /// 主题变更事件
        /// </summary>
        event Action<bool> OnThemeChanged;
    }
}