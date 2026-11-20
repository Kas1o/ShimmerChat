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

		/// <summary>
		/// 收藏标记：仅影响配置选择器的排序（置顶），不改变持久化列表本身。
		/// 旧配置没有该字段时按未收藏处理。
		/// </summary>
		public bool Pinned { get; set; }

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

		#region Gemini
		/// <summary>Gemini 原生 REST 接口前缀（v1beta；区域受限用户可改指向自定义网关/代理）。</summary>
		public const string GeminiDefaultUrl = "https://generativelanguage.googleapis.com/v1beta";

		public string? GeminiUrl { get; set; } = GeminiDefaultUrl;
		public string? GeminiApiKey { get; set; } = "";
		public string? GeminiModel { get; set; } = "gemini-4-hydrogen";
		public bool GeminiStream { get; set; } = true;
		public int GeminiCtx { get; set; } = 16384;

		/// <summary>
		/// 思考档位，字面直译为 thinkingLevel；Default = 不发送 thinkingConfig（模型默认）。
		/// 不做模型名探测：非法组合由 Gemini API 显式报错，恒显提示负责事先告知。
		/// </summary>
		public GeminiThinkingLevel GeminiThinking { get; set; } = GeminiThinkingLevel.Default;

		/// <summary>
		/// 是否随响应返回推理摘要（thinkingConfig.includeThoughts）。默认开：
		/// Gemini 3/4 恒开思考，这只是摘要可见性开关，走与 DeepSeek reasoning_content 相同的展示链路。
		/// </summary>
		public bool GeminiIncludeThoughts { get; set; } = true;

		/// <summary>对话中部 system 片段的替换模板（{0} = 原文），映射为 user turn；留空 = 构建请求时显式失败。</summary>
		public string? GeminiMidSystemTemplate { get; set; } = null;

		public string? GeminiCustomRequestBody { get; set; } = null;
		public string? GeminiCustomHeaders { get; set; } = null;

		#endregion Gemini

		// Completion settings
		public List<TextCompletionSetting>? TextCompletionSettings { get; set; } = new List<TextCompletionSetting>();
		public int SelectedTextCompletionSettingIndex { get; set; } = 0;

		[Newtonsoft.Json.JsonIgnore]
		public bool EnableStream => Type switch
		{
			ApiConfigType.OpenAI => OpenAIStream,
			ApiConfigType.DeepSeek => DeepSeekStream,
			ApiConfigType.OpenCodeGo => OpenCodeGoStream,
			ApiConfigType.Gemini => GeminiStream,
			_ => false
		};

		/// <summary>
		/// 当前类型实际使用的模型名。只给 UI 展示用（例如节点属性下拉的副标题），
		/// 集中在这里避免各处按类型猜字段。
		/// </summary>
		[Newtonsoft.Json.JsonIgnore]
		public string ModelName => Type switch
		{
			ApiConfigType.OpenAI => OpenAIModel ?? "",
			ApiConfigType.DeepSeek => DeepSeekModel ?? "",
			ApiConfigType.Ollama => OllamaModel ?? "",
			ApiConfigType.OpenAIText => OpenAITextModel ?? "",
			ApiConfigType.OpenCodeGo => OpenCodeGoModel ?? "",
			ApiConfigType.Gemini => GeminiModel ?? "",
			_ => ""
		};

		/// <summary>
		/// 转换为管线使用的 API 设置。
		/// </summary>
		/// <param name="sessionId">
		/// 当前对话的稳定标识。OpenCode Go 会把它作为 <c>x-opencode-session</c> 发出，
		/// 供上游做会话路由与 prompt 缓存；为 null 时不发送该头。
		/// </param>
		/// <param name="log">
		/// 可选告警通道，透传给客户端（Gemini 垫 dummy 签名、模板降级等可见事件）。
		/// 传 null 时这些事件只发生在客户端内部、不留痕。
		/// </param>
		public APISetting ToAPISetting(string? sessionId = null, Action<string>? log = null)
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

					// TODO(headers): OpenAIChatCompletionClient 已支持 CustomHeaders，但 OpenAI/DeepSeek
					// 尚无用户可编辑的请求头字段与设置页入口；补齐时在此装配（DeepSeek 分支同），
					// 非法头沿用 OpenAICustomRequestBody 的显式失败语义，不静默忽略。

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

					// TODO(modes): OpenCode Go 也需要思考/推理多模式切换（对照 DeepSeek 的
					// DeepSeekReasoningEffort + thinking 开关）；补齐时在此装配 CustomRequestProperties。

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

				case ApiConfigType.Gemini:
				{
					var client = new GeminiChatCompletionClient(
						_url: string.IsNullOrWhiteSpace(GeminiUrl) ? GeminiDefaultUrl : GeminiUrl,
						_apiKey: GeminiApiKey ?? "",
						_model: string.IsNullOrWhiteSpace(GeminiModel) ? "gemini-4-hydrogen" : GeminiModel,
						_maxTokens: GeminiCtx)
					{
						ThinkingLevel = GeminiThinking switch
						{
							GeminiThinkingLevel.Minimal => "minimal",
							GeminiThinkingLevel.Low => "low",
							GeminiThinkingLevel.Medium => "medium",
							GeminiThinkingLevel.High => "high",
							_ => null
						},
						MidSystemTemplate = string.IsNullOrWhiteSpace(GeminiMidSystemTemplate) ? null : GeminiMidSystemTemplate,
						IncludeThoughts = GeminiIncludeThoughts,
						Log = log
					};

					if (!string.IsNullOrWhiteSpace(GeminiCustomRequestBody))
					{
						Dictionary<string, object>? customProps;
						try
						{
							customProps = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(GeminiCustomRequestBody);
						}
						catch (Newtonsoft.Json.JsonException ex)
						{
							throw new InvalidOperationException($"Gemini custom request body is not valid JSON: {ex.Message}", ex);
						}

						if (customProps != null && customProps.Count > 0)
							client.CustomRequestProperties = customProps;
					}

					if (!string.IsNullOrWhiteSpace(GeminiCustomHeaders))
						client.CustomHeaders = ParseHeaderLines(GeminiCustomHeaders);

					return new APISetting
					{
						ChatClient = client,
						SupportsStreaming = GeminiStream,
						SupportsToolCalling = true
					};
				}

				default:
					throw new InvalidOperationException($"Unsupported API type: {Type}");
			}
		}

		/// <summary>
		/// OpenCode Go 文档要求客户端用自有 User-Agent 标识身份，
		/// 并用稳定的 <c>x-opencode-session</c> 标记每个对话（供上游路由与 prompt 缓存）。
		/// </summary>
		/// <summary>
		/// 解析 "每行一个 Name: Value" 的自定义请求头文本。非法行显式失败而不是静默忽略，
		/// 避免用户以为配置生效而实际请求缺头。
		/// </summary>
		private static Dictionary<string, string> ParseHeaderLines(string text)
		{
			var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var rawLine in text.Split('\n'))
			{
				var line = rawLine.Trim();
				if (line.Length == 0)
					continue;

				var separator = line.IndexOf(':');
				if (separator <= 0)
					throw new InvalidOperationException($"Gemini custom header line is not in 'Name: Value' format: '{rawLine.TrimEnd()}'");

				var name = line[..separator].Trim();
				var value = line[(separator + 1)..].Trim();
				if (string.IsNullOrWhiteSpace(name) || value.Length == 0)
					throw new InvalidOperationException($"Gemini custom header line has an empty name or value: '{rawLine.TrimEnd()}'");

				headers[name] = value;
			}

			if (headers.Count == 0)
				throw new InvalidOperationException("Gemini custom headers text contains no 'Name: Value' lines.");

			return headers;
		}

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
				Pinned = Pinned,
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

				GeminiUrl = GeminiUrl,
				GeminiApiKey = GeminiApiKey,
				GeminiModel = GeminiModel,
				GeminiStream = GeminiStream,
				GeminiCtx = GeminiCtx,
				GeminiThinking = GeminiThinking,
				GeminiIncludeThoughts = GeminiIncludeThoughts,
				GeminiMidSystemTemplate = GeminiMidSystemTemplate,
				GeminiCustomRequestBody = GeminiCustomRequestBody,
				GeminiCustomHeaders = GeminiCustomHeaders,

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
		OpenCodeGo,
		Gemini
	}

	/// <summary>Gemini 思考档位。字面直译为 thinkingLevel；Default = 不发送 thinkingConfig（模型默认）。</summary>
	public enum GeminiThinkingLevel
	{
		Default,
		Minimal,
		Low,
		Medium,
		High
	}
}
