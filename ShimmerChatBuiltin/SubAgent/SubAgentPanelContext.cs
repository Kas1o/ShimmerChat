using ShimmerChatLib.Panel;

namespace ShimmerChatBuiltin.SubAgent
{
    /// <summary>
    /// 子代理配置编辑器的宿主契约：宿主只有一条 <see cref="SubAgentConfig"/> 记录，
    /// <strong>不存在</strong>活的 Agent 实例。因此该类型在<strong>类型层面</strong>就没有
    /// <c>Agent</c> 属性——依赖 Agent 实体的面板无法在这种宿主里被渲染，
    /// 不需要（也不应该）用 null 字段当哨兵值。
    /// </summary>
    public class SubAgentPanelContext : AgentPanelContext
    {
        /// <summary>当前子代理配置。</summary>
        public required SubAgentConfig Config { get; init; }

        public override string DraftKey => $"subagent:{Config.Guid:N}";

        public SubAgentPanelContext() => HostName = "SubAgentConfigurationPanel";
    }
}
