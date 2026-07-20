using Newtonsoft.Json;
using ShimmerChatBuiltin.ToolManage;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.ToolManage
{
    /// <summary>
    /// 首次启动时创建默认工具预设（IsDefault = true），使生成管线在 ToolManager 被访问前就能正常加载。
    /// 后续由用户在 ToolManager 中自行编辑。
    /// </summary>
    public class DefaultToolPresetInitializer : IPluginInitializer
    {
        private readonly IKVDataService _kvData;

        public DefaultToolPresetInitializer(IKVDataService kvData)
        {
            _kvData = kvData;
        }

        public Task InitializeAsync()
        {
            List<ToolPreset>? existing = null;
            var json = _kvData.Read("ToolPresets", "__presets__");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    existing = JsonConvert.DeserializeObject<List<ToolPreset>>(json);
                }
                catch (JsonException)
                {
                    // 旧格式 List<string>，留给 ToolManager.LoadPresets 做迁移
                }
                if (existing != null && existing.Any(p => p.IsDefault))
                    return Task.CompletedTask;
            }

            var defaultPreset = new ToolPreset
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Default",
                IsDefault = true,
                EnabledToolTypeNames = new List<string>()
            };

            var presets = existing ?? new List<ToolPreset>();
            presets.Insert(0, defaultPreset);
            _kvData.Write("ToolPresets", "__presets__", JsonConvert.SerializeObject(presets));
            return Task.CompletedTask;
        }
    }
}
