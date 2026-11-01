using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
            IPostGenerationNode? root;
            var postTreeJson = overrides?.PostGenerationTreeJson ?? agent.PostGenerationTreeJson;
            if (!string.IsNullOrEmpty(postTreeJson))
            {
                root = _serializer.Deserialize(postTreeJson) as IPostGenerationNode
                    ?? CreateFallbackRoot();
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
                        new PostDebugOutputNode { Name = "Debug Output" },
                        new AutoChatNameNode { Name = "Auto Chat Name" },
                        new PostToolCallNode { Name = "Tool Calls" }
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
