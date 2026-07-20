using SharperLLM.FunctionCalling;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// ShimmerChat 2.0 Tool 纯执行接口。
    /// 只定义 LLM 可调用的工具能力，不暴露 Name/Description。
    /// 需要自动发现和批量管理的工具请实现 <see cref="IAutoCreateToolV2"/>。
    /// 有参构造的工具由专用节点手动实例化并提供依赖，只实现此接口即可。
    /// </summary>
    public interface IToolV2
    {
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
