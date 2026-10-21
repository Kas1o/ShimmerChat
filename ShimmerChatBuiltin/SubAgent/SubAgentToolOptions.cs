namespace ShimmerChatBuiltin.SubAgent
{
    /// <summary>
    /// 单个 SubAgent 注册项在 <see cref="SubAgentToolV2"/> 里的运行选项。
    /// <para>
    /// 开关必须<strong>按配置项</strong>携带：同一轮生成里多个 <c>SubAgentToolNode</c>
    /// 共享同一个工具实例，挂在工具级别会互相串。
    /// </para>
    /// </summary>
    public sealed class SubAgentToolOptions
    {
        /// <summary>
        /// true = 继承父级 AgentGuid（默认，保持既有行为）；
        /// false = 使用 <see cref="SubAgentConfig.Guid"/> 作为独立 Agent 身份
        /// （与 <c>SubAgentNode.SharedGuid = false</c> 语义相同）。
        /// </summary>
        public bool SharedAgentGuid { get; init; } = true;

        /// <summary>
        /// true = 继承父级 ChatGuid（默认，保持既有行为）；
        /// false = 使用会话 Id 作为独立 Chat 作用域（每个会话一份变量/记忆）。
        /// </summary>
        public bool SharedChatGuid { get; init; } = true;

        /// <summary>默认选项：完全继承父级身份。</summary>
        public static SubAgentToolOptions InheritParent { get; } = new();
    }
}
