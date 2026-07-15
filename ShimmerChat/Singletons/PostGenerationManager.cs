using Microsoft.Extensions.Logging;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 后生成管线管理器。反序列化 Agent 的 PostGenerationTreeJson 并执行节点树。
    /// </summary>
    public class PostGenerationManager : IPostGenerationManager
    {
        private readonly ITreeNodeSerializer _serializer;
        private readonly ILogger<PostGenerationManager> _logger;

        public PostGenerationManager(PostGenerationNodeSerializer serializer, ILogger<PostGenerationManager> logger)
        {
            _serializer = serializer;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(Agent agent, string responseText,
            IReadOnlyList<ContextSegment> preFragments,
            PersistentEnv persistentEnv, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(agent.PostGenerationTreeJson))
                return responseText;

            var root = _serializer.Deserialize(agent.PostGenerationTreeJson);
            if (root is not IPostGenerationNode postRoot)
                return responseText;

            var env = new PostGenerationEnv(responseText, preFragments, persistentEnv)
            {
                Serializer = _serializer
            };
            var context = new PostNodeExecutionContext(env, ct);

            try
            {
                var result = await postRoot.ExecuteAsync(context);
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
    }
}
