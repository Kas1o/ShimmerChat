using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 生成提供器接口。插件实现此接口以提供非用户直接启动的生成会话
    /// （如 CRON 定时、聊天软件集成、特定 Hook 等）。
    ///
    /// 提供器"类型"由插件定义；每个 Agent 可以挂载多个提供器"事件"实例
    /// （<see cref="GenerationEvent"/>）。事件实例的公有属性（标记
    /// <see cref="NodePropertyAttribute"/> 或 <see cref="PipelineTreePropertyAttribute"/>）
    /// 即该事件的可编辑配置参数，由宿主序列化到 <see cref="GenerationEvent.ConfigJson"/>。
    /// 管线树覆盖同样由提供器通过这些参数自行声明，而非框架固定字段。
    ///
    /// 实现约束：
    /// - 运行于后台线程，只能依赖 Singleton 生命周期的服务；
    /// - StartAsync 抛异常表示启动失败（如配置非法），宿主会记录错误并保留事件；
    /// - StopAsync 必须能被安全地多次调用并及时返回。
    /// </summary>
    public interface IGenerationProvider
    {
        /// <summary>
        /// 启动提供器（开始监听 / 调度）。配置属性在调用前已被宿主填充。
        /// </summary>
        Task StartAsync(ProviderRuntimeContext runtime, CancellationToken ct = default);

        /// <summary>
        /// 停止提供器，释放资源。
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// 手动触发（UI "立即触发"）时使用的触发消息文本。
        /// 默认返回 null，表示不追加触发消息。
        /// </summary>
        string? GetManualTriggerText() => null;

        /// <summary>
        /// 返回当前配置对应的管线树覆盖（通常读取自身的
        /// <see cref="PipelineTreePropertyAttribute"/> 参数）。
        /// 提供器在 TriggerAsync 时未显式传递 overrides 时，宿主会从
        /// 运行中（或临时实例化）的提供器拉取此值。默认返回 null = 全部继承 Agent。
        /// </summary>
        PipelineTreeOverrides? GetPipelineOverrides() => null;
    }

    /// <summary>
    /// 提供器类型元数据。NameKey / DescriptionKey 为本地化 Key，由 LocService 解析。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class ProviderInfoAttribute : Attribute
    {
        /// <summary>提供器显示名称本地化 Key（建议前缀 "provider."）</summary>
        public string NameKey { get; }

        /// <summary>提供器描述本地化 Key</summary>
        public string? DescriptionKey { get; init; }

        /// <summary>显示图标</summary>
        public string Icon { get; init; } = "⚡";

        public ProviderInfoAttribute(string nameKey)
        {
            NameKey = nameKey;
        }
    }
}
