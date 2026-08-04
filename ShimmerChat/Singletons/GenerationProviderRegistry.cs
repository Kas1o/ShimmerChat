using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 生成提供器注册表。扫描所有程序集中的 <see cref="IGenerationProvider"/> 实现，
    /// 提取 <see cref="ProviderInfoAttribute"/> 元数据，供事件管理 UI 与宿主使用。
    /// </summary>
    public class GenerationProviderRegistry : IGenerationProviderRegistry
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GenerationProviderRegistry> _logger;
        private readonly Lazy<List<ProviderTypeInfo>> _types;

        public GenerationProviderRegistry(IPluginLoaderService pluginLoader,
            IServiceProvider serviceProvider, ILogger<GenerationProviderRegistry> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _types = new Lazy<List<ProviderTypeInfo>>(() => ScanAll(pluginLoader));
        }

        public IReadOnlyList<ProviderTypeInfo> GetAllProviderTypes() => _types.Value;

        public ProviderTypeInfo? GetProviderType(string typeName)
        {
            return _types.Value.FirstOrDefault(t => t.TypeName == typeName);
        }

        public IGenerationProvider? CreateInstance(ProviderTypeInfo typeInfo)
        {
            try
            {
                return (IGenerationProvider?)ActivatorUtilities
                    .CreateInstance(_serviceProvider, typeInfo.Type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GenerationProviderRegistry] 无法创建提供器实例 {TypeName}: {Message}",
                    typeInfo.TypeName, ex.Message);
                return null;
            }
        }

        private List<ProviderTypeInfo> ScanAll(IPluginLoaderService pluginLoader)
        {
            var types = pluginLoader.GetImplementingTypes(typeof(IGenerationProvider));
            var result = new List<ProviderTypeInfo>(types.Count);

            foreach (var type in types)
            {
                try
                {
                    var info = type.GetCustomAttribute<ProviderInfoAttribute>();
                    result.Add(new ProviderTypeInfo(
                        type.FullName!,
                        type,
                        info?.NameKey ?? type.Name,
                        info?.DescriptionKey,
                        info?.Icon ?? "⚡"
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[GenerationProviderRegistry] 扫描提供器类型失败 ({TypeName}): {Message}",
                        type.FullName, ex.Message);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Agent 级生成事件存储。按 Agent GUID 持久化于 KVData 空间 "GenerationProviders"。
    /// </summary>
    public class GenerationEventStore : IGenerationEventStore
    {
        private const string SpaceId = "GenerationProviders";

        private readonly IKVDataService _kvData;
        private readonly ILogger<GenerationEventStore> _logger;

        public GenerationEventStore(IKVDataService kvData, ILogger<GenerationEventStore> logger)
        {
            _kvData = kvData;
            _logger = logger;
        }

        public List<GenerationEvent> GetEvents(Guid agentGuid)
        {
            var json = _kvData.Read(SpaceId, agentGuid.ToString());
            if (string.IsNullOrEmpty(json))
                return new List<GenerationEvent>();

            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<List<GenerationEvent>>(json)
                    ?? new List<GenerationEvent>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GenerationEventStore] 反序列化事件列表失败 (Agent {AgentGuid}): {Message}",
                    agentGuid, ex.Message);
                return new List<GenerationEvent>();
            }
        }

        public GenerationEvent? GetEvent(Guid agentGuid, string eventId)
        {
            return GetEvents(agentGuid).FirstOrDefault(e => e.Id == eventId);
        }

        public void SaveEvents(Guid agentGuid, List<GenerationEvent> events)
        {
            _kvData.Write(SpaceId, agentGuid.ToString(),
                Newtonsoft.Json.JsonConvert.SerializeObject(events));
        }

        public void UpdateEvent(Guid agentGuid, GenerationEvent evt)
        {
            var events = GetEvents(agentGuid);
            var index = events.FindIndex(e => e.Id == evt.Id);
            if (index >= 0)
                events[index] = evt;
            else
                events.Add(evt);
            SaveEvents(agentGuid, events);
        }
    }
}
