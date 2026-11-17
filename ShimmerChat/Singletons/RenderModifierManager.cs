using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ShimmerChatBuiltin.Misc.Node.Render;
using ShimmerChatBuiltin.NodeBasic.Render;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class RenderModifierManager : IRenderModifierManager
    {
        private readonly ITreeNodeSerializer _serializer;
        private readonly IKVDataService _kvData;
        private readonly ILocService _loc;
        private readonly ILogger<RenderModifierManager> _logger;

        private RenderModifierNodeWrapper? _globalRoot;
        private readonly object _globalLock = new();

        public RenderModifierManager(RenderModifierNodeSerializer serializer, IKVDataService kvData,
            ILocService loc, ILogger<RenderModifierManager> logger)
        {
            _serializer = serializer;
            _kvData = kvData;
            _loc = loc;
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

            RenderModifierNodeWrapper root = ResolveRoot(agent, chat)
                ?? CreateFallbackRoot();

            var env = new RenderEnv(content, _serializer, _kvData, chat, agent, _loc);
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
                _globalRoot = string.IsNullOrEmpty(json) ? null : DeserializeRoot(json, "global");
            }
        }

        private RenderModifierNodeWrapper? DeserializeRoot(string json, string source)
        {
            var result = _serializer.DeserializeTree(json);
            if (result.Root is RenderModifierNodeWrapper wrapper)
                return wrapper;

            if (!result.Success)
            {
                // 回退必须可见：留下日志，调用方会使用默认渲染树。
                _logger.LogError("[RenderModifier] {Source} 渲染节点树反序列化失败，已回退: {Error}",
                    source, result.Error);
            }

            return null;
        }

        private RenderModifierNodeWrapper? ResolveRoot(Agent? agent, Chat? chat = null)
        {
            // 生成提供器渲染树覆盖：优先使用触发时盖印在 Chat 上的覆盖树
            if (!string.IsNullOrEmpty(chat?.SourceRenderModifierTreeJson))
                return DeserializeRoot(chat.SourceRenderModifierTreeJson, "chat override");

            if (agent != null && !string.IsNullOrEmpty(agent.RenderModifierTreeJson))
                return DeserializeRoot(agent.RenderModifierTreeJson, "agent");

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
                RootNodeJson = _serializer.Serialize(
                    new RenderModifierNodeWrapper(new RenderSequenceNode
                    {
                        Children =
                        [
                            new RenderModifierNodeWrapper(new MarkdownRenderNode(), "Markdown Render")
                        ]
                    }, "Default"))
            });

            _kvData.Write("RenderModifierManager", "render_modifier_presets",
                JsonConvert.SerializeObject(presets, Formatting.Indented));
        }

        private static RenderModifierNodeWrapper CreateFallbackRoot()
        {
            var payload = new RenderSequenceNode();
            payload.Children.Add(new RenderModifierNodeWrapper(new RenderCallNode { PresetId = "__default__" }));
            return new RenderModifierNodeWrapper(payload, "Default");
        }
    }
}
