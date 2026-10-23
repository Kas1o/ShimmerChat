using SharperLLM.API;
using System.Text.Json.Serialization;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin
{
	public class ApiConfig
	{
		public Guid Id { get; set; } = Guid.NewGuid();
		public string Name { get; set; } = string.Empty;

		[JsonConverter(typeof(JsonStringEnumConverter))]
		public ApiConfigType Type { get; set; }

		#region Kobold
		public string? KoboldUrl { get; set; } = "http://localhost:5001/api";
		public KoboldTextCompletionClient.KoboldAPIConf? KoboldConf { get; set; } = new();

		#endregion

		#region OpenAI
		public string? OpenAIUrl { get; set; } = "http://localhost:5001/api/v1";
		public string? OpenAIApiKey { get; set; } = "key";
		public string? OpenAIModel { get; set; } = "gpt-4o";
		public bool OpenAIStream { get; set; } = true;
		public int OpenAICtx { get; set; } = 16384;
		public bool OpenAIAsIs { get; set; } = false;

		/// <summary>
		/// OpenAI 类型可配置的推理内容回传方式，默认为完整回传。
		/// </summary>
		public ReasoningEchoMode OpenAIReasoningEchoMode { get; set; } = ReasoningEchoMode.Full;

		public string? OpenAICustomRequestBody { get; set; } = null;

		#endregion OpenAI

		#region DeepSeek
		public string? DeepSeekUrl { get; set; } = "https://api.deepseek.com/v1";
		public string? DeepSeekApiKey { get; set; } = "";
		public string? DeepSeekModel { get; set; } = "deepseek-v4-flash";
		public bool DeepSeekStream { get; set; } = true;
		public int DeepSeekCtx { get; set; } = 8192;
		public bool DeepSeekAsIs { get; set; } = false;
		public string? DeepSeekReasoningEffort { get; set; } = null;

		#endregion DeepSeek

		#region Ollama
		public string? OllamaUrl { get; set; }
		public string? OllamaModel { get; set; }

		#endregion

		#region OpenCode Go
		/// <summary>OpenCode Go 的 OpenAI 兼容对话接口前缀（官方文档给出的端点去掉 /chat/completions）。</summary>
		public const string OpenCodeGoDefaultUrl = "https://opencode.ai/inference/go/openai/v1";

		/// <summary>OpenCode Go 的模型列表接口（与对话接口不在同一路径前缀下）。</summary>
		public const string OpenCodeGoDefaultModelsUrl = "https://opencode.ai/inference/go/v1/models";

		/// <summary>OpenCode Go 要求客户端用自有 User-Agent 标识身份，而不是通用 HTTP 库名。</summary>
		public const string OpenCodeGoUserAgent = "ShimmerChat/2.0";

		public string? OpenCodeGoUrl { get; set; } = OpenCodeGoDefaultUrl;
		public string? OpenCodeGoModelsUrl { get; set; } = OpenCodeGoDefaultModelsUrl;
		public string? OpenCodeGoApiKey { get; set; } = "";
		public string? OpenCodeGoModel { get; set; } = "glm-5.3-flash";
		public bool OpenCodeGoStream { get; set; } = true;
		public int OpenCodeGoCtx { get; set; } = 16384;
		public bool OpenCodeGoAsIs { get; set; } = false;

		#endregion OpenCode Go

		#region OpenAIText
		public string? OpenAITextUrl { get; set; } = "http://localhost:5001/api/v1";
		public string? OpenAITextApiKey { get; set; } = "key";
		public string? OpenAITextModel { get; set; } = "gpt-3.5-turbo-instruct";
		public int OpenAITextCtx { get; set; } = 4096;

		#endregion

		// Completion settings
		public List<TextCompletionSetting>? TextCompletionSettings { get; set; } = new List<TextCompletionSetting>();
		public int SelectedTextCompletionSettingIndex { get; set; } = 0;

		[Newtonsoft.Json.JsonIgnore]
		public bool EnableStream => Type switch
		{
			ApiConfigType.OpenAI => OpenAIStream,
			ApiConfigType.DeepSeek => DeepSeekStream,
			ApiConfigType.OpenCodeGo => OpenCodeGoStream,
			_ => false
		};

		/// <summary>
		/// 转换为管线使用的 API 设置。
		/// </summary>
		/// <param name="sessionId">
		/// 当前对话的稳定标识。OpenCode Go 会把它作为 <c>x-opencode-session</c> 发出，
		/// 供上游做会话路由与 prompt 缓存；为 null 时不发送该头。
		/// </param>
		public APISetting ToAPISetting(string? sessionId = null)
		{
			switch (Type)
			{
				case ApiConfigType.Kobold:
					return new APISetting
					{
						ChatClient = new TextToChatAdapter(
							new KoboldTextCompletionClient(KoboldUrl ?? "http://localhost:5001/api")
							{
								conf = KoboldConf ?? new KoboldTextCompletionClient.KoboldAPIConf()
							},
							GetPromptTemplate()),
						SupportsStreaming = true,
						SupportsToolCalling = false
					};

				case ApiConfigType.OpenAI:
				{
					var client = new OpenAIChatCompletionClient(
						_url: OpenAIUrl ?? "https://api.openai.com/v1",
						_apiKey: OpenAIApiKey ?? throw new InvalidOperationException("OpenAI API key is required."),
						_model: OpenAIModel ?? "gpt-4o",
						_max_tokens: OpenAICtx,
						_as_is: OpenAIAsIs
					)
					{
						ReasoningEchoMode = OpenAIReasoningEchoMode
					};

					if (!string.IsNullOrWhiteSpace(OpenAICustomRequestBody))
					{
						Dictionary<string, object>? customProps;
						try
						{
							customProps = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(OpenAICustomRequestBody);
						}
						catch (Newtonsoft.Json.JsonException ex)
						{
							throw new InvalidOperationException($"OpenAI custom request body is not valid JSON: {ex.Message}", ex);
						}

						if (customProps != null && customProps.Count > 0)
							client.CustomRequestProperties = customProps;
					}

					return new APISetting
					{
						ChatClient = client,
						SupportsStreaming = OpenAIStream,
						SupportsToolCalling = true
					};
				}

				case ApiConfigType.DeepSeek:
				{
					var client = new OpenAIChatCompletionClient(
						_url: DeepSeekUrl ?? "https://api.deepseek.com/v1",
						_apiKey: DeepSeekApiKey ?? throw new InvalidOperationException("DeepSeek API key is required."),
						_model: DeepSeekModel ?? "deepseek-v4-flash",
						_max_tokens: DeepSeekCtx,
						_as_is: DeepSeekAsIs
					)
					{
						// DeepSeek 固定完整回传：其推理 + 工具调用链需要完整 reasoning_content。
						ReasoningEchoMode = ReasoningEchoMode.Full,
						CustomRequestProperties = !string.IsNullOrEmpty(DeepSeekReasoningEffort)
							? new Dictionary<string, object>
							{
								["reasoning_effort"] = DeepSeekReasoningEffort,
								["thinking"] = new { type = "enabled" }
							}
							: new Dictionary<string, object> { ["thinking"] = new { type = "disabled" } }
					};

					return new APISetting
					{
						ChatClient = client,
						SupportsStreaming = DeepSeekStream,
						SupportsToolCalling = true
					};
				}

				case ApiConfigType.Ollama:
					return new APISetting
					{
						ChatClient = new TextToChatAdapter(
							new OllamaTextCompletionClient(OllamaUrl ?? "http://localhost:11434", OllamaModel ?? "llama3"),
							GetPromptTemplate()),
						SupportsStreaming = true,
						SupportsToolCalling = false
					};

				case ApiConfigType.OpenCodeGo:
				{
					// OpenCode Go 走 OpenAI 兼容路径，仅额外要求 User-Agent 与会话头。
					var client = new OpenAIChatCompletionClient(
						_url: string.IsNullOrWhiteSpace(OpenCodeGoUrl) ? OpenCodeGoDefaultUrl : OpenCodeGoUrl,
						_apiKey: OpenCodeGoApiKey ?? throw new InvalidOperationException("OpenCode Go API key is required."),
						_model: string.IsNullOrWhiteSpace(OpenCodeGoModel) ? "glm-5.3-flash" : OpenCodeGoModel,
						_max_tokens: OpenCodeGoCtx,
						_as_is: OpenCodeGoAsIs
					)
					{
						ModelsUrl = string.IsNullOrWhiteSpace(OpenCodeGoModelsUrl) ? null : OpenCodeGoModelsUrl,
						CustomHeaders = BuildOpenCodeGoHeaders(sessionId)
					};

					return new APISetting
					{
						ChatClient = client,
						SupportsStreaming = OpenCodeGoStream,
						SupportsToolCalling = true
					};
				}

				case ApiConfigType.OpenAIText:
					return new APISetting
					{
						ChatClient = new TextToChatAdapter(
							new OpenAITextCompletionClient(
								OpenAITextUrl ?? "http://localhost:5001/api/v1",
								OpenAITextApiKey ?? "key",
								OpenAITextModel ?? "gpt-3.5-turbo-instruct",
								maxTokens: OpenAITextCtx),
							GetPromptTemplate()),
						SupportsStreaming = true,
						SupportsToolCalling = false
					};

				case ApiConfigType.Pseudo:
					return new APISetting
					{
						ChatClient = new PseudoChatCompletionClient(),
						SupportsStreaming = true,
						SupportsToolCalling = false
					};

				default:
					throw new InvalidOperationException($"Unsupported API type: {Type}");
			}
		}

		/// <summary>
		/// OpenCode Go 文档要求客户端用自有 User-Agent 标识身份，
		/// 并用稳定的 <c>x-opencode-session</c> 标记每个对话（供上游路由与 prompt 缓存）。
		/// </summary>
		private static Dictionary<string, string> BuildOpenCodeGoHeaders(string? sessionId)
		{
			var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["User-Agent"] = OpenCodeGoUserAgent
			};

			if (!string.IsNullOrWhiteSpace(sessionId))
				headers["x-opencode-session"] = sessionId;

			return headers;
		}

		private SharperLLM.Util.PromptBuilder GetPromptTemplate()
		{
			if (TextCompletionSettings != null
				&& SelectedTextCompletionSettingIndex >= 0
				&& SelectedTextCompletionSettingIndex < TextCompletionSettings.Count)
			{
				var setting = TextCompletionSettings[SelectedTextCompletionSettingIndex];
				var templates = setting.GetMessageTemplates();
				return new SharperLLM.Util.PromptBuilder
				{
					SysSeqPrefix = templates.sys_start,
					SysSeqSuffix = templates.sys_stop,
					InputPrefix = templates.user_start,
					InputSuffix = templates.user_stop,
					OutputPrefix = templates.char_start,
					OutputSuffix = templates.char_stop,
					LatestOutputPrefix = templates.char_start
				};
			}
			return SharperLLM.Util.PromptBuilder.ChatML;
		}

		public ApiConfig Clone()
		{
			return new ApiConfig
			{
				// Intentional: clone gets a new Id via the default Guid.NewGuid()
				Name = Name,
				Type = Type,

				KoboldUrl = KoboldUrl,
				KoboldConf = KoboldConf?.Clone() as KoboldTextCompletionClient.KoboldAPIConf,

				OpenAIUrl = OpenAIUrl,
				OpenAIApiKey = OpenAIApiKey,
				OpenAIModel = OpenAIModel,
				OpenAIStream = OpenAIStream,
				OpenAICtx = OpenAICtx,
				OpenAIAsIs = OpenAIAsIs,
				OpenAIReasoningEchoMode = OpenAIReasoningEchoMode,
				OpenAICustomRequestBody = OpenAICustomRequestBody,

				DeepSeekUrl = DeepSeekUrl,
				DeepSeekApiKey = DeepSeekApiKey,
				DeepSeekModel = DeepSeekModel,
				DeepSeekStream = DeepSeekStream,
				DeepSeekCtx = DeepSeekCtx,
				DeepSeekAsIs = DeepSeekAsIs,
				DeepSeekReasoningEffort = DeepSeekReasoningEffort,

				OllamaUrl = OllamaUrl,
				OllamaModel = OllamaModel,

				OpenCodeGoUrl = OpenCodeGoUrl,
				OpenCodeGoModelsUrl = OpenCodeGoModelsUrl,
				OpenCodeGoApiKey = OpenCodeGoApiKey,
				OpenCodeGoModel = OpenCodeGoModel,
				OpenCodeGoStream = OpenCodeGoStream,
				OpenCodeGoCtx = OpenCodeGoCtx,
				OpenCodeGoAsIs = OpenCodeGoAsIs,

				OpenAITextUrl = OpenAITextUrl,
				OpenAITextApiKey = OpenAITextApiKey,
				OpenAITextModel = OpenAITextModel,
				OpenAITextCtx = OpenAITextCtx,

				TextCompletionSettings = TextCompletionSettings?.Select(tcs => tcs.Clone()).ToList(),
				SelectedTextCompletionSettingIndex = SelectedTextCompletionSettingIndex
			};
		}
	}

	public enum ApiConfigType
	{
		Kobold,
		Ollama,
		OpenAI,
		DeepSeek,
		OpenAIText,
		Pseudo,
		// 新增类型必须追加在末尾：已保存的 API 配置可能把枚举存成序号。
		OpenCodeGo
	}
}
