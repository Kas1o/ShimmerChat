using ShimmerChatLib;

namespace ShimmerChatBuiltin.SubAgent.Sessions
{
    /// <summary>
    /// 子智能体会话的执行状态。
    /// </summary>
    public enum SubAgentSessionStatus
    {
        /// <summary>会话正在执行中</summary>
        Running,
        /// <summary>上一次调用正常结束</summary>
        Completed,
        /// <summary>上一次调用失败，错误码/错误信息记录在会话上</summary>
        Failed
    }

    /// <summary>
    /// 一次 <c>subagent_call</c> 的持久会话。
    /// <para>
    /// 会话是内置插件的<strong>私有存储</strong>（KVData 空间 <c>SubAgent</c>），
    /// 不写入 <c>Chats</c> / 消息存储，因此不会出现在对话列表里，也不占用
    /// <see cref="Chat"/> 的存储路径；查看与删除由 SubAgent 会话面板负责。
    /// </para>
    /// </summary>
    public class SubAgentSession
    {
        /// <summary>会话 Id，即工具返回给调用方的 <c>session_id</c>。</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>所属的 SubAgent 配置 Guid。</summary>
        public Guid ConfigGuid { get; set; }

        /// <summary>所属的 SubAgent 配置名称（配置改名后保留当时的名称，便于溯源）。</summary>
        public string ConfigName { get; set; } = "";

        /// <summary>会话标题。</summary>
        public string Title { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public SubAgentSessionStatus Status { get; set; } = SubAgentSessionStatus.Running;

        /// <summary>失败时的错误码，与工具返回信封里的 <c>code</c> 一致。</summary>
        public string? ErrorCode { get; set; }

        /// <summary>失败时的错误信息。</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>最近一次调用的 OutputMode 输出文本。</summary>
        public string? OutputText { get; set; }

        /// <summary>创建会话时的父对话 Guid，仅用于溯源，与 SharedChatGuid 开关无关。</summary>
        public Guid? ParentChatGuid { get; set; }

        /// <summary>创建会话时的父 Agent Guid，仅用于溯源，与 SharedAgentGuid 开关无关。</summary>
        public Guid? ParentAgentGuid { get; set; }

        /// <summary>会话消息。复用 <see cref="Message"/>，因此带 sender / timestamp / 版本信息。</summary>
        public List<Message> Messages { get; set; } = [];

        /// <summary>生成索引条目（列表页只反序列化索引，不加载整段消息）。</summary>
        public SubAgentSessionSummary ToSummary()
        {
            return new SubAgentSessionSummary
            {
                Id = Id,
                ConfigGuid = ConfigGuid,
                ConfigName = ConfigName,
                Title = Title,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                MessageCount = Messages.Count,
                Status = Status,
                LastPreview = BuildPreview(Messages)
            };
        }

        /// <summary>
        /// 由调用方给的标题或任务文本派生会话标题。空标题时取任务首行前 60 字。
        /// </summary>
        public static string BuildTitle(string? requestedTitle, string task, int maxLength = 60)
        {
            if (!string.IsNullOrWhiteSpace(requestedTitle))
                return requestedTitle.Trim();

            if (string.IsNullOrWhiteSpace(task))
                return "Session";

            var firstLine = task.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? "Session";

            return Truncate(firstLine, maxLength);
        }

        private static string BuildPreview(List<Message> messages)
        {
            for (var i = messages.Count - 1; i >= 0; i--)
            {
                var content = messages[i].CurrentVersion?.Content;
                if (!string.IsNullOrWhiteSpace(content))
                    return Truncate(content.Replace('\n', ' ').Trim(), 120);
            }
            return "";
        }

        private static string Truncate(string text, int maxLength)
            => text.Length <= maxLength ? text : text[..maxLength] + "…";
    }

    /// <summary>
    /// 会话索引条目。存于 KVData 键 <c>sessions</c>，避免列表页反序列化全部会话消息。
    /// </summary>
    public class SubAgentSessionSummary
    {
        public Guid Id { get; set; }
        public Guid ConfigGuid { get; set; }
        public string ConfigName { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int MessageCount { get; set; }
        public SubAgentSessionStatus Status { get; set; }
        public string? LastPreview { get; set; }
    }
}
