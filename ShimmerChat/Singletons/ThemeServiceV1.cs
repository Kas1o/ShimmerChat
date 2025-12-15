using Microsoft.JSInterop;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class ThemeServiceV1 : IThemeService
    {
        private readonly IJSRuntime _jsRuntime;
        private bool _isDarkMode = false;
        private const string THEME_STORAGE_KEY = "shimmerchat_theme_dark_mode";
        
        public bool IsDarkMode => _isDarkMode;
        
        public event Action<bool>? OnThemeChanged;
        
        public ThemeServiceV1(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
            InitializeTheme();
        }
        
        private async void InitializeTheme()
        {
            try
            {
                // 从本地存储读取主题设置
                var storedTheme = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", THEME_STORAGE_KEY);
                
                if (storedTheme != null)
                {
                    _isDarkMode = bool.Parse(storedTheme);
                }
                else
                {
                    // 默认使用浏览器的主题偏好
                    _isDarkMode = await _jsRuntime.InvokeAsync<bool>("() => window.matchMedia('(prefers-color-scheme: dark)').matches");
                }
                
                // 应用主题到文档
                ApplyThemeToDocument();
            }
            catch (Exception ex)
            {
                // 如果初始化失败，默认为亮色主题
                _isDarkMode = false;
                ApplyThemeToDocument();
            }
        }
        
        public void ToggleTheme()
        {
            _isDarkMode = !_isDarkMode;
            SaveThemePreference();
            ApplyThemeToDocument();
            OnThemeChanged?.Invoke(_isDarkMode);
        }
        
        public void SetTheme(bool isDarkMode)
        {
            _isDarkMode = isDarkMode;
            SaveThemePreference();
            ApplyThemeToDocument();
            OnThemeChanged?.Invoke(_isDarkMode);
        }
        
        private async void SaveThemePreference()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", THEME_STORAGE_KEY, _isDarkMode.ToString());
            }
            catch (Exception ex)
            {
                // 忽略存储错误
            }
        }
        
        private async void ApplyThemeToDocument()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync(
                    "eval",
                    _isDarkMode 
                        ? "document.documentElement.classList.add('dark-theme'); document.documentElement.classList.remove('light-theme');" 
                        : "document.documentElement.classList.add('light-theme'); document.documentElement.classList.remove('dark-theme');"
                );
            }
            catch (Exception ex)
            {
                // 忽略应用主题错误
            }
        }
    }
}