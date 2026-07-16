using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Interface
{
    public interface IRenderModifierManager
    {
        /// <summary>执行渲染节点树，返回处理后的文本。失败时抛出异常。</summary>
        string Render(Agent? agent, string content, Chat? chat = null);

        /// <summary>执行渲染节点树，返回内容 + 完整变更记录。失败时抛出异常。</summary>
        (string Content, List<RenderChangeRecord> ChangeLog) RenderWithLog(
            Agent? agent, string content, Chat? chat = null);
    }
}
