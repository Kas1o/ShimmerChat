using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 渲染修改节点接口。节点通过 <see cref="RenderEnv.UpdateContent"/> 修改内容，
    /// 失败时抛出 <see cref="RenderNodeException"/>。
    /// </summary>
    public interface IRenderModifierNode : ITreeNode
    {
        void Execute(RenderNodeExecutionContext context);
    }

    /// <summary>
    /// 渲染节点执行异常。携带错误码、节点信息供上层构建错误 UI。
    /// </summary>
    public class RenderNodeException : Exception
    {
        public string Code { get; }
        public string? NodeId { get; }
        public string? NodeName { get; }

        public RenderNodeException(string code, string message, string? nodeId = null, string? nodeName = null)
            : base(message)
        {
            Code = code;
            NodeId = nodeId;
            NodeName = nodeName;
        }
    }

    /// <summary>
    /// 渲染节点执行上下文。
    /// </summary>
    public class RenderNodeExecutionContext
    {
        public RenderEnv Env { get; }

        public RenderNodeExecutionContext(RenderEnv env) => Env = env;
    }

    /// <summary>
    /// 渲染管线环境。节点通过 GetContent/UpdateContent 修改内容，
    /// 每次 UpdateContent 自动记录变更到 ChangeLog。
    /// </summary>
    public class RenderEnv
    {
        private string _content;

        /// <summary>变更记录，每次 UpdateContent 自动追加</summary>
        public List<RenderChangeRecord> ChangeLog { get; } = new();

        public ITreeNodeSerializer Serializer { get; }
        public IKVDataService KVData { get; }
        public Chat? Chat { get; }
        public Agent? Agent { get; }

        public RenderEnv(string initialContent, ITreeNodeSerializer serializer, IKVDataService kvData,
            Chat? chat = null, Agent? agent = null)
        {
            _content = initialContent;
            Serializer = serializer;
            KVData = kvData;
            Chat = chat;
            Agent = agent;
        }

        public string GetContent() => _content;

        public void UpdateContent(string newContent, string nodeName, string nodeType)
        {
            ChangeLog.Add(new RenderChangeRecord
            {
                NodeName = nodeName,
                NodeType = nodeType,
                Before = _content,
                After = newContent
            });
            _content = newContent;
        }
    }

    /// <summary>
    /// 单次内容变更记录
    /// </summary>
    public class RenderChangeRecord
    {
        public string NodeName { get; init; } = "";
        public string NodeType { get; init; } = "";
        public string Before { get; init; } = "";
        public string After { get; init; } = "";
    }
}
