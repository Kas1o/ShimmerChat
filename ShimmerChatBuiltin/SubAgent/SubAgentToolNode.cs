using Newtonsoft.Json;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    /// <summary>
    /// 将指定的 SubAgent 配置注册到共享的 SubAgentToolV2 实例中。
    /// 多个 SubAgentToolNode 共享同一个工具，每个添加一个 SubAgent。
    /// </summary>
    [NodeInfo("node.sub_agent_tool", Icon = "🔧", Color = "var(--node-subagent)", CategoryKeys = ["category.tool", "category.sub_agent"])]
    [NodeEditor(typeof(SubAgentToolNodeEditor))]
    public class SubAgentToolNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "SubAgent Tool";

        [NodeProperty("prop.sub_agent_tool.config_name", HintKey = "prop.sub_agent_tool.config_name.hint")]
        public string ConfigName { get; set; } = "";

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;

            if (string.IsNullOrWhiteSpace(ConfigName))
                return Task.FromResult(NodeResult.SuccessResult());

            var kvData = context.Env.Persistent.KVData;

            var config = LoadConfig(kvData, ConfigName);
            if (config == null)
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ConfigNotFound,
                    loc.Format("node_err.subagent_tool_config_not_found", ConfigName),
                    nodeId: Id, nodeName: Name));

            // 查找或创建 SubAgentToolV2 单例
            var tool = context.Env.Transient.Tools.OfType<SubAgentToolV2>().FirstOrDefault();
            if (tool == null)
            {
                var p = context.Env.Persistent;
                tool = new SubAgentToolV2(
                    kvData,
                    p.ToolRegistry, p.ChatGuid, p.AgentGuid,
                    p.Serializer, p.LocService, p.DebugOutput,
                    p.PostGenerationManager);
                context.Env.Transient.Tools.Add(tool);
            }

            tool.AddSubAgent(ConfigName, config);
            return Task.FromResult(NodeResult.SuccessResult());
        }

        private static SubAgentConfig? LoadConfig(IKVDataService kvData, string name)
        {
            var json = kvData.Read("SubAgent", "configs");
            var configs = JsonConvert.DeserializeObject<List<SubAgentConfig>>(json ?? "[]") ?? [];
            return configs.FirstOrDefault(c => c.Name == name);
        }
    }
}
