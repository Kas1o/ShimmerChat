using SharperLLM.FunctionCalling;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 结构化工具接口。与 <see cref="IToolV2"/> 共存：
    /// <list type="bullet">
    /// <item>新工具实现 <see cref="IToolV3"/>，拿到 <see cref="CancellationToken"/>、类型化参数和结构化结果；</item>
    /// <item>旧工具继续实现 <see cref="IToolV2"/>，由执行节点包装成 <see cref="ToolResult"/>，行为不变；</item>
    /// <item>需要自动发现的工具仍然实现 <see cref="IAutoCreateToolV2"/>（它继承 <see cref="IToolV2"/>），
    /// 再额外实现本接口即可，无需新的注册表或元数据。</item>
    /// </list>
    /// <para>
    /// <b>取消契约</b>：实现必须响应 <c>ct</c>。阻塞式等待（例如等待用户输入）只能通过
    /// <c>task.WaitAsync(ct)</c> 之类可取消的形态进行，禁止裸 <c>await tcs.Task</c>——
    /// 否则整个对话在本轮内无法停止。
    /// </para>
    /// </summary>
    public interface IToolV3 : IToolV2
    {
        /// <summary>执行一次工具调用。预期内的失败返回 <see cref="ToolResult.Fail"/>，不要抛异常。</summary>
        Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct);

        /// <summary>
        /// 旧的字符串式执行入口。实现 <see cref="IToolV3"/> 的工具不需要、也不应该被通过它调用：
        /// 执行节点总是优先走结构化重载。这里保留默认实现只为满足接口继承。
        /// </summary>
        Task<string> IToolV2.ExecuteAsync(string input) => throw new NotSupportedException(
            $"{GetType().FullName} implements IToolV3; call ExecuteAsync(ToolInvocationContext, CancellationToken) instead.");
    }
}
