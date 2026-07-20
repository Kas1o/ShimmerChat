using System;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// Agent 数据自动迁移服务接口。
    /// </summary>
    public interface IAgentMigrationService
    {
        /// <summary>
        /// 迁移所有未迁移的 Agent
        /// </summary>
        int MigrateAll();

        /// <summary>
        /// 迁移单个 Agent（如果尚未迁移）
        /// </summary>
        bool MigrateAgent(Guid agentGuid);
    }
}
