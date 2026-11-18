using Microsoft.Extensions.Logging;
using SharperLLM.Util;
using ShimmerChatBuiltin.Misc.Node.PostGeneration;
using ShimmerChatBuiltin.NodeBasic.PostGeneration;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 后生成管线管理器。反序列化 Agent 的 PostGenerationTreeJson 并执行节点树。
    /// 节点直接操作对话与消息存储，这里只负责组装环境与汇总结果。
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

        public async Task<PostPipelineResult> ExecuteAsync(
            Agent agent, ChatMessage responseMessage,
            IReadOnlyList<ContextSegment> preFragments,
            PersistentEnv persistentEnv,
            PostPipelineContext? roundContext = null,
            CancellationToken ct = default,
            PipelineTreeOverrides? overrides = null)
        {
            PostGenerationNodeWrapper? root;
            var postTreeJson = overrides?.PostGenerationTreeJson ?? agent.PostGenerationTreeJson;
            if (!string.IsNullOrEmpty(postTreeJson))
            {
                var loaded = _serializer.DeserializeTree(postTreeJson);
                if (loaded.Root is PostGenerationNodeWrapper wrapper)
                {
                    root = wrapper;
                }
                else if (loaded.Success)
                {
                    // 空树（"{}"）不是损坏数据，按未配置处理。
                    root = CreateFallbackRoot();
                }
                else
                {
                    // 回退必须可见：日志记录失败原因，不悄悄换成默认树。
                    _logger.LogError("[PostGeneration] 后生成节点树反序列化失败，已回退到默认树: {Error}",
                        loaded.Error);
                    root = CreateFallbackRoot();
                }
            }
            else
            {
                root = CreateFallbackRoot();
            }

            var env = new PostGenerationEnv(responseMessage, preFragments, persistentEnv,
                roundContext?.Tools, roundContext?.CurrentMessage,
                roundContext?.RoundIndex ?? 0, roundContext?.SharedState)
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
                    return Fail(responseMessage, env, result.Code ?? NodeErrorCodes.CommonError, result.Message);
                }

                // ResponseText 是本轮输出的权威来源；SetResponse 会同时替换 ResponseMessage
                env.ResponseMessage.Content = env.ResponseText;
                return new PostPipelineResult
                {
                    Success = true,
                    ResponseMessage = env.ResponseMessage,
                    LoopDirective = env.LoopDirective,
                    ToolCallsHandled = env.ToolCallsHandled,
                    ChatMutated = env.ChatMutated
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PostGeneration] 执行异常");
                return Fail(responseMessage, env, NodeErrorCodes.CommonError, ex.Message);
            }
        }

        /// <summary>
        /// 失败时回退到原始响应并强制结束本轮——带着未处理完的状态继续循环风险更大。
        /// </summary>
        private static PostPipelineResult Fail(ChatMessage responseMessage, PostGenerationEnv env,
            string code, string? message) => new()
            {
                Success = false,
                Code = code,
                Message = message,
                ResponseMessage = responseMessage,
                LoopDirective = PostLoopDirective.Stop,
                ChatMutated = env.ChatMutated
            };

        private void EnsureDefaultPostPreset()
        {
            // 读取与写回都走 PipelinePresetStore（存储位置的唯一事实来源）。
            var presets = PipelinePresetStore.ReadAll<PostGenerationPreset>(_kvData, GenerationPipeline.PostGeneration);

            presets.RemoveAll(p => p.Id == "__default__");

            presets.Add(new PostGenerationPreset
            {
                Id = "__default__",
                Name = "Default",
                RootNodeJson = _serializer.Serialize(new PostGenerationNodeWrapper(new PostSequenceNode
                {
                    Children =
                    [
                        new PostGenerationNodeWrapper(new PostDebugOutputNode(), "Debug Output"),
                        new PostGenerationNodeWrapper(new AutoChatNameNode(), "Auto Chat Name"),
                        new PostGenerationNodeWrapper(new PostToolCallNode(), "Tool Calls")
                    ]
                }, "Default"))
            });

            PipelinePresetStore.WriteAll(_kvData, GenerationPipeline.PostGeneration, presets);
        }

        private static PostGenerationNodeWrapper CreateFallbackRoot()
        {
            var payload = new PostSequenceNode();
            payload.Children.Add(new PostGenerationNodeWrapper(new PostCallNode { PresetId = "__default__" }));
            return new PostGenerationNodeWrapper(payload, "Default");
        }
    }
}
