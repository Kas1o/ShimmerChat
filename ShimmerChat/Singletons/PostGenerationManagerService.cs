using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.Generation.Nodes;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 后生成管线管理器。反序列化 Agent 的 PostGenerationTreeJson 并执行节点树。
    /// </summary>
    public class PostGenerationManagerService : IPostGenerationManagerService
    {
        private readonly ITreeNodeSerializer _serializer;
        private readonly IKVDataService _kvData;
        private readonly ILogger<PostGenerationManagerService> _logger;

        public PostGenerationManagerService(PostGenerationNodeSerializerService serializer,
            IKVDataService kvData, ILogger<PostGenerationManagerService> logger)
        {
            _serializer = serializer;
            _kvData = kvData;
            _logger = logger;

            EnsureDefaultPostPreset();
        }

        public async Task<string> ExecuteAsync(Agent agent, string responseText,
            IReadOnlyList<ContextSegment> preFragments,
            PersistentEnv persistentEnv, CancellationToken ct = default)
        {
            IPostGenerationNode? root;
            if (!string.IsNullOrEmpty(agent.PostGenerationTreeJson))
            {
                root = _serializer.Deserialize(agent.PostGenerationTreeJson) as IPostGenerationNode
                    ?? CreateFallbackRoot();
            }
            else
            {
                root = CreateFallbackRoot();
            }

            var env = new PostGenerationEnv(responseText, preFragments, persistentEnv)
            {
                Serializer = _serializer
            };
            var context = new PostNodeExecutionContext(env, ct);

            try
            {
                var result = await root.ExecuteAsync(context);
                if (!result.Success)
                {
                    _logger.LogWarning("[PostGeneration] 节点执行失败: {Code} {Message} (Node: {NodeName})",
                        result.Code, result.Message, result.NodeName);
                }
                return env.ResponseText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PostGeneration] 执行异常");
                return responseText;
            }
        }

        private void EnsureDefaultPostPreset()
        {
            var json = _kvData.Read("PostGenerationManager", "post_generation_presets");
            var presets = string.IsNullOrEmpty(json)
                ? new List<PostGenerationPreset>()
                : (JsonConvert.DeserializeObject<List<PostGenerationPreset>>(json) ?? new List<PostGenerationPreset>());

            presets.RemoveAll(p => p.Id == "__default__");

            presets.Add(new PostGenerationPreset
            {
                Id = "__default__",
                Name = "Default",
                RootNodeJson = _serializer.Serialize(new PostSequenceNode
                {
                    Name = "Default",
                    Children = new List<IPostGenerationNode>
                    {
                        new PostDebugOutputNode { Name = "Debug Output" }
                    }
                })
            });

            _kvData.Write("PostGenerationManager", "post_generation_presets",
                JsonConvert.SerializeObject(presets, Formatting.Indented));
        }

        private static IPostGenerationNode CreateFallbackRoot()
        {
            var root = new PostSequenceNode { Name = "Default" };
            root.Children.Add(new PostCallNode { PresetId = "__default__" });
            return root;
        }
    }
}
