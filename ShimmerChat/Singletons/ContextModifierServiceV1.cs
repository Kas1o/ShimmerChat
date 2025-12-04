using System.Reflection;
using System.Text.Json;
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
        private const string ActivatedModifiersKey = "activated_modifiers";
        
        private readonly IPluginLoaderService _pluginLoaderService;
        private readonly IKVDataService _pluginDataService;

        public List<IContextModifier> LoadedModifiers { get; private set; } = new();
        public List<ActivatedModifier> ActivatedModifiers { get; private set; } = new();

        public ContextModifierServiceV1(IPluginLoaderService pluginLoaderService, IKVDataService pluginDataService)
        {
            _pluginLoaderService = pluginLoaderService;
            _pluginDataService = pluginDataService;
            LoadAllModifiers();
            LoadActivatedModifiers();
        }

        private void LoadAllModifiers()
        {
            var modifierDict = new Dictionary<string, IContextModifier>(StringComparer.OrdinalIgnoreCase);

            // 1. 加载 ShimmerChatBuiltin 项目的ContextModifier
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

            // 2. 加载插件中的ContextModifier
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

        private void LoadActivatedModifiers()
        {
            try
            {
                var json = _pluginDataService.Read(PluginId, ActivatedModifiersKey);
                if (!string.IsNullOrEmpty(json))
                {
                    ActivatedModifiers = JsonSerializer.Deserialize<List<ActivatedModifier>>(json) ?? new List<ActivatedModifier>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载激活的ContextModifier失败: {ex.Message}");
                ActivatedModifiers = new List<ActivatedModifier>();
            }
        }

        public void SaveActivatedModifiers()
        {
            try
            {
                var json = JsonSerializer.Serialize(ActivatedModifiers);
                _pluginDataService.Write(PluginId, ActivatedModifiersKey, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存激活的ContextModifier失败: {ex.Message}");
            }
        }

        public void ActivateModifier(string modifierName, string inputValue)
        {
            // 验证Modifier是否存在
            var modifier = LoadedModifiers.FirstOrDefault(m => m.info.Name == modifierName);
            if (modifier != null)
            {
                ActivatedModifiers.Add(new ActivatedModifier
                {
                    Name = modifierName,
                    Value = inputValue
                });
                SaveActivatedModifiers();
            }
        }

        public void RemoveActivatedModifier(int index)
        {
            if (index >= 0 && index < ActivatedModifiers.Count)
            {
                ActivatedModifiers.RemoveAt(index);
                SaveActivatedModifiers();
            }
        }

        public void ClearActivatedModifiers()
        {
            ActivatedModifiers.Clear();
            SaveActivatedModifiers();
        }

        public void ApplyModifiers(PromptBuilder promptBuilder, Chat chat, Agent agent)
        {
            foreach (var activatedModifier in ActivatedModifiers)
            {
                var modifier = LoadedModifiers.FirstOrDefault(m => m.info.Name == activatedModifier.Name);
                if (modifier != null)
                {
                    var modifierInput = activatedModifier.Value;
                    modifier.ModifyContext(promptBuilder, modifierInput, chat, agent);
                }
            }
        }
    }
}