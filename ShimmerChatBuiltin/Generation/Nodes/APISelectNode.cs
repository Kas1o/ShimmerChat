using SharperLLM.API;
using Newtonsoft.Json;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 设置 TransientEnv.API。
    /// APIIndex: -1 表示使用全局选中 API；>=0 表示使用指定索引的 API 配置。
    /// </summary>
    [NodeInfo("API Select", Icon = "⚡", Color = "#e07070", Category = "Config", Description = "Choose which API configuration to use for generation")]
    [NodeEditor("ShimmerChat.Components.SubComponents.APISelectNodeEditor")]
    public class APISelectNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Select API";

        /// <summary>
        /// API 配置索引。-1 表示全局选中。
        /// </summary>
        public int APIIndex { get; set; } = -1;

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var kvData = context.Env.Persistent.KVData;
            var json = kvData.Read("ApiSettings", "apiSetting") ?? "[]";
            var settings = JsonConvert.DeserializeObject<List<ApiSetting>>(json) ?? [];

            if (settings.Count == 0)
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ApiUnavailable,
                    "APISelect: No API settings configured.",
                    nodeId: Id, nodeName: Name));

            int index = APIIndex;
            if (index == -1)
            {
                var globalIndexStr = kvData.Read("ApiSettings", "selectedAPIIndex") ?? "0";
                int.TryParse(globalIndexStr, out index);
            }

            if (index >= 0 && index < settings.Count)
                context.Env.Transient.API = settings[index].LLMApi;
            else
                context.Env.Transient.API = settings[0].LLMApi;

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
