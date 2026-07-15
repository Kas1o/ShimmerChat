using SharperLLM.API;
using Newtonsoft.Json;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 设置 TransientEnv.API。
    /// APIIndex: -1 表示使用全局选中 API；>=0 表示使用指定索引的 API 配置。
    /// 同时处理续写（IsContinuation）标记。
    /// </summary>
    [NodeInfo("node.api_select", Icon = "⚡", Color = "var(--node-config)", CategoryKeys = ["category.config"], DescriptionKey = "node.api_select.desc")]
    [NodeEditor(typeof(APISelectNodeEditor))]
    public class APISelectNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Select API";

        /// <summary>
        /// API 配置索引。-1 表示全局选中。
        /// </summary>
        public int APIIndex { get; set; } = -1;

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;
            var kvData = context.Env.Persistent.KVData;
            var json = kvData.Read("ApiSettings", "apiSetting") ?? "[]";
            var settings = JsonConvert.DeserializeObject<List<ApiConfig>>(json) ?? [];

            if (settings.Count == 0)
                return NodeResult.Failure(
                    NodeErrorCodes.ApiUnavailable,
                    loc["node_err.api_no_settings"],
                    nodeId: Id, nodeName: Name);

            int index = APIIndex;
            if (index == -1)
            {
                var globalIndexStr = kvData.Read("ApiSettings", "selectedAPIIndex") ?? "0";
                int.TryParse(globalIndexStr, out index);
            }

            var selectedConfig = index >= 0 && index < settings.Count
                ? settings[index]
                : settings[0];

            context.Env.Transient.API = selectedConfig.ToAPISetting();

            // 续写处理：检查 SharedState 中的 IsContinuation 标记
            if (context.Env.Transient.SharedState.TryGetValue("IsContinuation", out var isCont)
                && isCont is true)
            {
                if (selectedConfig.Type == ApiConfigType.OpenAI
                    || selectedConfig.Type == ApiConfigType.DeepSeek)
                {
                    var messages = context.Env.Transient.SharedState["ChatMessages"] as List<Message>;
                    var lastAi = messages?.LastOrDefault(m => m.sender == Sender.AI);
                    if (lastAi != null)
                    {
                        lastAi.message.CustomProperties ??= new Dictionary<string, object>();
                        lastAi.message.CustomProperties["prefix"] = true;
                    }
                }
                else
                {
                    return NodeResult.Failure(
                        NodeErrorCodes.ApiUnavailable,
                        loc.Format("node_err.api_no_continuation", selectedConfig.Type),
                        nodeId: Id, nodeName: Name);
                }
            }

            return NodeResult.SuccessResult();
        }
    }
}
