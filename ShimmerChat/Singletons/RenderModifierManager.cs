using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
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
        private string? _globalRootJson;
        private readonly object _globalLock = new();

        /// <summary>
        /// 已反序列化的根树缓存。按 JSON 内容索引，避免每次消息渲染都重建整棵树；
        /// 有上限且按先进先出淘汰，不会因为一条新树清空整个缓存。
        /// </summary>
        private readonly ConcurrentDictionary<string, RenderModifierNodeWrapper> _deserializedRoots =
            new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _deserializedRootOrder = new();
        private const int MaxDeserializedRoots = 64;

        /// <summary>
        /// 根树 JSON 实例 → 修订号。使用引用相等：Agent / Chat 上盖印的树字符串在
        /// 未被编辑时是同一个实例，编辑后换成新实例，因此不需要哈希整棵 JSON。
        /// </summary>
        private readonly ConditionalWeakTable<string, RootRevision> _rootRevisions = new();
        private long _nextRootRevision;

        private sealed class RootRevision
        {
            public RootRevision(long value) => Value = value;
            public long Value { get; }
        }

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
            var (result, _) = RenderWithLog(agent, content, chat, allowCacheWrites: true,
                RenderPosition.Body);
            return result;
        }

        public (string Content, List<RenderChangeRecord> ChangeLog) RenderWithLog(
            Agent? agent, string content, Chat? chat = null)
            => RenderWithLog(agent, content, chat, allowCacheWrites: true);

        public (string Content, List<RenderChangeRecord> ChangeLog) RenderWithLog(
            Agent? agent, string content, Chat? chat, bool allowCacheWrites)
            => RenderWithLog(agent, content, chat, allowCacheWrites, RenderPosition.Body);

        public (string Content, List<RenderChangeRecord> ChangeLog) RenderWithLog(
            Agent? agent, string content, Chat? chat, bool allowCacheWrites, RenderPosition position)
        {
            if (string.IsNullOrEmpty(content))
                return (content, new List<RenderChangeRecord>());

            var (resolvedRoot, rootJson) = ResolveRoot(agent, chat);
            RenderModifierNodeWrapper root = resolvedRoot ?? CreateFallbackRoot();

            var revision = new RenderTreeRevision(
                GetRootRevision(rootJson),
                PipelinePresetStore.GetRevision(GenerationPipeline.RenderModifier));

            var env = new RenderEnv(content, _serializer, _kvData, chat, agent, _loc)
            {
                AllowCacheWrites = allowCacheWrites,
                TreeRevision = revision
            };
            var context = new RenderNodeExecutionContext(env)
            {
                Position = position
            };

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
                _globalRootJson = json;
                _globalRoot = string.IsNullOrEmpty(json) ? null : DeserializeRoot(json, "global");
            }
        }

        /// <summary>
        /// 根树 JSON 对应的修订号。空 JSON（使用默认回退树）固定为 0；
        /// 其余按字符串实例分配递增编号，Agent / Chat 的树被替换时编号随之变化。
        /// </summary>
        private long GetRootRevision(string? json)
        {
            if (string.IsNullOrEmpty(json)) return 0;

            return _rootRevisions
                .GetValue(json, _ => new RootRevision(Interlocked.Increment(ref _nextRootRevision)))
                .Value;
        }

        private RenderModifierNodeWrapper? DeserializeRoot(string json, string source)
        {
            if (_deserializedRoots.TryGetValue(json, out var cached))
                return cached;

            var result = _serializer.DeserializeTree(json);
            if (result.Root is RenderModifierNodeWrapper wrapper)
            {
                CacheDeserializedRoot(json, wrapper);
                return wrapper;
            }

            if (!result.Success)
            {
                // 回退必须可见：留下日志，调用方会使用默认渲染树。
                _logger.LogError("[RenderModifier] {Source} 渲染节点树反序列化失败，已回退: {Error}",
                    source, result.Error);
            }

            return null;
        }

        private void CacheDeserializedRoot(string json, RenderModifierNodeWrapper root)
        {
            if (!_deserializedRoots.TryAdd(json, root)) return;

            _deserializedRootOrder.Enqueue(json);
            while (_deserializedRoots.Count > MaxDeserializedRoots
                && _deserializedRootOrder.TryDequeue(out var oldest))
            {
                _deserializedRoots.TryRemove(oldest, out _);
            }
        }

        private (RenderModifierNodeWrapper? Root, string? Json) ResolveRoot(Agent? agent, Chat? chat = null)
        {
            // 生成提供器渲染树覆盖：优先使用触发时盖印在 Chat 上的覆盖树
            if (!string.IsNullOrEmpty(chat?.SourceRenderModifierTreeJson))
            {
                var json = chat.SourceRenderModifierTreeJson;
                return (DeserializeRoot(json, "chat override"), json);
            }

            if (agent != null && !string.IsNullOrEmpty(agent.RenderModifierTreeJson))
            {
                var json = agent.RenderModifierTreeJson;
                return (DeserializeRoot(json, "agent"), json);
            }

            lock (_globalLock) return (_globalRoot, _globalRootJson);
        }

        private void EnsureDefaultRenderPreset()
        {
            // 读取与写回都走 PipelinePresetStore（存储位置的唯一事实来源）。
            var presets = PipelinePresetStore.ReadAll<RenderModifierPreset>(_kvData, GenerationPipeline.RenderModifier);

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
                            // 仅正文做 Markdown 渲染：思维链等位置原样通过，
                            // 需要时用户可以在树里给其他位置追加自己的节点。
                            new RenderModifierNodeWrapper(new RenderIfNode
                            {
                                Position = RenderPosition.Body,
                                Then = new RenderModifierNodeWrapper(
                                    new MarkdownRenderNode(), "Markdown Render")
                            }, "Only Body")
                        ]
                    }, "Default"))
            });

            PipelinePresetStore.WriteAll(_kvData, GenerationPipeline.RenderModifier, presets);
        }

        private static RenderModifierNodeWrapper CreateFallbackRoot()
        {
            var payload = new RenderSequenceNode();
            payload.Children.Add(new RenderModifierNodeWrapper(new RenderCallNode { PresetId = "__default__" }));
            return new RenderModifierNodeWrapper(payload, "Default");
        }
    }
}
