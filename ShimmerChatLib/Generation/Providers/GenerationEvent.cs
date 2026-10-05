using System;
using System.Collections.Generic;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// Agent 级生成事件实例。本质上是某个 <see cref="IGenerationProvider"/>
    /// 类型在特定 Agent 上的一份配置副本，属于 Agent 的附属。
    ///
    /// 持久化于 KVData（空间 "GenerationProviders"，键为 Agent GUID），
    /// 由宿主在启动时加载并实例化对应提供器。
    /// </summary>
    public class GenerationEvent
    {
        /// <summary>事件唯一 Id</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>对应提供器类型的 FullName（白名单解析，找不到时标记为缺失插件）</summary>
        public string ProviderTypeName { get; set; } = "";

        /// <summary>用户可编辑的事件名称</summary>
        public string Name { get; set; } = "";

        /// <summary>是否启用（禁用时宿主不启动该提供器实例）</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 提供器自有配置参数 JSON（即提供器实例标记了 <see cref="NodePropertyAttribute"/>
        /// 或 <see cref="PipelineTreePropertyAttribute"/> 的公有属性的序列化结果）。
        /// 不使用 $type 多态，类型由 <see cref="ProviderTypeName"/> 决定。
        /// </summary>
        public string? ConfigJson { get; set; }

        /// <summary>最近一次触发时间</summary>
        public DateTime? LastTriggerTime { get; set; }

        /// <summary>最近一次运行结果（"OK" 或错误描述），供 UI 展示</summary>
        public string? LastRunStatus { get; set; }

        /// <summary>
        /// 本事件生成的 Chat GUIDs（最新在前）。
        /// 这些 Chat 不进入 Agent.ChatGuids，因此默认对话浏览界面不显示；
        /// 事件管理界面通过此列表访问它们。
        /// </summary>
        public List<Guid> GeneratedChatGuids { get; set; } = new();
    }

    /// <summary>
    /// 管线树覆盖。由提供器通过自身声明的参数（<see cref="PipelineTreePropertyAttribute"/>）
    /// 在触发时提供（见 <see cref="IGenerationProvider.GetPipelineOverrides"/>），
    /// 字段为 null 时表示继承 Agent 的设置。
    /// Render 树覆盖由宿主在触发时盖印到 Chat.SourceRenderModifierTreeJson，
    /// 渲染阶段直接读取该字段。
    /// </summary>
    public class PipelineTreeOverrides
    {
        public string? PreGenerationTreeJson { get; init; }
        public string? PostGenerationTreeJson { get; init; }
        public string? RenderModifierTreeJson { get; init; }
    }
}
