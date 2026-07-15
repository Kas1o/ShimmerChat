using Microsoft.Extensions.Logging;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.Generation.Nodes;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// Agent 数据自动迁移服务。
    /// 遍历 Agent，将旧的 Description 转换为 ModifierTreeJson。
    /// CustomToolNames 已从 Agent 移除 (ShimmerChat 2.0)。
    /// </summary>
    public class AgentMigrationService : IAgentMigrationService
    {
        private readonly IKVDataService _kvData;
        private readonly IPreGenerationNodeSerializer _serializer;
        private readonly ILogger<AgentMigrationService> _logger;

        public AgentMigrationService(IKVDataService kvData, IPreGenerationNodeSerializer serializer, ILogger<AgentMigrationService> logger)
        {
            _kvData = kvData;
            _serializer = serializer;
            _logger = logger;
        }

        /// <summary>
        /// 迁移所有未迁移的 Agent
        /// </summary>
        public int MigrateAll()
        {
            var guids = Agent.GetAllAgentGuids(_kvData);
            int migrated = 0;
            foreach (var guid in guids)
            {
                try
                {
                    if (MigrateAgent(guid))
                        migrated++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AgentMigration] Failed to migrate agent {Guid}: {Message}", guid, ex.Message);
                }
            }
            return migrated;
        }

        /// <summary>
        /// 迁移单个 Agent（如果尚未迁移）
        /// </summary>
        public bool MigrateAgent(Guid agentGuid)
        {
            var agent = Agent.Load(agentGuid, _kvData);

            // 已经有 ModifierTreeJson，不需要迁移
            if (!string.IsNullOrEmpty(agent.PreGenerationTreeJson))
                return false;

            var root = new SequenceNode
            {
                Name = agent.Name,
                Nodes = new List<IPreGenerationNode>()
            };


            // Description → FragmentNode (system)
#pragma warning disable CS0618 // 类型或成员已过时

            if (!string.IsNullOrWhiteSpace(agent.Description))
            {
                root.Nodes.Add(new FragmentNode
                {
                    Name = "System Prompt",
                    Content = agent.Description,
                    From = SharperLLM.Util.PromptBuilder.From.system
                });
            }
#pragma warning restore CS0618 // 类型或成员已过时


            root.Nodes.Add(new CallNode
            {
                PresetId = "__default__"
            });

            // ShimmerChat 2.0: CustomToolNames has been removed. Tools are now configured
            // via ToolManager presets and loaded by ToolPresetNode in the tree.
            // The migration only converts the Description.

            agent.PreGenerationTreeJson = _serializer.Serialize(root);
            agent.Save(_kvData);
            return true;
        }
    }
}
