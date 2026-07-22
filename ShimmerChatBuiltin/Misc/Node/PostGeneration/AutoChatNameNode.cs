using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Misc.Node.PostGeneration
{
    /// <summary>
    /// 自动对话命名策略
    /// </summary>
    public enum AutoChatNameStrategy
    {
        /// <summary>仅当名称为默认日期格式时生成</summary>
        NamePattern,
        /// <summary>同一对话仅生成一次（基于 KVData 标记）</summary>
        GuidFlag,
        /// <summary>每次都重新生成</summary>
        Always
    }

    [NodeInfo("node.auto_chat_name",
        Icon = "✏",
        Color = "var(--node-prompt)",
        CategoryKeys = ["category.chat", "category.post"],
        DescriptionKey = "node.auto_chat_name.desc")]
    public partial class AutoChatNameNode : IPostGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Auto Chat Name";

        [NodeProperty("prop.auto_chat_name.strategy", Order = 10,
            HintKey = "prop.auto_chat_name.strategy.hint")]
        public AutoChatNameStrategy Strategy { get; set; } = AutoChatNameStrategy.NamePattern;

        /// <summary>
        /// 传入 LLM 的最近 N 条对话轮次
        /// </summary>
        [NodeProperty("prop.auto_chat_name.latest_n", Order = 20,
            HintKey = "prop.auto_chat_name.latest_n.hint")]
        public int LatestN { get; set; } = 10;

        /// <summary>
        /// 自定义生成 Prompt 模板。占位符：{conversation}
        /// </summary>
        [NodeProperty("prop.auto_chat_name.prompt_template", Order = 30,
            HintKey = "prop.auto_chat_name.prompt_template.hint", MultiLine = true)]
        public string PromptTemplate { get; set; } =
            "Based on the following conversation, generate a short and descriptive title (maximum 10 words). Output ONLY the title, nothing else. USE THE SAME LANGUAGE.\n\nConversation:\n{conversation}\n\nTitle:";

        // 默认日期名称模式：yyyy-M-d-HH-mm-ss 或类似格式
        // 例如 2026-7-22-23-59-07（中文区域）或 7-22-2026-11-59-07-PM（英文区域）
        [GeneratedRegex(@"^\d{1,4}-\d{1,2}-\d{1,4}-\d{1,2}-\d{1,2}-\d{1,2}(-.*)?$")]
        private static partial Regex DefaultNamePattern();

        private const string AutoNameKVSection = "AutoChatName";

        public async Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;
            var kvData = context.Env.Persistent.KVData;
            var chatGuid = context.Env.Persistent.ChatGuid;

            // 1. 加载 Chat
            Chat chat;
            try
            {
                chat = Chat.Load(chatGuid, kvData);
            }
            catch (Exception ex)
            {
                return Fail(NodeErrorCodes.DataMissing,
                    loc.Format("node_err.auto_chat_name_load_chat_failed", chatGuid, ex.Message));
            }

            // 2. 根据策略判断是否需要生成名称
            var shouldGenerate = Strategy switch
            {
                AutoChatNameStrategy.GuidFlag => !HasNameFlag(kvData, chatGuid),
                AutoChatNameStrategy.Always => true,
                _ => DefaultNamePattern().IsMatch(chat.Name) // NamePattern
            };

            if (!shouldGenerate)
                return PostNodeResult.SuccessResult();

            // 3. 构建对话文本：从 PreFragments 提取 user/assistant 消息 + 最新 ResponseText
            var conversationText = BuildConversationText(context, chat.Name);

            if (string.IsNullOrWhiteSpace(conversationText))
                return PostNodeResult.SuccessResult();

            // 4. 解析 API 配置
            var apiSetting = ResolveAPI(kvData);
            if (apiSetting == null)
                return Fail(NodeErrorCodes.ApiUnavailable, loc["node_err.api_no_settings"]);

            // 5. 填充 Prompt 模板并调用 LLM
            var promptText = PromptTemplate.Replace("{conversation}", conversationText);

            string? generatedName;
            try
            {
                generatedName = await GenerateNameAsync(apiSetting.ChatClient, promptText, context.CancellationToken);
            }
            catch (Exception ex)
            {
                return Fail(NodeErrorCodes.ServiceError,
                    loc.Format("node_err.auto_chat_name_generate_failed", ex.Message));
            }

            if (string.IsNullOrWhiteSpace(generatedName))
                return PostNodeResult.SuccessResult();

            // 6. 清理并设置名称
            generatedName = generatedName.Trim();
            // 移除可能的引号包裹
            if (generatedName.Length > 2 &&
                ((generatedName.StartsWith('"') && generatedName.EndsWith('"')) ||
                 (generatedName.StartsWith('\'') && generatedName.EndsWith('\'')) ||
                 (generatedName.StartsWith('「') && generatedName.EndsWith('」'))))
            {
                generatedName = generatedName[1..^1].Trim();
            }

            // 限制名称长度
            if (generatedName.Length > 200)
                generatedName = generatedName[..200];

            chat.Name = generatedName;
            chat.Save(kvData);

            // 7. 如果是 GuidFlag 策略，设置标记防止重复生成
            if (Strategy == AutoChatNameStrategy.GuidFlag)
                SetNameFlag(kvData, chatGuid);

            // 8. 写调试输出
            context.Env.Persistent.DebugOutput.Write("AutoChatName", "Generated",
                $"Chat '{chatGuid}' name set to: {generatedName}");

            return PostNodeResult.SuccessResult();
        }

        /// <summary>
        /// 从 PreFragments 和 ResponseText 构建对话文本
        /// </summary>
        private string BuildConversationText(PostNodeExecutionContext context, string currentName)
        {
            var lines = new List<string>();
            lines.Add($"[Current Title: {currentName}]");
            lines.Add("");

            // 从 PreFragments 中提取 user 和 assistant 消息（最近 LatestN 条）
            var relevantFragments = context.Env.PreFragments
                .Where(f => f.From == PromptBuilder.From.user || f.From == PromptBuilder.From.assistant)
                .ToList();

            // 取最近 N*2 条（user+assistant 各算一条，所以乘2保留更多上下文）
            var recentFragments = relevantFragments
                .TakeLast(Math.Min(LatestN * 2, relevantFragments.Count))
                .ToList();

            foreach (var frag in recentFragments)
            {
                var role = frag.From == PromptBuilder.From.user ? "User" : "Assistant";
                var content = TruncateContent(frag.Message.Content ?? "", 500);
                lines.Add($"{role}: {content}");
            }

            // 添加最新的 ResponseText
            if (!string.IsNullOrWhiteSpace(context.Env.ResponseText))
            {
                var truncatedResponse = TruncateContent(context.Env.ResponseText, 1000);
                lines.Add($"Assistant: {truncatedResponse}");
            }

            return string.Join("\n", lines);
        }

        private static string TruncateContent(string content, int maxLength)
        {
            if (content.Length <= maxLength)
                return content;
            return content[..maxLength] + "...";
        }

        /// <summary>
        /// 解析 API 配置（与 APISelectNode 相同的逻辑）
        /// </summary>
        private static APISetting? ResolveAPI(IKVDataService kvData)
        {
            var json = kvData.Read("ApiSettings", "apiSetting") ?? "[]";
            var settings = JsonConvert.DeserializeObject<List<ApiConfig>>(json) ?? [];

            if (settings.Count == 0)
                return null;

            ApiConfig? selectedConfig = null;
            var globalGuidStr = kvData.Read("ApiSettings", "selectedAPIGuid");
            if (!string.IsNullOrEmpty(globalGuidStr))
                selectedConfig = settings.FirstOrDefault(s => s.Id.ToString() == globalGuidStr);

            selectedConfig ??= settings[0];

            return selectedConfig.ToAPISetting();
        }

        /// <summary>
        /// 调用 LLM 生成名称
        /// </summary>
        private static async Task<string?> GenerateNameAsync(
            IChatCompletionClient client, string promptText, CancellationToken ct)
        {
            var pb = new PromptBuilder
            {
                System = "You are a concise title generator. Generate ONLY the title, no extra text.",
                Messages = new (ChatMessage, PromptBuilder.From)[]
                {
                    (new ChatMessage { Content = promptText }, PromptBuilder.From.user)
                }
            };

            var response = await client.GenerateAsync(pb);
            return response.Body.Content?.Trim();
        }

        /// <summary>
        /// 检查 KVData 中是否已存在自动命名标记
        /// </summary>
        private static bool HasNameFlag(IKVDataService kvData, Guid chatGuid)
        {
            var flag = kvData.Read(AutoNameKVSection, chatGuid.ToString());
            return flag == "1";
        }

        /// <summary>
        /// 在 KVData 中设置自动命名标记
        /// </summary>
        private static void SetNameFlag(IKVDataService kvData, Guid chatGuid)
        {
            kvData.Write(AutoNameKVSection, chatGuid.ToString(), "1");
        }

        private PostNodeResult Fail(string code, string message)
        {
            var r = PostNodeResult.Failure(code, message);
            r.NodeId = Id;
            r.NodeName = Name;
            return r;
        }
    }
}
