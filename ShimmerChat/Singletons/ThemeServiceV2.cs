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
        private bool _isInitialized;
        private readonly object _lock = new();
        private const string THEME_STORAGE_KEY = "shimmerchat_current_theme_id";
        private const string THEMES_STORAGE_KEY = "shimmerchat_all_themes";

        private List<Theme> _availableThemes = [];

        public Theme CurrentTheme
        {
            get { EnsureInitialized(); return _currentTheme; }
        }

        public List<Theme> AvailableThemes
        {
            get { EnsureInitialized(); return _availableThemes; }
        }

        public event Action<Theme>? OnThemeChanged;

        public ThemeServiceV2(IJSRuntime jsRuntime, IKVDataService kvDataService)
        {
            _jsRuntime = jsRuntime;
            _kvDataService = kvDataService;
        }

        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            lock (_lock)
            {
                if (_isInitialized) return;
                InitializeThemes();
                _isInitialized = true;
            }
        }

        private void InitializeThemes()
        {
            try
            {
                LoadAllThemes();
                var currentThemeId = _kvDataService.Read(THEME_STORAGE_KEY, THEME_STORAGE_KEY);
                _currentTheme = (!string.IsNullOrEmpty(currentThemeId)
                    ? _availableThemes.FirstOrDefault(t => t.Id == currentThemeId)
                    : null)
                    ?? _availableThemes.FirstOrDefault(t => t.IsDefault)
                    ?? _availableThemes.FirstOrDefault()
                    ?? GetBuiltInThemes().First();
                ApplyTheme(_currentTheme);
            }
            catch
            {
                _availableThemes = GetBuiltInThemes();
                _currentTheme = _availableThemes.First();
                ApplyTheme(_currentTheme);
            }
        }

        public List<Theme> GetBuiltInThemes()
        {
            return
            [
                new Theme
                {
                    Id = "light_default",
                    Name = "Modern Light",
                    Description = "Clean, minimal light theme",
                    IsDefault = true,
                    IsBuiltIn = true,
                    IsDarkMode = false,
                },
                new Theme
                {
                    Id = "dark_default",
                    Name = "Modern Dark",
                    Description = "Clean, minimal dark theme",
                    IsDefault = false,
                    IsBuiltIn = true,
                    IsDarkMode = true,
                    Surface0 = "#0d0d0d",
                    Surface1 = "#1a1a1a",
                    Surface2 = "#242424",
                    Surface3 = "#1f1f1f",
                    Text0 = "#ecedee",
                    Text1 = "#9ba1a6",
                    Text2 = "#6b7280",
                    Border0 = "#2e2e2e",
                    Border1 = "#242424",
                    Accent = "#6c74e0",
                    AccentHover = "#7c83e8",
                    AccentSoft = "#1a1b2e",
                    Success = "#3fb950",
                    SuccessSoft = "#122418",
                    Warning = "#f0a020",
                    WarningSoft = "#1f1808",
                    Danger = "#f85149",
                    DangerSoft = "#1f1114",
                    Info = "#58a6ff",
                    InfoSoft = "#0d1b2e",
                    NodeFlow = "#60a5fa",
                    NodeBranch = "#fbbf24",
                    NodeLink = "#34d399",
                    NodeFragment = "#818cf8",
                    NodePrompt = "#c084fc",
                    NodeTool = "#4ade80",
                    NodeMemory = "#facc15",
                    NodeConfig = "#f87171",
                    NodeSubagent = "#f472b6",
                    NodeDebug = "#94a3b8",
                    ShadowSm = "0 1px 2px rgba(0,0,0,0.3)",
                    ShadowMd = "0 4px 12px rgba(0,0,0,0.4)",
                    ShadowLg = "0 12px 32px rgba(0,0,0,0.5)",
                },
            ];
        }

        public List<Theme> GetUserThemes() =>
            _availableThemes.Where(t => !t.IsBuiltIn).ToList();

        public void SetTheme(string themeId)
        {
            var theme = _availableThemes.FirstOrDefault(t => t.Id == themeId);
            if (theme == null) return;
            _currentTheme = theme;
            SaveCurrentThemeId(themeId);
            ApplyTheme(theme);
            OnThemeChanged?.Invoke(theme);
        }

        public void ApplyTheme(Theme theme)
        {
            try
            {
                var cssVars = BuildCssVariables(theme);
                var js = $@"
                    const root = document.documentElement;
                    root.setAttribute('data-theme', '{(theme.IsDarkMode ? "dark" : "light")}');
                    {cssVars}
                ";
                _jsRuntime.InvokeVoidAsync("eval", js);
            }
            catch { }
        }

        private static string BuildCssVariables(Theme t)
        {
            var sb = new System.Text.StringBuilder();

            void Set(string name, string value) =>
                sb.AppendLine($"root.style.setProperty('{name}', '{value}');");

            // Surface
            Set("--su-surface-0", t.Surface0);
            Set("--su-surface-1", t.Surface1);
            Set("--su-surface-2", t.Surface2);
            Set("--su-surface-3", t.Surface3);

            // Text
            Set("--su-text-0", t.Text0);
            Set("--su-text-1", t.Text1);
            Set("--su-text-2", t.Text2);

            // Border
            Set("--su-border-0", t.Border0);
            Set("--su-border-1", t.Border1);

            // Accent
            Set("--su-accent", t.Accent);
            Set("--su-accent-hover", t.AccentHover);
            Set("--su-accent-soft", t.AccentSoft);

            // Semantic
            Set("--su-success", t.Success);
            Set("--su-success-soft", t.SuccessSoft);
            Set("--su-warning", t.Warning);
            Set("--su-warning-soft", t.WarningSoft);
            Set("--su-danger", t.Danger);
            Set("--su-danger-soft", t.DangerSoft);
            Set("--su-info", t.Info);
            Set("--su-info-soft", t.InfoSoft);

            // Node colors
            Set("--node-flow", t.NodeFlow);
            Set("--node-branch", t.NodeBranch);
            Set("--node-link", t.NodeLink);
            Set("--node-fragment", t.NodeFragment);
            Set("--node-prompt", t.NodePrompt);
            Set("--node-tool", t.NodeTool);
            Set("--node-memory", t.NodeMemory);
            Set("--node-config", t.NodeConfig);
            Set("--node-subagent", t.NodeSubagent);
            Set("--node-debug", t.NodeDebug);

            // Shadows
            Set("--su-shadow-sm", t.ShadowSm);
            Set("--su-shadow-md", t.ShadowMd);
            Set("--su-shadow-lg", t.ShadowLg);

            // Radii
            Set("--su-radius-sm", t.RadiusSm);
            Set("--su-radius-md", t.RadiusMd);
            Set("--su-radius-lg", t.RadiusLg);

            // Spacing
            Set("--su-space-1", t.Space1);
            Set("--su-space-2", t.Space2);
            Set("--su-space-3", t.Space3);
            Set("--su-space-4", t.Space4);
            Set("--su-space-5", t.Space5);
            Set("--su-space-6", t.Space6);
            Set("--su-space-8", t.Space8);
            Set("--su-space-10", t.Space10);

            // Typography
            Set("--su-font-sans", t.FontSans);
            Set("--su-font-mono", t.FontMono);
            Set("--su-font-xs", t.FontXs);
            Set("--su-font-sm", t.FontSm);
            Set("--su-font-base", t.FontBase);
            Set("--su-font-md", t.FontMd);
            Set("--su-font-lg", t.FontLg);

            // Misc
            Set("--su-border-size", t.BorderSize);
            Set("--su-transition", t.Transition);

            return sb.ToString();
        }

        // ── CRUD ────────────────────────────────────────────

        public void CreateTheme(Theme theme)
        {
            if (_availableThemes.Any(t => t.Id == theme.Id))
                throw new InvalidOperationException($"Theme '{theme.Id}' already exists.");
            theme.CreatedAt = DateTime.UtcNow;
            theme.UpdatedAt = DateTime.UtcNow;
            _availableThemes.Add(theme);
            SaveAllThemes();
        }

        public void UpdateTheme(Theme theme)
        {
            var idx = _availableThemes.FindIndex(t => t.Id == theme.Id);
            if (idx < 0) return;
            theme.UpdatedAt = DateTime.UtcNow;
            _availableThemes[idx] = theme;
            _currentTheme = theme;
            ApplyTheme(theme);
            SaveAllThemes();
        }

        public void DeleteTheme(string themeId)
        {
            var theme = _availableThemes.FirstOrDefault(t => t.Id == themeId);
            if (theme == null || theme.IsBuiltIn) return;
            _availableThemes.Remove(theme);
            if (_currentTheme.Id == themeId)
            {
                var fallback = _availableThemes.FirstOrDefault(t => t.IsDefault) ?? _availableThemes.FirstOrDefault() ?? GetBuiltInThemes().First();
                SetTheme(fallback.Id);
            }
            SaveAllThemes();
        }

        public string ExportTheme(string themeId)
        {
            var theme = _availableThemes.FirstOrDefault(t => t.Id == themeId)
                ?? throw new ArgumentException($"Theme '{themeId}' not found.");
            return System.Text.Json.JsonSerializer.Serialize(theme, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        public void ImportTheme(string themeJson)
        {
            var theme = System.Text.Json.JsonSerializer.Deserialize<Theme>(themeJson);
            if (theme == null) return;
            theme.Id = Guid.NewGuid().ToString();
            theme.IsBuiltIn = false;
            theme.IsDefault = false;
            theme.CreatedAt = DateTime.UtcNow;
            theme.UpdatedAt = DateTime.UtcNow;
            _availableThemes.Add(theme);
            SaveAllThemes();
        }

        // ── Persistence ─────────────────────────────────────

        private void LoadAllThemes()
        {
            try
            {
                var json = _kvDataService.Read(THEMES_STORAGE_KEY, THEMES_STORAGE_KEY);
                if (!string.IsNullOrEmpty(json))
                {
                    var saved = System.Text.Json.JsonSerializer.Deserialize<List<Theme>>(json);
                    if (saved != null) _availableThemes = saved;
                }
            }
            catch { _availableThemes = GetBuiltInThemes(); }
            if (_availableThemes.Count == 0)
                _availableThemes = GetBuiltInThemes();
        }

        private void SaveAllThemes()
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_availableThemes);
                _kvDataService.Write(THEMES_STORAGE_KEY, THEMES_STORAGE_KEY, json);
            }
            catch { }
        }

        private void SaveCurrentThemeId(string themeId)
        {
            try { _kvDataService.Write(THEME_STORAGE_KEY, THEME_STORAGE_KEY, themeId); }
            catch { }
        }
    }
}
