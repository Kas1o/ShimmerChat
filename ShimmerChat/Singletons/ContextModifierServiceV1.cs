using System.Reflection;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;
using SharperLLM.Util;
using ShimmerChatBuiltinTools;
using ShimmerChatLib;

namespace ShimmerChat.Singletons
{
    public class ContextModifierServiceV1 : IContextModifierService
    {
        private readonly string PluginsFolder = Path.Combine(AppContext.BaseDirectory, "./Plugins");
        private const string PluginId = "ContextModifierService";
        private const string PresetKey = "modifier_presets";
        private const string LegacyActivatedModifiersKey = "activated_modifiers";

        private readonly IPluginLoaderService _pluginLoaderService;
        private readonly IKVDataService _pluginDataService;

        private ContextModifierPresetCollection _presetCollection = new();

        public List<IContextModifier> LoadedModifiers { get; private set; } = new();

        public List<ActivatedModifier> ActivatedModifiers
        {
            get
            {
                var preset = GetActivePreset();
                return preset?.Modifiers ?? new List<ActivatedModifier>();
            }
        }

        public List<ContextModifierPreset> Presets => _presetCollection.Presets;
        public string ActivePresetId => _presetCollection.ActivePresetId;

        public string ActivePresetName
        {
            get
            {
                var preset = GetActivePreset();
                return preset?.Name ?? "";
            }
        }

        public ContextModifierServiceV1(IPluginLoaderService pluginLoaderService, IKVDataService pluginDataService)
        {
            _pluginLoaderService = pluginLoaderService;
            _pluginDataService = pluginDataService;
            LoadAllModifiers();
            LoadPresets();
        }

        private void LoadAllModifiers()
        {
            var modifierDict = new Dictionary<string, IContextModifier>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var builtinAssembly = typeof(ShimmerChatBuiltinTools.Target).Assembly;
                var builtinModifiers = _pluginLoaderService.LoadImplementationsFromAssembly<IContextModifier>(builtinAssembly);

                foreach (var modifier in builtinModifiers)
                {
                    var name = modifier.info.Name;
                    if (modifierDict.ContainsKey(name))
                        throw new Exception($"ContextModifier名称冲突: {name}");
                    modifierDict[name] = modifier;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载内置ContextModifier失败: {ex.Message}");
            }

            var pluginModifiers = _pluginLoaderService.LoadImplementationsFromPlugins<IContextModifier>(PluginsFolder);
            foreach (var modifier in pluginModifiers)
            {
                var name = modifier.info.Name;
                if (modifierDict.ContainsKey(name))
                    throw new Exception($"ContextModifier名称冲突: {name}");
                modifierDict[name] = modifier;
            }

            LoadedModifiers = modifierDict.Values.ToList();
        }

