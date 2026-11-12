using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc.Node.PreGeneration
{
    /// <summary>
    /// 把 system / assistant / tool_result 消息中的图像内容块搬到相邻的伪 user 消息里，
    /// 供不接受这些角色直接携带图像（官方 Chat Completions 只允许 user 带 image_url）的
    /// OpenAI 兼容端点使用。
    /// <para>
    /// 拆分后的原消息与伪 user 消息通过 <see cref="GroupMetadataKey"/> 标记为同一组，
    /// 原消息保留文本、tool_calls、tool_call_id、thinking 与自定义参数，只移除 Images。
    /// 图像搬运顺序按协议约束决定：
    /// </para>
    /// <list type="bullet">
    /// <item>system：原消息在前、伪 user 在后，避免把 user 插到 system 之前破坏「system 在最前」。 </item>
    /// <item>tool_result：整段连续工具结果输出完之后再追加一条伪 user，绝不把 user 插进
    /// assistant(tool_calls) 与 tool 消息之间；同一段工具结果的图像合并到一条伪 user 消息。 </item>
    /// <item>assistant：伪 user 在前、原 assistant 在后，assistant 带 tool_calls 时也不会破坏配对。 </item>
    /// </list>
    /// <para>
    /// 节点是显式的兼容开关：目标端点本身支持特殊角色图像时不要加入此节点。
    /// 图像 Base64 缺失属于不可修复的数据错误，节点直接失败，不会把空图像块搬到另一个角色。
    /// </para>
    /// </summary>
    [NodeInfo("node.special_role_image_carrier", Icon = "🖼️", Color = "var(--node-fragment)", CategoryKeys = ["category.content", "category.fragment"], DescriptionKey = "node.special_role_image_carrier.desc")]
    public class SpecialRoleImageCarrierNode : IPreGenerationNode
    {
        /// <summary>原消息与伪 user 消息共享的分组标记（仅存在于本次生成的 Fragments 中）。</summary>
        public const string GroupMetadataKey = "image_carrier_group";

        /// <summary>伪 user 消息上的标记，值为 "synthetic"。</summary>
        public const string CarrierMetadataKey = "image_carrier";

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Special Role Image Carrier";

        [NodeProperty("prop.special_role_image_carrier.system", HintKey = "prop.special_role_image_carrier.system.hint", Order = 0)]
        public bool CarrySystemImages { get; set; } = true;

        [NodeProperty("prop.special_role_image_carrier.tool_result", HintKey = "prop.special_role_image_carrier.tool_result.hint", Order = 1)]
        public bool CarryToolResultImages { get; set; } = true;

        [NodeProperty("prop.special_role_image_carrier.assistant", HintKey = "prop.special_role_image_carrier.assistant.hint", Order = 2)]
        public bool CarryAssistantImages { get; set; } = true;

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var fragments = context.Env.Transient.Fragments;
            if (fragments.Count == 0)
                return Task.FromResult(NodeResult.SuccessResult());

            // 先整体校验再改写，避免失败时留下“一半已搬走、一半还在”的上下文
            var validationFailure = ValidateImages(fragments);
            if (validationFailure != null)
                return Task.FromResult(validationFailure);

            var rebuilt = new List<ContextSegment>(fragments.Count + 2);

            List<ChatImage>? pendingToolImages = null;
            List<string>? pendingToolCallIds = null;
            string? pendingToolGroupId = null;

            void FlushToolRun()
            {
                if (pendingToolImages is not { Count: > 0 })
                    return;

                var ids = string.Join(", ", pendingToolCallIds ?? []);
                var note = ids.Length > 0
                    ? $"Image attachment(s) returned by the preceding tool result(s) (tool_call_id: {ids}):"
                    : "Image attachment(s) returned by the preceding tool result(s):";

                rebuilt.Add(CreateCarrier(pendingToolImages, note, pendingToolGroupId!, PromptBuilder.From.tool_result));

                pendingToolImages = null;
                pendingToolCallIds = null;
                pendingToolGroupId = null;
            }

            foreach (var segment in fragments)
            {
                if (segment.From == PromptBuilder.From.tool_result && CarryToolResultImages)
                {
                    // 工具结果必须保持连续：带图的结果先移除图像，等这一段连续工具结果结束后
                    // 再统一追加伪 user，否则会把 user 插进 assistant(tool_calls) 与 tool 之间
                    if (segment.Message.Images is { Count: > 0 })
                    {
                        pendingToolGroupId ??= NewGroupId();
                        pendingToolImages ??= [];
                        pendingToolCallIds ??= [];

                        pendingToolImages.AddRange(CloneImages(segment.Message.Images));
                        if (!string.IsNullOrWhiteSpace(segment.Message.id))
                            pendingToolCallIds.Add(segment.Message.id!);

                        rebuilt.Add(WithGroup(
                            segment,
                            CloneWithoutImages(segment.Message),
                            pendingToolGroupId));
                    }
                    else
                    {
                        rebuilt.Add(segment);
                    }

                    continue;
                }

                // 任何非“待搬运工具结果”的片段都会结束当前工具结果段
                FlushToolRun();

                if (segment.From == PromptBuilder.From.system && CarrySystemImages
                    && segment.Message.Images is { Count: > 0 })
                {
                    var groupId = NewGroupId();
                    rebuilt.Add(WithGroup(segment, CloneWithoutImages(segment.Message), groupId));
                    rebuilt.Add(CreateCarrier(
                        CloneImages(segment.Message.Images),
                        "Image attachment(s) from the preceding system message:",
                        groupId,
                        PromptBuilder.From.system));
                    continue;
                }

                if (segment.From == PromptBuilder.From.assistant && CarryAssistantImages
                    && segment.Message.Images is { Count: > 0 })
                {
                    // 放在 assistant 之前：assistant 若带 tool_calls，user 不能插到它与 tool 之间
                    var groupId = NewGroupId();
                    rebuilt.Add(CreateCarrier(
                        CloneImages(segment.Message.Images),
                        "Image attachment(s) from the following assistant message:",
                        groupId,
                        PromptBuilder.From.assistant));
                    rebuilt.Add(WithGroup(segment, CloneWithoutImages(segment.Message), groupId));
                    continue;
                }

                rebuilt.Add(segment);
            }

            FlushToolRun();

            context.Env.Transient.Fragments.Clear();
            context.Env.Transient.Fragments.AddRange(rebuilt);

            return Task.FromResult(NodeResult.SuccessResult());
        }

        private NodeResult? ValidateImages(IReadOnlyList<ContextSegment> fragments)
        {
            for (int i = 0; i < fragments.Count; i++)
            {
                var segment = fragments[i];
                if (!ShouldCarry(segment.From) || segment.Message.Images is not { Count: > 0 })
                    continue;

                for (int imageIndex = 0; imageIndex < segment.Message.Images.Count; imageIndex++)
                {
                    var image = segment.Message.Images[imageIndex];
                    if (image is null || string.IsNullOrEmpty(image.Base64))
                    {
                        return NodeResult.Failure(
                            NodeErrorCodes.DataMissing,
                            $"SpecialRoleImageCarrier: cannot relocate image content from a {segment.From} message: image data is missing.",
                            $"Fragment #{i}, image #{imageIndex}, role {segment.From}, " +
                            $"message id '{segment.Message.id ?? "(null)"}', mime '{image?.MimeType ?? "(null)"}'.",
                            Id,
                            Name);
                    }
                }
            }

            return null;
        }

        private bool ShouldCarry(PromptBuilder.From from) => from switch
        {
            PromptBuilder.From.system => CarrySystemImages,
            PromptBuilder.From.tool_result => CarryToolResultImages,
            PromptBuilder.From.assistant => CarryAssistantImages,
            _ => false
        };

        private static string NewGroupId() => Guid.NewGuid().ToString("N");

        private static ChatMessage CloneWithoutImages(ChatMessage message)
        {
            var clone = (ChatMessage)message.Clone();
            clone.Images = null;
            return clone;
        }

        private static List<ChatImage> CloneImages(IEnumerable<ChatImage> images)
            => images.Select(image => (ChatImage)image.Clone()).ToList();

        private static ContextSegment WithGroup(ContextSegment source, ChatMessage message, string groupId)
        {
            var metadata = new Dictionary<string, object>(source.Metadata)
            {
                [GroupMetadataKey] = groupId
            };

            return new ContextSegment
            {
                Message = message,
                From = source.From,
                SourceType = source.SourceType,
                Metadata = metadata
            };
        }

        private static ContextSegment CreateCarrier(
            IReadOnlyList<ChatImage> images,
            string text,
            string groupId,
            PromptBuilder.From sourceRole)
        {
            return new ContextSegment
            {
                Message = new ChatMessage
                {
                    Content = text,
                    Images = images.Select(image => (ChatImage)image.Clone()).ToList()
                },
                From = PromptBuilder.From.user,
                SourceType = typeof(SpecialRoleImageCarrierNode),
                Metadata = new Dictionary<string, object>
                {
                    [GroupMetadataKey] = groupId,
                    [CarrierMetadataKey] = "synthetic",
                    ["image_carrier_source_role"] = sourceRole.ToString()
                }
            };
        }
    }
}
