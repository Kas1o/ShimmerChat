using Microsoft.JSInterop;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChat.Singletons
{
    public class ThemeServiceV2 : IThemeService
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly IKVDataService _kvDataService;
        private Theme _currentTheme;
        private bool _isInitialized = false;
        private readonly object _lock = new object();
        private const string THEME_STORAGE_KEY = "shimmerchat_current_theme_id";
        private const string THEMES_STORAGE_KEY = "shimmerchat_all_themes";
        
        public Theme CurrentTheme 
        { 
            get 
            {
                EnsureInitialized();
                return _currentTheme; 
            } 
        }
        
        public List<Theme> AvailableThemes { get; private set; } = new();
        
        public event Action<Theme>? OnThemeChanged;
        
        public ThemeServiceV2(IJSRuntime jsRuntime, IKVDataService kvDataService)
        {
            _jsRuntime = jsRuntime;
            _kvDataService = kvDataService;
            // 初始化在属性访问时进行，而不是在构造函数中
        }
        
        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                lock (_lock)
                {
                    if (!_isInitialized)
                    {
                        InitializeThemes();
                        _isInitialized = true;
                    }
                }
            }
        }
        
        private void InitializeThemes()
        {
            try
            {
                // 加载所有主题
                LoadAllThemes();
                
                // 获取当前主题ID - 使用KVDataService
                var currentThemeId = _kvDataService.Read(THEME_STORAGE_KEY, THEME_STORAGE_KEY);
                
                if (!string.IsNullOrEmpty(currentThemeId) && AvailableThemes.Any(t => t.Id == currentThemeId))
                {
                    _currentTheme = AvailableThemes.First(t => t.Id == currentThemeId);
                }
                else
                {
                    // 如果没有找到当前主题或当前主题不存在，则使用默认主题
                    _currentTheme = AvailableThemes.FirstOrDefault(t => t.IsDefault) ?? AvailableThemes.FirstOrDefault() ?? GetBuiltInThemes().First();
                }
                
                // 应用当前主题
                ApplyTheme(_currentTheme);
            }
            catch (Exception ex)
            {
                // 如果初始化失败，使用默认主题
                var builtInThemes = GetBuiltInThemes();
                _currentTheme = builtInThemes.First();
                AvailableThemes = builtInThemes;
                ApplyTheme(_currentTheme);
            }
        }
        
        public List<Theme> GetBuiltInThemes()
        {
            var themes = new List<Theme>();
            
            // 默认亮色主题
            var lightTheme = new Theme
            {
                Id = "light_default",
                Name = "Default Light",
                Description = "Default light theme",
                IsDefault = true,
                IsBuiltIn = true,
                // 颜色变量
                ColorPrimary = "#ffa200",
                ColorPrimaryHover = "#ff7700",
                ColorPrimaryActive = "#e66b00",
                ColorSecondary = "#9ca3af",
                ColorSecondaryHover = "#b0b8c5",
                ColorSecondaryActive = "#c2c9d6",
                ColorSuccess = "#10b981",
                ColorSuccessHover = "#0ea570",
                ColorSuccessActive = "#0d9264",
                ColorWarning = "#f59e0b",
                ColorWarningHover = "#d97706",
                ColorWarningActive = "#b45309",
                ColorDanger = "#ef4444",
                ColorDangerHover = "#dc2626",
                ColorDangerActive = "#b91c1c",
                ColorInfo = "#3b82f6",
                ColorInfoHover = "#2563eb",
                ColorInfoActive = "#1d4ed8",
                ColorTextPrimary = "#111827",
                ColorTextSecondary = "#4b5563",
                ColorTextTertiary = "#9ca3af",
                ColorTextInverse = "#ffffff",
                ColorBgPrimary = "#ffffff",
                ColorBgSecondary = "#f9fafb",
                ColorBgTertiary = "#f3f4f6",
                ColorBgInverse = "#111827",
                ColorBorderPrimary = "#e5e7eb",
                ColorBorderSecondary = "#d1d5db",
                ColorBorderTertiary = "#9ca3af",
                ColorOverlay = "rgba(0, 0, 0, 0.5)",
                // 阴影变量
                ShadowSm = "0 1px 2px 0 rgba(0, 0, 0, 0.05)",
                ShadowBase = "0 1px 3px 0 rgba(0, 0, 0, 0.1), 0 1px 2px -1px rgba(0, 0, 0, 0.1)",
                ShadowMd = "0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06)",
                ShadowLg = "0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05)",
                ShadowXl = "0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)",
                Shadow2Xl = "0 25px 50px -12px rgba(0, 0, 0, 0.25)",
                // 圆角变量
                RadiusXs = "0.125rem",
                RadiusSm = "0.25rem",
                RadiusMd = "0.375rem",
                RadiusLg = "0.5rem",
                RadiusXl = "0.75rem",
                Radius2Xl = "1rem",
                Radius3Xl = "1.5rem",
                RadiusFull = "9999px",
                // 间距变量
                SpacingXs = "0.25rem",
                SpacingSm = "0.5rem",
                SpacingMd = "0.75rem",
                SpacingLg = "1rem",
                SpacingXl = "1.5rem",
                Spacing2Xl = "2rem",
                Spacing3Xl = "2.5rem",
                // 其他属性
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            
            themes.Add(lightTheme);
            
            return themes;
        }
        
        public List<Theme> GetUserThemes()
        {
            return AvailableThemes.Where(t => !t.IsBuiltIn).ToList();
        }
        
        public void SetTheme(string themeId)
        {
            var theme = AvailableThemes.FirstOrDefault(t => t.Id == themeId);
            if (theme != null)
            {
                _currentTheme = theme;
                SaveCurrentThemeId(themeId);
                ApplyTheme(theme);
                OnThemeChanged?.Invoke(theme);
            }
        }
        
        public void ApplyTheme(Theme theme)
        {
            try
            {
                // 构建CSS变量字符串
                var cssVariables = BuildCssVariables(theme);
                
                // 通过JavaScript应用主题
                var jsCode = $@"
                    // 清除之前的主题类
                    document.documentElement.className = document.documentElement.className
                        .replace(/\b\w*-theme\b/g, '');
                    
                    // 添加当前主题类
                    document.documentElement.classList.add('{theme.Id.Replace(" ", "-").ToLower()}-theme');
                    
                    // 设置CSS变量
                    const root = document.documentElement;
                    {cssVariables}
                ";
                
                _jsRuntime.InvokeVoidAsync("eval", jsCode);
            }
            catch (Exception ex)
            {
                // 忽略应用主题错误
            }
        }
        
        private string BuildCssVariables(Theme theme)
        {
            var sb = new System.Text.StringBuilder();
            
            // 添加颜色变量
            sb.AppendLine($"root.style.setProperty('--color-primary', '{theme.ColorPrimary}');");
            sb.AppendLine($"root.style.setProperty('--color-primary-hover', '{theme.ColorPrimaryHover}');");
            sb.AppendLine($"root.style.setProperty('--color-primary-active', '{theme.ColorPrimaryActive}');");
            sb.AppendLine($"root.style.setProperty('--color-secondary', '{theme.ColorSecondary}');");
            sb.AppendLine($"root.style.setProperty('--color-secondary-hover', '{theme.ColorSecondaryHover}');");
            sb.AppendLine($"root.style.setProperty('--color-secondary-active', '{theme.ColorSecondaryActive}');");
            sb.AppendLine($"root.style.setProperty('--color-success', '{theme.ColorSuccess}');");
            sb.AppendLine($"root.style.setProperty('--color-success-hover', '{theme.ColorSuccessHover}');");
            sb.AppendLine($"root.style.setProperty('--color-success-active', '{theme.ColorSuccessActive}');");
            sb.AppendLine($"root.style.setProperty('--color-warning', '{theme.ColorWarning}');");
            sb.AppendLine($"root.style.setProperty('--color-warning-hover', '{theme.ColorWarningHover}');");
            sb.AppendLine($"root.style.setProperty('--color-warning-active', '{theme.ColorWarningActive}');");
            sb.AppendLine($"root.style.setProperty('--color-danger', '{theme.ColorDanger}');");
            sb.AppendLine($"root.style.setProperty('--color-danger-hover', '{theme.ColorDangerHover}');");
            sb.AppendLine($"root.style.setProperty('--color-danger-active', '{theme.ColorDangerActive}');");
            sb.AppendLine($"root.style.setProperty('--color-info', '{theme.ColorInfo}');");
            sb.AppendLine($"root.style.setProperty('--color-info-hover', '{theme.ColorInfoHover}');");
            sb.AppendLine($"root.style.setProperty('--color-info-active', '{theme.ColorInfoActive}');");
            sb.AppendLine($"root.style.setProperty('--color-text-primary', '{theme.ColorTextPrimary}');");
            sb.AppendLine($"root.style.setProperty('--color-text-secondary', '{theme.ColorTextSecondary}');");
            sb.AppendLine($"root.style.setProperty('--color-text-tertiary', '{theme.ColorTextTertiary}');");
            sb.AppendLine($"root.style.setProperty('--color-text-inverse', '{theme.ColorTextInverse}');");
            sb.AppendLine($"root.style.setProperty('--color-bg-primary', '{theme.ColorBgPrimary}');");
            sb.AppendLine($"root.style.setProperty('--color-bg-secondary', '{theme.ColorBgSecondary}');");
            sb.AppendLine($"root.style.setProperty('--color-bg-tertiary', '{theme.ColorBgTertiary}');");
            sb.AppendLine($"root.style.setProperty('--color-bg-inverse', '{theme.ColorBgInverse}');");
            sb.AppendLine($"root.style.setProperty('--color-border-primary', '{theme.ColorBorderPrimary}');");
            sb.AppendLine($"root.style.setProperty('--color-border-secondary', '{theme.ColorBorderSecondary}');");
            sb.AppendLine($"root.style.setProperty('--color-border-tertiary', '{theme.ColorBorderTertiary}');");
            sb.AppendLine($"root.style.setProperty('--color-overlay', '{theme.ColorOverlay}');");
            
            // 添加阴影变量
            sb.AppendLine($"root.style.setProperty('--shadow-sm', '{theme.ShadowSm}');");
            sb.AppendLine($"root.style.setProperty('--shadow-base', '{theme.ShadowBase}');");
            sb.AppendLine($"root.style.setProperty('--shadow-md', '{theme.ShadowMd}');");
            sb.AppendLine($"root.style.setProperty('--shadow-lg', '{theme.ShadowLg}');");
            sb.AppendLine($"root.style.setProperty('--shadow-xl', '{theme.ShadowXl}');");
            sb.AppendLine($"root.style.setProperty('--shadow-2xl', '{theme.Shadow2Xl}');");
            
            // 添加圆角变量
            sb.AppendLine($"root.style.setProperty('--radius-xs', '{theme.RadiusXs}');");
            sb.AppendLine($"root.style.setProperty('--radius-sm', '{theme.RadiusSm}');");
            sb.AppendLine($"root.style.setProperty('--radius-md', '{theme.RadiusMd}');");
            sb.AppendLine($"root.style.setProperty('--radius-lg', '{theme.RadiusLg}');");
            sb.AppendLine($"root.style.setProperty('--radius-xl', '{theme.RadiusXl}');");
            sb.AppendLine($"root.style.setProperty('--radius-2xl', '{theme.Radius2Xl}');");
            sb.AppendLine($"root.style.setProperty('--radius-3xl', '{theme.Radius3Xl}');");
            sb.AppendLine($"root.style.setProperty('--radius-full', '{theme.RadiusFull}');");
            
            // 添加间距变量
            sb.AppendLine($"root.style.setProperty('--spacing-xs', '{theme.SpacingXs}');");
            sb.AppendLine($"root.style.setProperty('--spacing-sm', '{theme.SpacingSm}');");
            sb.AppendLine($"root.style.setProperty('--spacing-md', '{theme.SpacingMd}');");
            sb.AppendLine($"root.style.setProperty('--spacing-lg', '{theme.SpacingLg}');");
            sb.AppendLine($"root.style.setProperty('--spacing-xl', '{theme.SpacingXl}');");
            sb.AppendLine($"root.style.setProperty('--spacing-2xl', '{theme.Spacing2Xl}');");
            sb.AppendLine($"root.style.setProperty('--spacing-3xl', '{theme.Spacing3Xl}');");
            
            return sb.ToString();
        }
        
        public void CreateTheme(Theme theme)
        {
            if (AvailableThemes.Any(t => t.Id == theme.Id))
            {
                throw new InvalidOperationException($"Theme with ID '{theme.Id}' already exists.");
            }
            
            theme.CreatedAt = DateTime.Now;
            theme.UpdatedAt = DateTime.Now;
            
            AvailableThemes.Add(theme);
            SaveAllThemes();
        }
        
        public void UpdateTheme(Theme theme)
        {
            var existingIndex = AvailableThemes.FindIndex(t => t.Id == theme.Id);
            if (existingIndex >= 0)
            {
                theme.UpdatedAt = DateTime.Now;
                AvailableThemes[existingIndex] = theme;
                
                _currentTheme = theme;
                ApplyTheme(theme);

                SaveAllThemes();
            }
        }
        
        public void DeleteTheme(string themeId)
        {
            var theme = AvailableThemes.FirstOrDefault(t => t.Id == themeId);
            if (theme != null && !theme.IsBuiltIn)
            {
                AvailableThemes.Remove(theme);
                
                // 如果删除的是当前主题，则切换到默认主题
                if (_currentTheme.Id == themeId)
                {
                    var newTheme = AvailableThemes.FirstOrDefault(t => t.IsDefault) ?? AvailableThemes.FirstOrDefault() ?? GetBuiltInThemes().First();
                    SetTheme(newTheme.Id);
                }
                
                SaveAllThemes();
            }
        }
        
        public string ExportTheme(string themeId)
        {
            var theme = AvailableThemes.FirstOrDefault(t => t.Id == themeId);
            if (theme == null)
            {
                throw new ArgumentException($"Theme with ID '{themeId}' not found.");
            }
            
            return System.Text.Json.JsonSerializer.Serialize(theme, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
        }
        
        public void ImportTheme(string themeJson)
        {
            try
            {
                var theme = System.Text.Json.JsonSerializer.Deserialize<Theme>(themeJson, new System.Text.Json.JsonSerializerOptions());
                if (theme != null)
                {
                    // 生成新的ID以避免冲突
                    theme.Id = Guid.NewGuid().ToString();
                    theme.IsBuiltIn = false;
                    theme.IsDefault = false;
                    theme.CreatedAt = DateTime.Now;
                    theme.UpdatedAt = DateTime.Now;
                    
                    AvailableThemes.Add(theme);
                    SaveAllThemes();
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Invalid theme JSON format.", ex);
            }
        }
        
        private void LoadAllThemes()
        {
            try
            {
                var themesJson = _kvDataService.Read(THEMES_STORAGE_KEY, THEMES_STORAGE_KEY);
                if (!string.IsNullOrEmpty(themesJson))
                {
                    var savedThemes = System.Text.Json.JsonSerializer.Deserialize<List<Theme>>(themesJson, new System.Text.Json.JsonSerializerOptions());
                    if (savedThemes != null)
                    {
                        AvailableThemes = savedThemes;
                    }
                }
            }
            catch (Exception ex)
            {
                // 如果加载失败，使用内置主题
                AvailableThemes = GetBuiltInThemes();
            }
            
            // 确保至少有一个内置主题
            if (!AvailableThemes.Any())
            {
                AvailableThemes = GetBuiltInThemes();
            }
        }
        
        private void SaveAllThemes()
        {
            try
            {
                var themesJson = System.Text.Json.JsonSerializer.Serialize(AvailableThemes, new System.Text.Json.JsonSerializerOptions());
                _kvDataService.Write(THEMES_STORAGE_KEY, THEMES_STORAGE_KEY, themesJson);
            }
            catch (Exception ex)
            {
                // 忽略存储错误
            }
        }
        
        private void SaveCurrentThemeId(string themeId)
        {
            try
            {
                _kvDataService.Write(THEME_STORAGE_KEY, THEME_STORAGE_KEY, themeId);
            }
            catch (Exception ex)
            {
                // 忽略存储错误
            }
        }
    }
}