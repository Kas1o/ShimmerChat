using SharperLLM.FunctionCalling;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// ShimmerChat 2.0 Tool 抽象。
    /// 依赖通过构造函数注入。无参构造的工具可被 ToolInstantiateNode 自动发现和批量开关；
    /// 有参构造的工具通过专用节点手动实例化并提供依赖。
    /// </summary>
    public interface IToolV2
    {
        /// <summary>
        /// 工具名称（唯一标识）
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 工具描述（给 LLM 看）
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 获取 Function Calling 工具定义
        /// </summary>
        SharperLLM.FunctionCalling.Tool GetDefinition();

        /// <summary>
        /// 执行工具
        /// </summary>
        /// <param name="input">JSON 格式的参数</param>
        /// <returns>执行结果文本</returns>
        Task<string> ExecuteAsync(string input);
    }
}
