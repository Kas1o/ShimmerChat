using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Interface
{
    public interface IRenderModifierManager
    {
        /// <summary>执行渲染节点树，返回处理后的文本</summary>
        string Render(Agent? agent, string content, Chat? chat = null);

        /// <summary>执行渲染节点树，返回结果 + 完整变更记录</summary>
        (RenderNodeResult Result, List<RenderChangeRecord> ChangeLog) RenderWithLog(
            Agent? agent, string content, Chat? chat = null);
    }
}
