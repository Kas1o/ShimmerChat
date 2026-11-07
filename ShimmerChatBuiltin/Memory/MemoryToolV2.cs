using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Memory
{
    /// <summary>
    /// 记忆工具：新增 / 删除 / 搜索。文本写入真值库，向量写穿到当前模型索引。
    /// </summary>
    public class MemoryToolV2 : IAutoCreateToolV2
    {
        private readonly PersistentEnv _env;

        public static string NameKey => "tool.memory";
        public static string DescriptionKey => "tool.memory.desc";
        public static string[] CategoryKeys => ["category.memory"];

        public MemoryToolV2()
        {
            _env = null!;
        }

        private MemoryToolV2(PersistentEnv env)
        {
            _env = env;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) => new MemoryToolV2(env);

        public Tool GetDefinition() => new()
        {
            name = "MemoryTool",
            description = "A tool for managing memory in conversations. Supports add, delete, and search with similarity or keyword mode.",
            parameters =
            [
                (new ToolParameter { name = "action", type = ParameterType.String, description = "Action: add, delete, or search.", @enum = ["add", "delete", "search"] }, true),
                (new ToolParameter { name = "content", type = ParameterType.String, description = "Content to add or search for." }, false),
                (new ToolParameter { name = "id", type = ParameterType.String, description = "The ID of the memory to delete." }, false),
                (new ToolParameter { name = "search_mode", type = ParameterType.String, description = "Search mode: similarity or keyword.", @enum = ["similarity", "keyword"] }, false)
            ]
        };

        public async Task<string> ExecuteAsync(string input)
        {
            if (_env is null)
                throw new InvalidOperationException("MemoryToolV2 必须通过 Create(PersistentEnv) 创建。");

            var action = JsonConvert.DeserializeObject<InputAction>(input);
            var vectorStore = _env.VectorStore
                ?? throw new InvalidOperationException("当前宿主没有提供向量存储。");

            var (config, configError) = MemoryModelConfig.Load(_env.KVData);
            if (config is null)
                throw new InvalidDataException(configError ?? "嵌入模型未配置。");

            var agentId = MemoryPartition.ForAgent(_env.AgentGuid);
            var service = new MemoryIndexService(MemoryStoreProvider.Current, vectorStore, config);

            return action.action switch
            {
                "add" => await HandleAdd(service, agentId, action),
                "delete" => await HandleDelete(service, agentId, action),
                "search" => await HandleSearch(service, agentId, action),
                _ => "Unknown action."
            };
        }

        private async Task<string> HandleAdd(MemoryIndexService service, string agentId, InputAction action)
        {
            if (string.IsNullOrWhiteSpace(action.content))
                return "[Tool error] Content cannot be empty when adding memory.";

            try
            {
                var record = await service.AddAsync(agentId, action.content, source: _env.ChatGuid.ToString());
                return $"Memory added: {record.Id}";
            }
            catch (MemoryVectorSyncException ex)
            {
                return $"[Tool error] {ex.Message}";
            }
        }

        private async Task<string> HandleDelete(MemoryIndexService service, string agentId, InputAction action)
        {
            if (string.IsNullOrWhiteSpace(action.id))
                return "[Tool error] ID cannot be empty when deleting memory.";

            var record = MemoryStoreProvider.Current.Get(action.id);
            if (record is null || !string.Equals(record.AgentId, agentId, StringComparison.Ordinal))
                return $"Memory with ID {action.id} not found.";

            await service.DeleteAsync(action.id);
            return $"Memory with ID {action.id} deleted.";
        }

        private async Task<string> HandleSearch(MemoryIndexService service, string agentId, InputAction action)
        {
            if (string.IsNullOrWhiteSpace(action.content))
                return "[Tool error] Content cannot be empty when searching memory.";

            if (string.Equals(action.search_mode, "keyword", StringComparison.OrdinalIgnoreCase))
            {
                var keywordHits = service.SearchKeyword(agentId, action.content, 5);
                if (keywordHits.Count == 0) return "Search Results: (empty)";

                return "Search Results:\n" + string.Join("\n", keywordHits.Select(x => $"ID: {x.Id}, Content: {x.Content}"));
            }

            try
            {
                var hits = await service.SearchAsync(agentId, [action.content], 5, 0f);
                if (hits.Count == 0) return "Search Results: (empty)";

                return "Search Results:\n" + string.Join("\n",
                    hits.Select(x => $"ID: {x.Id}, Score: {x.Score:F4}, Content: {x.Content}"));
            }
            catch (MemoryNotReadyException ex)
            {
                return $"[Tool error] Memory index is not ready ({ex.Status}). Build or verify it in Settings first.";
            }
        }

        private struct InputAction
        {
            public string action { get; set; }
            public string? content { get; set; }
            public string? id { get; set; }
            public string search_mode { get; set; }
        }
    }
}