        private void LoadPresets()
        {
            try
            {
                var json = _pluginDataService.Read(PluginId, PresetKey);
                if (!string.IsNullOrEmpty(json))
                {
                    _presetCollection = DeserializePresetCollection(json);
                }
                else
                {
                    TryMigrateLegacyData();
                }

                if (_presetCollection.Presets.Count == 0)
                {
                    var defaultPreset = new ContextModifierPreset { Name = "Default" };
                    _presetCollection.Presets.Add(defaultPreset);
                    _presetCollection.ActivePresetId = defaultPreset.Id;
                    SavePresets();
                }
                else if (string.IsNullOrEmpty(_presetCollection.ActivePresetId)
                    || !_presetCollection.Presets.Any(p => p.Id == _presetCollection.ActivePresetId))
                {
                    _presetCollection.ActivePresetId = _presetCollection.Presets[0].Id;
                    SavePresets();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载ContextModifier预设失败: {ex.Message}");
                _presetCollection = new ContextModifierPresetCollection();
                var defaultPreset = new ContextModifierPreset { Name = "Default" };
                _presetCollection.Presets.Add(defaultPreset);
                _presetCollection.ActivePresetId = defaultPreset.Id;
            }
        }

        private void TryMigrateLegacyData()
        {
            try
            {
                var legacyJson = _pluginDataService.Read(PluginId, LegacyActivatedModifiersKey);
                if (!string.IsNullOrEmpty(legacyJson))
                {
                    var legacyModifiers = JsonConvert.DeserializeObject<List<LegacyActivatedModifier>>(legacyJson);
                    if (legacyModifiers != null && legacyModifiers.Count > 0)
                    {
                        var defaultPreset = new ContextModifierPreset
                        {
                            Name = "Default",
                            Modifiers = legacyModifiers
                                .Select(m => new ActivatedModifier
                                {
                                    Name = m.Name,
                                    Config = new LegacyModifierConfig { Value = m.Value },
                                    IsEnabled = m.IsEnabled
                                })
                                .ToList()
                        };
                        _presetCollection.Presets.Add(defaultPreset);
                        _presetCollection.ActivePresetId = defaultPreset.Id;
                        SavePresets();
                        Console.WriteLine($"已从旧格式迁移 {legacyModifiers.Count} 个ContextModifier到新预设系统");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"迁移旧ContextModifier数据失败: {ex.Message}");
            }
        }

        private ContextModifierPreset? GetActivePreset()
        {
            return _presetCollection.Presets.FirstOrDefault(p => p.Id == _presetCollection.ActivePresetId);
        }

        private void SavePresets()
        {
            try
            {
                var json = SerializePresetCollection(_presetCollection);
                _pluginDataService.Write(PluginId, PresetKey, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存ContextModifier预设失败: {ex.Message}");
            }
        }

        public void LoadActivatedModifiers()
        {
        }

        public void SaveActivatedModifiers()
        {
            SavePresets();
        }

        public void ActivateModifier(string modifierName, ModifierConfig config)
        {
            var modifier = LoadedModifiers.FirstOrDefault(m => m.info.Name == modifierName);
            if (modifier != null)
            {
                var preset = GetActivePreset();
                if (preset != null)
                {
                    preset.Modifiers.Add(new ActivatedModifier
                    {
                        Name = modifierName,
                        Config = config
                    });
                    SavePresets();
                }
            }
        }

        public void ActivateModifier(string modifierName, string inputValue)
        {
            var modifier = LoadedModifiers.FirstOrDefault(m => m.info.Name == modifierName);
            if (modifier != null)
            {
                var config = Activator.CreateInstance(modifier.ConfigType) as ModifierConfig;
                if (config is LegacyModifierConfig legacy)
                    legacy.Value = inputValue;
                ActivateModifier(modifierName, config ?? new LegacyModifierConfig { Value = inputValue });
            }
        }

        public void RemoveActivatedModifier(int index)
        {
            var preset = GetActivePreset();
            if (preset != null && index >= 0 && index < preset.Modifiers.Count)
            {
                preset.Modifiers.RemoveAt(index);
                SavePresets();
            }
        }

        public void ReorderActivatedModifier(int oldIndex, int newIndex)
        {
            var preset = GetActivePreset();
            if (preset != null
                && oldIndex >= 0 && oldIndex < preset.Modifiers.Count
                && newIndex >= 0 && newIndex < preset.Modifiers.Count)
            {
                var item = preset.Modifiers[oldIndex];
                preset.Modifiers.RemoveAt(oldIndex);
                preset.Modifiers.Insert(newIndex, item);
                SavePresets();
            }
        }

        public void ClearActivatedModifiers()
        {
            var preset = GetActivePreset();
            if (preset != null)
            {
                preset.Modifiers.Clear();
                SavePresets();
            }
        }

        public void ApplyModifiers(ContextDocument context, Chat chat, Agent agent)
        {
            foreach (var activatedModifier in ActivatedModifiers)
            {
                var modifier = LoadedModifiers.FirstOrDefault(m => m.info.Name == activatedModifier.Name);
                if (modifier != null && activatedModifier.IsEnabled)
                {
                    var expectedType = modifier.ConfigType;
                    if (expectedType != typeof(LegacyModifierConfig)
                        && activatedModifier.Config.GetType() != expectedType)
                    {
                        throw new InvalidOperationException(
                            $"ContextModifier \"{activatedModifier.Name}\" 的配置格式已过时，请到 /contextmanager 重新编辑该修改器以迁移到新的配置格式。");
                    }

                    modifier.ModifyContext(context, activatedModifier.Config, chat, agent);
                }
            }
        }

        public void ApplyModifiers(PromptBuilder promptBuilder, Chat chat, Agent agent)
        {
            var segments = promptBuilder.Messages
                .Select(m => new ContextSegment { Message = m.Item1, From = m.Item2 })
                .ToList();

            var context = new ContextDocument { Segments = segments };
            ApplyModifiers(context, chat, agent);
            promptBuilder.Messages = context.GetMessages();
        }

        public void CreatePreset(string name)
        {
            var newPreset = new ContextModifierPreset { Name = name };
            _presetCollection.Presets.Add(newPreset);
            _presetCollection.ActivePresetId = newPreset.Id;
            SavePresets();
        }

        public void DeletePreset(string presetId)
        {
            if (_presetCollection.Presets.Count <= 1)
                return;

            var preset = _presetCollection.Presets.FirstOrDefault(p => p.Id == presetId);
            if (preset != null)
            {
                _presetCollection.Presets.Remove(preset);
                if (_presetCollection.ActivePresetId == presetId)
                {
                    _presetCollection.ActivePresetId = _presetCollection.Presets[0].Id;
                }
                SavePresets();
            }
        }

        public void RenamePreset(string presetId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return;

            var preset = _presetCollection.Presets.FirstOrDefault(p => p.Id == presetId);
            if (preset != null)
            {
                preset.Name = newName;
                SavePresets();
            }
        }

        public void SwitchToPreset(string presetId)
        {
            if (_presetCollection.Presets.Any(p => p.Id == presetId))
            {
                _presetCollection.ActivePresetId = presetId;
                SavePresets();
            }
        }

        #region Serialization

        private static readonly JsonSerializerSettings PresetSerializerSettings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            SerializationBinder = new ModifierConfigSerializationBinder()
        };

        private static string SerializePresetCollection(ContextModifierPresetCollection collection)
        {
            return JsonConvert.SerializeObject(collection, PresetSerializerSettings);
        }

        private static ContextModifierPresetCollection DeserializePresetCollection(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<ContextModifierPresetCollection>(json, PresetSerializerSettings)
                    ?? new ContextModifierPresetCollection();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Deserialization with TypeNameHandling failed, trying legacy format: {ex.Message}");
                return DeserializeLegacyPresetCollection(json);
            }
        }

        private static ContextModifierPresetCollection DeserializeLegacyPresetCollection(string json)
        {
            var dto = JsonConvert.DeserializeObject<LegacyPresetCollectionDto>(json);
            if (dto == null)
                return new ContextModifierPresetCollection();

            var collection = new ContextModifierPresetCollection
            {
                ActivePresetId = dto.ActivePresetId
            };

            foreach (var presetDto in dto.Presets)
            {
                var preset = new ContextModifierPreset
                {
                    Id = presetDto.Id,
                    Name = presetDto.Name,
                    Modifiers = presetDto.Modifiers
                        .Select(m => new ActivatedModifier
                        {
                            Name = m.Name,
                            Config = new LegacyModifierConfig { Value = m.Value },
                            IsEnabled = m.IsEnabled
                        })
                        .ToList()
                };
                collection.Presets.Add(preset);
            }

            return collection;
        }

        #endregion

        #region Serialization DTOs

        private class LegacyActivatedModifier
        {
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";
            public bool IsEnabled { get; set; } = true;
        }

        private class LegacyPresetDto
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "Default";
            public List<LegacyActivatedModifier> Modifiers { get; set; } = new();
        }

        private class LegacyPresetCollectionDto
        {
            public string ActivePresetId { get; set; } = "";
            public List<LegacyPresetDto> Presets { get; set; } = new();
        }

        #endregion
    }

    public class ModifierConfigSerializationBinder : ISerializationBinder
    {
        public Type BindToType(string? assemblyName, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentNullException(nameof(typeName));

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType(typeName);
                    if (type != null)
                        return type;
                }
                catch { }
            }

            throw new TypeLoadException($"Cannot resolve type '{typeName}'");
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string typeName)
        {
            assemblyName = null;
            typeName = serializedType.FullName;
        }
    }
}
