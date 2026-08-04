using System;
using System.Collections.Generic;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 提供器类型元数据，供事件管理 UI 的"添加事件"菜单使用。
    /// </summary>
    public record ProviderTypeInfo(
        string TypeName,
        Type Type,
        string NameKey,
        string? DescriptionKey,
        string Icon
    );

    /// <summary>
    /// 生成提供器注册表。扫描所有 <see cref="IGenerationProvider"/> 实现，
    /// 提供按类型名查找和实例创建（支持构造函数注入）。
    /// </summary>
    public interface IGenerationProviderRegistry
    {
        /// <summary>获取所有已发现的提供器类型</summary>
        IReadOnlyList<ProviderTypeInfo> GetAllProviderTypes();

        /// <summary>按类型 FullName 查找提供器类型，不存在返回 null</summary>
        ProviderTypeInfo? GetProviderType(string typeName);

        /// <summary>创建提供器实例（配置属性未填充，由调用方 Populate）</summary>
        IGenerationProvider? CreateInstance(ProviderTypeInfo typeInfo);
    }

    /// <summary>
    /// Agent 级生成事件存储。事件列表按 Agent GUID 持久化于 KVData。
    /// </summary>
    public interface IGenerationEventStore
    {
        /// <summary>读取指定 Agent 的全部生成事件</summary>
        List<GenerationEvent> GetEvents(Guid agentGuid);

        /// <summary>读取单个事件，不存在返回 null</summary>
        GenerationEvent? GetEvent(Guid agentGuid, string eventId);

        /// <summary>整体保存指定 Agent 的事件列表</summary>
        void SaveEvents(Guid agentGuid, List<GenerationEvent> events);

        /// <summary>更新单个事件（不存在则追加）</summary>
        void UpdateEvent(Guid agentGuid, GenerationEvent evt);

        /// <summary>
        /// 仅更新事件的运行状态字段（LastTriggerTime / LastRunStatus），
        /// 其余字段以存储中的最新值为准，避免后台线程用生成开始时的
        /// 旧快照覆盖生成期间用户对事件配置的并发修改。
        /// </summary>
        void UpdateEventStatus(Guid agentGuid, string eventId,
            DateTime? lastTriggerTime, string? lastRunStatus);
    }
}
