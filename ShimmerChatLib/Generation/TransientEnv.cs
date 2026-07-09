using SharperLLM.API;
using SharperLLM.Util;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 每次生成时重新构建的临时环境
    /// </summary>
    public class TransientEnv
    {
        /// <summary>
        /// 上下文片段列表
        /// </summary>
        public List<ContextSegment> Fragments { get; set; } = new();

        /// <summary>
        /// 本次生成可用的 Tool 实例列表（由节点在 ExecuteAsync 中填充）
        /// </summary>
        public List<IToolV2> Tools { get; set; } = new();

        /// <summary>
        /// 当前使用的 API 实例
        /// </summary>
        public ILLMAPI? API { get; set; }

        /// <summary>
        /// 节点间共享状态
        /// </summary>
        public Dictionary<string, object> SharedState { get; set; } = new();
    }
}
