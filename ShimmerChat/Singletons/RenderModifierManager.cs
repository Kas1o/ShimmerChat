using Microsoft.Extensions.Logging;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class RenderModifierManager : IRenderModifierManager
    {
        private readonly ITreeNodeSerializer _serializer;
        private readonly IKVDataService _kvData;
        private readonly ILogger<RenderModifierManager> _logger;

        private IRenderModifierNode? _globalRoot;
        private readonly object _globalLock = new();

        public RenderModifierManager(RenderModifierNodeSerializer serializer, IKVDataService kvData,
            ILogger<RenderModifierManager> logger)
        {
            _serializer = serializer;
            _kvData = kvData;
            _logger = logger;
        }

        public string Render(Agent? agent, string content, Chat? chat = null)
        {
            var (result, _) = RenderWithLog(agent, content, chat);
            return result.Content;
        }

        public (RenderNodeResult Result, List<RenderChangeRecord> ChangeLog) RenderWithLog(
            Agent? agent, string content, Chat? chat = null)
        {
            if (string.IsNullOrEmpty(content))
                return (RenderNodeResult.SuccessResult(content), new List<RenderChangeRecord>());

            IRenderModifierNode? root = ResolveRoot(agent);

            if (root == null)
                return (RenderNodeResult.SuccessResult(content), new List<RenderChangeRecord>());

            var env = new RenderEnv(content, _serializer, _kvData, chat, agent);
            var context = new RenderNodeExecutionContext(env);

            try
            {
                var task = root.ExecuteAsync(context);
                task.Wait();
                return (task.Result, env.ChangeLog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RenderModifier] Pipeline execution error");
                return (RenderNodeResult.Failure(NodeErrorCodes.ServiceError, ex.Message),
                    env.ChangeLog);
            }
        }

        public void SetGlobalTreeJson(string? json)
        {
            lock (_globalLock)
            {
                _globalRoot = string.IsNullOrEmpty(json)
                    ? null
                    : _serializer.Deserialize(json) as IRenderModifierNode;
            }
        }

        private IRenderModifierNode? ResolveRoot(Agent? agent)
        {
            if (agent != null && !string.IsNullOrEmpty(agent.RenderModifierTreeJson))
                return _serializer.Deserialize(agent.RenderModifierTreeJson) as IRenderModifierNode;

            lock (_globalLock) return _globalRoot;
        }
    }
}
