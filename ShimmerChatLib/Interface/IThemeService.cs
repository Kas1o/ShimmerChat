using ShimmerChatLib.Models;

namespace ShimmerChatLib.Interface
{
    public interface IThemeService
    {
        /// <summary>
        /// 当前活动的主题
        /// </summary>
        Theme CurrentTheme { get; }
        
        /// <summary>
        /// 所有可用的主题列表
        /// </summary>
        List<Theme> AvailableThemes { get; }
        
        /// <summary>
        /// 获取内置主题
        /// </summary>
        /// <returns>内置主题列表</returns>
        List<Theme> GetBuiltInThemes();
        
        /// <summary>
        /// 获取用户自定义主题
        /// </summary>
        /// <returns>用户自定义主题列表</returns>
        List<Theme> GetUserThemes();
        
        /// <summary>
        /// 设置当前主题
        /// </summary>
        /// <param name="themeId">主题ID</param>
        void SetTheme(string themeId);
        
        /// <summary>
        /// 应用主题到文档
        /// </summary>
        /// <param name="theme">要应用的主题</param>
        void ApplyTheme(Theme theme);
        
        /// <summary>
        /// 创建新主题
        /// </summary>
        /// <param name="theme">主题对象</param>
        void CreateTheme(Theme theme);
        
        /// <summary>
        /// 更新主题
        /// </summary>
        /// <param name="theme">更新后的主题对象</param>
        void UpdateTheme(Theme theme);
        
        /// <summary>
        /// 删除主题
        /// </summary>
        /// <param name="themeId">主题ID</param>
        void DeleteTheme(string themeId);
        
        /// <summary>
        /// 导出主题
        /// </summary>
        /// <param name="themeId">主题ID</param>
        /// <returns>主题的JSON字符串</returns>
        string ExportTheme(string themeId);
        
        /// <summary>
        /// 导入主题
        /// </summary>
        /// <param name="themeJson">主题的JSON字符串</param>
        void ImportTheme(string themeJson);
        
        /// <summary>
        /// 主题变更事件
        /// </summary>
        event Action<Theme> OnThemeChanged;
    }
}