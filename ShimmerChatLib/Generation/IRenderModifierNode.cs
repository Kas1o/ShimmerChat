using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 渲染修改节点接口。对标 IPreGenerationNode 的设计。
    /// 通过 RenderNodeExecutionContext 携带所有依赖，返回 RenderNodeResult。
    /// </summary>
    public interface IRenderModifierNode : ITreeNode
    {
        Task<RenderNodeResult> ExecuteAsync(RenderNodeExecutionContext context);
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

    /// <summary>
    /// 渲染节点执行结果
    /// </summary>
    public class RenderNodeResult
    {
        public bool Success { get; set; }
        public string? Code { get; set; }
        public string? Message { get; set; }
        public string? NodeId { get; set; }
        public string? NodeName { get; set; }

        public string Content { get; set; } = "";

        public static RenderNodeResult SuccessResult(string content)
            => new() { Success = true, Content = content };

        public static RenderNodeResult Failure(string code, string message,
            string? nodeId = null, string? nodeName = null)
            => new() { Success = false, Code = code, Message = message, NodeId = nodeId, NodeName = nodeName };
    }
}
