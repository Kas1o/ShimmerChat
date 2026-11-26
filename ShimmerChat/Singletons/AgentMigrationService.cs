using Microsoft.Extensions.Logging;
using ShimmerChatBuiltin.Fragments;
using ShimmerChatBuiltin.NodeBasic.PreGeneration;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// Agent 数据自动迁移服务。
    /// 遍历 Agent，将旧的 Description 转换为 ModifierTreeJson，
    /// 并把旧的 AvatarGuid / BackgroundGuid 迁移为 AvatarPath / BackgroundPath。
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
            var changed = false;

            // 已经有 ModifierTreeJson，不需要迁移管线
            if (string.IsNullOrEmpty(agent.PreGenerationTreeJson))
            {
                var payload = new SequenceNode
                {
                    Nodes = new List<PreGenerationNodeWrapper>()
                };


                // Description → FragmentNode (system)
#pragma warning disable CS0618 // 类型或成员已过时

                if (!string.IsNullOrWhiteSpace(agent.Description))
                {
                    payload.Nodes.Add(new PreGenerationNodeWrapper(new FragmentNode
                    {
                        Content = agent.Description,
                        From = SharperLLM.Util.PromptBuilder.From.system
                    }, "System Prompt"));
                }
#pragma warning restore CS0618 // 类型或成员已过时


                payload.Nodes.Add(new PreGenerationNodeWrapper(new CallNode
                {
                    PresetId = "__default__"
                }));

                var root = new PreGenerationNodeWrapper(payload, agent.Name);

                // ShimmerChat 2.0: CustomToolNames has been removed. Tools are now configured
                // via ToolManager presets and loaded by ToolPresetNode in the tree.
                // The migration only converts the Description.

                agent.PreGenerationTreeJson = _serializer.Serialize(root);
                changed = true;
            }

            changed |= MigrateLegacyImageReferences(agent);

            if (!changed)
                return false;

            agent.Save(_kvData);
            return true;
        }

        /// <summary>
        /// 清掉旧版 Guid 字段，让下一次读取只看到路径。
        /// Load 的 OnDeserialized 已经把 Guid 换算成 <c>{guid}.png</c>，这里只负责落盘新格式。
        /// </summary>
        private static bool MigrateLegacyImageReferences(Agent agent)
        {
#pragma warning disable CS0618 // 兼容旧数据
            if (agent.AvatarGuid == null && agent.BackgroundGuid == null)
                return false;

            if (string.IsNullOrWhiteSpace(agent.AvatarPath) && agent.AvatarGuid is Guid avatarGuid)
                agent.AvatarPath = $"{avatarGuid}.png";

            if (string.IsNullOrWhiteSpace(agent.BackgroundPath) && agent.BackgroundGuid is Guid backgroundGuid)
                agent.BackgroundPath = $"{backgroundGuid}.png";

            agent.AvatarGuid = null;
            agent.BackgroundGuid = null;
#pragma warning restore CS0618
            return true;
        }
    }
}
