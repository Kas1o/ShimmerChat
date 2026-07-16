using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.Generation.Nodes;

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

            EnsureDefaultRenderPreset();
        }

        public string Render(Agent? agent, string content, Chat? chat = null)
        {
            var (result, _) = RenderWithLog(agent, content, chat);
            return result;
        }

        public (string Content, List<RenderChangeRecord> ChangeLog) RenderWithLog(
            Agent? agent, string content, Chat? chat = null)
        {
            if (string.IsNullOrEmpty(content))
                return (content, new List<RenderChangeRecord>());

            IRenderModifierNode? root = ResolveRoot(agent)
                ?? CreateFallbackRoot();

            var env = new RenderEnv(content, _serializer, _kvData, chat, agent);
            var context = new RenderNodeExecutionContext(env);

            try
            {
                root.Execute(context);
                return (env.GetContent(), env.ChangeLog);
            }
            catch (RenderNodeException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RenderModifier] Pipeline execution error");
                throw new RenderNodeException(NodeErrorCodes.ServiceError, ex.Message);
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

        private void EnsureDefaultRenderPreset()
        {
            var json = _kvData.Read("RenderModifierManager", "render_modifier_presets");
            var presets = string.IsNullOrEmpty(json)
                ? new List<RenderModifierPreset>()
                : (JsonConvert.DeserializeObject<List<RenderModifierPreset>>(json) ?? new List<RenderModifierPreset>());

            presets.RemoveAll(p => p.Id == "__default__");

            presets.Add(new RenderModifierPreset
            {
                Id = "__default__",
                Name = "Default",
                RootNodeJson = _serializer.Serialize(new MarkdownRenderNode { Name = "Markdown Render" })
            });

            _kvData.Write("RenderModifierManager", "render_modifier_presets",
                JsonConvert.SerializeObject(presets, Formatting.Indented));
        }

        private static IRenderModifierNode CreateFallbackRoot()
        {
            var root = new RenderSequenceNode { Name = "Default" };
            root.Children.Add(new RenderCallNode { PresetId = "__default__" });
            return root;
        }
    }
}
