using System;
using System.Threading;
using System.Threading.Tasks;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 提供器触发服务。由宿主实现：创建 Chat、写入触发消息、
    /// 以提供器声明的管线树覆盖执行一次完整生成。
    /// </summary>
    public interface IProviderTriggerService
    {
        /// <summary>
        /// 触发一次生成，返回本次生成使用的 Chat GUID。
        /// </summary>
        /// <param name="agentGuid">目标 Agent</param>
        /// <param name="eventId">目标事件 Id</param>
        /// <param name="triggerText">触发消息文本（以 System 身份写入 Chat，可为空）</param>
        /// <param name="overrides">
        /// 本次生成的管线树覆盖；为 null 时宿主从提供器实例拉取
        /// （<see cref="IGenerationProvider.GetPipelineOverrides"/>）。
        /// </param>
        Task<Guid> TriggerAsync(Guid agentGuid, string eventId,
            string? triggerText = null, PipelineTreeOverrides? overrides = null,
            CancellationToken ct = default);
    }

    /// <summary>
    /// 提供器运行时上下文。宿主在 StartAsync 前构造，
    /// 只暴露 Singleton 生命周期的服务（提供器运行于后台线程）。
    /// </summary>
    public class ProviderRuntimeContext
    {
        /// <summary>所属 Agent GUID</summary>
        public required Guid AgentGuid { get; init; }

        /// <summary>本事件实例（LastTriggerTime / LastRunStatus 由宿主维护）</summary>
        public required GenerationEvent Event { get; init; }

        /// <summary>触发服务</summary>
        public required IProviderTriggerService TriggerService { get; init; }

        /// <summary>结构化日志输出（后台错误的落盘通道）</summary>
        public required IDebugOutputService DebugOutput { get; init; }

        /// <summary>KV 存储（提供器私有数据请使用独立的 spaceId）</summary>
        public required IKVDataService KVData { get; init; }

        /// <summary>本地化服务</summary>
        public required ILocService LocService { get; init; }

        /// <summary>
        /// 触发一次生成（便捷方法）。返回本次生成的 Chat GUID。
        /// overrides 为 null 时宿主从本提供器实例拉取 GetPipelineOverrides()。
        /// </summary>
        public Task<Guid> TriggerAsync(string? triggerText = null,
            PipelineTreeOverrides? overrides = null, CancellationToken ct = default)
            => TriggerService.TriggerAsync(AgentGuid, Event.Id, triggerText, overrides, ct);
    }
}
