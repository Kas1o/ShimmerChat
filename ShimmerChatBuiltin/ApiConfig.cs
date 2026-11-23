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

		/// <summary>请求体根级别自定义参数（JSON 对象，浅合并）；同键覆盖客户端预写入的字段。</summary>
		public string? OpenAICustomRequestBody { get; set; } = null;

		/// <summary>自定义请求头（每行 Name: Value）；同名头覆盖默认 Authorization。</summary>
		public string? OpenAICustomHeaders { get; set; } = null;

		#endregion OpenAI

		#region DeepSeek
		public string? DeepSeekUrl { get; set; } = "https://api.deepseek.com/v1";
		public string? DeepSeekApiKey { get; set; } = "";
		public string? DeepSeekModel { get; set; } = "deepseek-v4-flash";
		public bool DeepSeekStream { get; set; } = true;
		public int DeepSeekCtx { get; set; } = 8192;
		public bool DeepSeekAsIs { get; set; } = false;
		public string? DeepSeekReasoningEffort { get; set; } = null;

		/// <summary>
		/// 请求体根级别自定义参数（JSON 对象，浅合并）。DeepSeek 会预写入
		/// <c>thinking</c>（填写档位时还有 <c>reasoning_effort</c>），这里的同名键覆盖预写入值。
		/// </summary>
		public string? DeepSeekCustomRequestBody { get; set; } = null;

		/// <summary>自定义请求头（每行 Name: Value）；同名头覆盖默认 Authorization。</summary>
		public string? DeepSeekCustomHeaders { get; set; } = null;

		#endregion DeepSeek

		#region Ollama
		public string? OllamaUrl { get; set; }
		public string? OllamaModel { get; set; }

		#endregion

		#region OpenCode Go
		/// <summary>OpenCode Go 的 OpenAI 兼容对话接口前缀（官方文档给出的端点去掉 /chat/completions）。</summary>
		public const string OpenCodeGoDefaultUrl = "https://opencode.ai/inference/go/openai/v1";

		/// <summary>OpenCode Go 的 Gemini 原生接口前缀（官方文档给出的端点去掉 /models/&lt;model&gt;:generateContent）。</summary>
		public const string OpenCodeGoGeminiDefaultUrl = "https://opencode.ai/inference/go/google/v1beta";

		/// <summary>OpenCode Go 的模型列表接口（与对话接口不在同一路径前缀下；OpenAI 形状，两种模式共用）。</summary>
		public const string OpenCodeGoDefaultModelsUrl = "https://opencode.ai/inference/go/v1/models";

		/// <summary>OpenCode Go 要求客户端用自有 User-Agent 标识身份，而不是通用 HTTP 库名。</summary>
		public const string OpenCodeGoUserAgent = "ShimmerChat/2.0";

		/// <summary>OpenAI 兼容模式未填模型时使用的默认模型。</summary>
		public const string OpenCodeGoDefaultModel = "glm-5.3-flash";

		/// <summary>Gemini 模式未填模型时使用的默认模型（OpenCode Go 当前提供的 Gemini 模型）。</summary>
		public const string OpenCodeGoDefaultGeminiModel = "gemini-4-argon";

		public string? OpenCodeGoUrl { get; set; } = OpenCodeGoDefaultUrl;
		public string? OpenCodeGoModelsUrl { get; set; } = OpenCodeGoDefaultModelsUrl;
		public string? OpenCodeGoApiKey { get; set; } = "";
		public string? OpenCodeGoModel { get; set; } = OpenCodeGoDefaultModel;
		public bool OpenCodeGoStream { get; set; } = true;
		public int OpenCodeGoCtx { get; set; } = 16384;
		public bool OpenCodeGoAsIs { get; set; } = false;

		/// <summary>
		/// OpenAI 兼容模式的请求体根级别自定义参数（JSON 对象，浅合并）；同键覆盖
		/// 客户端预写入的 model / max_tokens / stream 等字段。该模式没有思考档位开关，
		/// 需要精调时用这里覆盖 thinking / reasoning_effort 之类字段。
		/// </summary>
		public string? OpenCodeGoCustomRequestBody { get; set; } = null;

		/// <summary>
		/// OpenAI 兼容模式的自定义请求头（每行 Name: Value）。OpenCode Go 强制要求
		/// 应用标识（User-Agent）与对话标识（x-opencode-session），二者由宿主预写入；
		/// 同名头会覆盖预写入值。
		/// </summary>
		public string? OpenCodeGoCustomHeaders { get; set; } = null;

		/// <summary>
		/// 接口模式：OpenAI 兼容（默认）或 Gemini 原生。两种模式走同一 API Key 与
		/// 同一套 UA / x-opencode-session 头，只是请求形状与鉴权头不同。
		/// 新模式必须追加在枚举末尾（旧配置按序号持久化）。
		/// </summary>
		public OpenCodeGoMode OpenCodeGoMode { get; set; } = OpenCodeGoMode.OpenAICompatible;

		/// <summary>Gemini 模式的接口前缀；留空时回退到官方默认值。</summary>
		public string? OpenCodeGoGeminiUrl { get; set; } = OpenCodeGoGeminiDefaultUrl;

		/// <summary>Gemini 模式下的思考档位，字面直译为 thinkingLevel；Default = 不发送。</summary>
		public GeminiThinkingLevel OpenCodeGoGeminiThinking { get; set; } = GeminiThinkingLevel.Default;

		/// <summary>Gemini 模式下是否随响应返回推理摘要（thinkingConfig.includeThoughts），默认开。</summary>
		public bool OpenCodeGoGeminiIncludeThoughts { get; set; } = true;

		/// <summary>Gemini 模式下对话中部 system 片段的替换模板（{0} = 原文）；留空 = 构建请求时显式失败。</summary>
		public string? OpenCodeGoGeminiMidSystemTemplate { get; set; } = null;

		/// <summary>Gemini 模式下请求体根级别的自定义参数（JSON 对象，浅合并）；非法 JSON 显式失败。</summary>
		public string? OpenCodeGoGeminiCustomRequestBody { get; set; } = null;

		/// <summary>
		/// Gemini 模式的自定义请求头（每行 Name: Value）。与 OpenAI 兼容模式相同：
		/// 宿主预写入的 User-Agent / x-opencode-session 会被同名头覆盖。
		/// </summary>
		public string? OpenCodeGoGeminiCustomHeaders { get; set; } = null;

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

					var customProps = ParseCustomRequestBody(OpenAICustomRequestBody, "OpenAI");
					if (customProps != null)
						client.CustomRequestProperties = customProps;

					var customHeaders = ParseCustomHeaders(OpenAICustomHeaders, "OpenAI");
					if (customHeaders != null)
						client.CustomHeaders = customHeaders;

					return new APISetting
					{
						ChatClient = client,
						SupportsStreaming = OpenAIStream,
						SupportsToolCalling = true
					};
				}

				case ApiConfigType.DeepSeek:
				{
					// DeepSeek 固定完整回传：其推理 + 工具调用链需要完整 reasoning_content。
					// thinking / reasoning_effort 属于客户端预写入项：自定义请求体最后合并，
					// 同键覆盖它们（提示已告知用户该语义）。
					var reasoningProperties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
					{
						["thinking"] = new { type = "disabled" }
					};

					if (!string.IsNullOrEmpty(DeepSeekReasoningEffort))
					{
						reasoningProperties["reasoning_effort"] = DeepSeekReasoningEffort;
						reasoningProperties["thinking"] = new { type = "enabled" };
					}

					var customProps = ParseCustomRequestBody(DeepSeekCustomRequestBody, "DeepSeek");
					if (customProps != null)
					{
						foreach (var (key, value) in customProps)
							reasoningProperties[key] = value;
					}

					var client = new OpenAIChatCompletionClient(
						_url: DeepSeekUrl ?? "https://api.deepseek.com/v1",
						_apiKey: DeepSeekApiKey ?? throw new InvalidOperationException("DeepSeek API key is required."),
						_model: DeepSeekModel ?? "deepseek-v4-flash",
						_max_tokens: DeepSeekCtx,
						_as_is: DeepSeekAsIs
					)
					{
						ReasoningEchoMode = ReasoningEchoMode.Full,
						CustomRequestProperties = reasoningProperties,
						CustomHeaders = ParseCustomHeaders(DeepSeekCustomHeaders, "DeepSeek")
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
					// 两种模式共用同一个 API Key 与 UA / 会话头，只是请求形状与鉴权头不同。
					// 未知模式必须显式失败：静默落到 OpenAI 路径会把坏配置伪装成可用。
					if (!Enum.IsDefined(OpenCodeGoMode))
						throw new InvalidOperationException($"Unsupported OpenCode Go mode: {OpenCodeGoMode}.");

					var apiKey = OpenCodeGoApiKey ?? throw new InvalidOperationException("OpenCode Go API key is required.");
					var useGeminiMode = OpenCodeGoMode == OpenCodeGoMode.Gemini;
					var model = string.IsNullOrWhiteSpace(OpenCodeGoModel)
						? useGeminiMode ? OpenCodeGoDefaultGeminiModel : OpenCodeGoDefaultModel
						: OpenCodeGoModel;

					if (useGeminiMode)
					{
						// OpenCode Go 的 Gemini 路径用 Authorization: Bearer 鉴权（不是 Google 的
						// x-goog-api-key），模型与 generateContent 方法仍按原生协议拼进 URL。
						var geminiClient = new GeminiChatCompletionClient(
							_url: string.IsNullOrWhiteSpace(OpenCodeGoGeminiUrl) ? OpenCodeGoGeminiDefaultUrl : OpenCodeGoGeminiUrl,
							_apiKey: apiKey,
							_model: model,
							_maxTokens: OpenCodeGoCtx)
						{
							UseBearerAuth = true,
							ThinkingLevel = MapThinkingLevel(OpenCodeGoGeminiThinking),
							IncludeThoughts = OpenCodeGoGeminiIncludeThoughts,
							MidSystemTemplate = string.IsNullOrWhiteSpace(OpenCodeGoGeminiMidSystemTemplate)
								? null
								: OpenCodeGoGeminiMidSystemTemplate,
							CustomHeaders = BuildOpenCodeGoHeaders(
								sessionId, OpenCodeGoGeminiCustomHeaders, "OpenCode Go Gemini"),
							Log = log
						};

						var customProps = ParseCustomRequestBody(OpenCodeGoGeminiCustomRequestBody, "OpenCode Go Gemini");
						if (customProps != null)
							geminiClient.CustomRequestProperties = customProps;

						return new APISetting
						{
							ChatClient = geminiClient,
							SupportsStreaming = OpenCodeGoStream,
							SupportsToolCalling = true
						};
					}

					// OpenAI 兼容模式：仅额外要求 User-Agent 与会话头（自定义头可覆盖）。
					var client = new OpenAIChatCompletionClient(
						_url: string.IsNullOrWhiteSpace(OpenCodeGoUrl) ? OpenCodeGoDefaultUrl : OpenCodeGoUrl,
						_apiKey: apiKey,
						_model: model,
						_max_tokens: OpenCodeGoCtx,
						_as_is: OpenCodeGoAsIs
					)
					{
						ModelsUrl = string.IsNullOrWhiteSpace(OpenCodeGoModelsUrl) ? null : OpenCodeGoModelsUrl,
						CustomHeaders = BuildOpenCodeGoHeaders(sessionId, OpenCodeGoCustomHeaders, "OpenCode Go"),
						CustomRequestProperties = ParseCustomRequestBody(OpenCodeGoCustomRequestBody, "OpenCode Go")
					};

					// TODO(modes): OpenAI 兼容路径没有独立的思考/推理档位开关（对照 DeepSeek 的
					// DeepSeekReasoningEffort）；需要时由 OpenCodeGoCustomRequestBody 覆盖
					// thinking / reasoning_effort 等字段。Gemini 模式已通过 OpenCodeGoMode 支持。

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
						ThinkingLevel = MapThinkingLevel(GeminiThinking),
						MidSystemTemplate = string.IsNullOrWhiteSpace(GeminiMidSystemTemplate) ? null : GeminiMidSystemTemplate,
						IncludeThoughts = GeminiIncludeThoughts,
						Log = log
					};

					var customProps = ParseCustomRequestBody(GeminiCustomRequestBody, "Gemini");
					if (customProps != null)
						client.CustomRequestProperties = customProps;

					var customHeaders = ParseCustomHeaders(GeminiCustomHeaders, "Gemini");
					if (customHeaders != null)
						client.CustomHeaders = customHeaders;

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
		/// 思考档位字面直译为 thinkingLevel；Default = 不发送档位。不做模型名探测。
		/// </summary>
		private static string? MapThinkingLevel(GeminiThinkingLevel level) => level switch
		{
			GeminiThinkingLevel.Minimal => "minimal",
			GeminiThinkingLevel.Low => "low",
			GeminiThinkingLevel.Medium => "medium",
			GeminiThinkingLevel.High => "high",
			_ => null
		};

		/// <summary>
		/// 解析自定义请求体（JSON 对象根级浅合并）。非法 JSON 显式失败，
		/// 避免用户以为参数生效而实际请求没带上；空文本返回 null。
		/// </summary>
		private static Dictionary<string, object>? ParseCustomRequestBody(string? json, string label)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try
			{
				var props = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
				return props is { Count: > 0 } ? props : null;
			}
			catch (Newtonsoft.Json.JsonException ex)
			{
				throw new InvalidOperationException($"{label} custom request body is not valid JSON: {ex.Message}", ex);
			}
		}

		/// <summary>
		/// 解析 "每行一个 Name: Value" 的自定义请求头文本；空文本返回 null（调用方保持默认头）。
		/// 非法行显式失败而不是静默忽略，避免用户以为配置生效而实际请求缺头。
		/// <paramref name="label"/> 用于指出是哪一类配置出错（同一份解析供对话与模型列表装配共用）。
		/// </summary>
		private static Dictionary<string, string>? ParseCustomHeaders(string? text, string label)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var rawLine in text.Split('\n'))
			{
				var line = rawLine.Trim();
				if (line.Length == 0)
					continue;

				var separator = line.IndexOf(':');
				if (separator <= 0)
					throw new InvalidOperationException($"{label} custom header line is not in 'Name: Value' format: '{rawLine.TrimEnd()}'");

				var name = line[..separator].Trim();
				var value = line[(separator + 1)..].Trim();
				if (string.IsNullOrWhiteSpace(name) || value.Length == 0)
					throw new InvalidOperationException($"{label} custom header line has an empty name or value: '{rawLine.TrimEnd()}'");

				headers[name] = value;
			}

			if (headers.Count == 0)
				throw new InvalidOperationException($"{label} custom headers text contains no 'Name: Value' lines.");

			return headers;
		}

		/// <summary>
		/// OpenCode Go 文档要求客户端用自有 User-Agent 标识身份，
		/// 并用稳定的 <c>x-opencode-session</c> 标记每个对话（供上游路由与 prompt 缓存）。
		/// OpenAI 兼容与 Gemini 两种模式共用这一份头；用户自定义头最后合并，
		/// 同名头覆盖预写入的应用 / 对话标识（设置页提示已告知该语义）。
		/// </summary>
		private static Dictionary<string, string> BuildOpenCodeGoHeaders(
			string? sessionId, string? customHeadersText, string label)
		{
			var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["User-Agent"] = OpenCodeGoUserAgent
			};

			if (!string.IsNullOrWhiteSpace(sessionId))
				headers["x-opencode-session"] = sessionId;

			var customHeaders = ParseCustomHeaders(customHeadersText, label);
			if (customHeaders != null)
			{
				foreach (var (name, value) in customHeaders)
					headers[name] = value;
			}

			return headers;
		}

		/// <summary>
		/// 模型列表（OpenAI 形状）请求用的客户端，供 OpenAI / DeepSeek / OpenCode Go 共用。
		/// 与对话请求共用同一份地址、密钥与自定义头：用户配了网关鉴权头后，「获取模型」
		/// 不能因为走另一套装配而失败。
		/// </summary>
		/// <param name="sessionId">OpenCode Go 的会话标识；获取模型时通常没有对话上下文，传 null。</param>
		public OpenAIChatCompletionClient CreateModelListClient(string? sessionId = null)
		{
			switch (Type)
			{
				case ApiConfigType.OpenAI:
					return new OpenAIChatCompletionClient(
						_url: OpenAIUrl ?? "https://api.openai.com/v1",
						_apiKey: OpenAIApiKey ?? "",
						_model: OpenAIModel ?? "gpt-4o")
					{
						CustomHeaders = ParseCustomHeaders(OpenAICustomHeaders, "OpenAI")
					};

				case ApiConfigType.DeepSeek:
					return new OpenAIChatCompletionClient(
						_url: DeepSeekUrl ?? "https://api.deepseek.com/v1",
						_apiKey: DeepSeekApiKey ?? "",
						_model: DeepSeekModel ?? "deepseek-v4-flash")
					{
						CustomHeaders = ParseCustomHeaders(DeepSeekCustomHeaders, "DeepSeek")
					};

				case ApiConfigType.OpenCodeGo:
				{
					// 两种模式的请求头规则不同（各有自己的自定义头字段），错误标签也要可区分。
					var geminiMode = OpenCodeGoMode == OpenCodeGoMode.Gemini;
					return new OpenAIChatCompletionClient(
						_url: string.IsNullOrWhiteSpace(OpenCodeGoUrl) ? OpenCodeGoDefaultUrl : OpenCodeGoUrl,
						_apiKey: OpenCodeGoApiKey ?? "",
						_model: string.IsNullOrWhiteSpace(OpenCodeGoModel) ? OpenCodeGoDefaultModel : OpenCodeGoModel)
					{
						ModelsUrl = string.IsNullOrWhiteSpace(OpenCodeGoModelsUrl) ? null : OpenCodeGoModelsUrl,
						CustomHeaders = BuildOpenCodeGoHeaders(
							sessionId,
							geminiMode ? OpenCodeGoGeminiCustomHeaders : OpenCodeGoCustomHeaders,
							geminiMode ? "OpenCode Go Gemini" : "OpenCode Go")
					};
				}

				default:
					throw new InvalidOperationException($"API type '{Type}' does not support model listing.");
			}
		}

		/// <summary>
		/// Gemini 原生模型列表请求用的客户端（/models 原生形状）；自定义头同样生效。
		/// </summary>
		public GeminiChatCompletionClient CreateGeminiModelListClient() => new(
			_url: string.IsNullOrWhiteSpace(GeminiUrl) ? GeminiDefaultUrl : GeminiUrl,
			_apiKey: GeminiApiKey ?? "",
			_model: string.IsNullOrWhiteSpace(GeminiModel) ? "gemini-4-hydrogen" : GeminiModel)
		{
			CustomHeaders = ParseCustomHeaders(GeminiCustomHeaders, "Gemini")
		};

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
				OpenAICustomHeaders = OpenAICustomHeaders,

				DeepSeekUrl = DeepSeekUrl,
				DeepSeekApiKey = DeepSeekApiKey,
				DeepSeekModel = DeepSeekModel,
				DeepSeekStream = DeepSeekStream,
				DeepSeekCtx = DeepSeekCtx,
				DeepSeekAsIs = DeepSeekAsIs,
				DeepSeekReasoningEffort = DeepSeekReasoningEffort,
				DeepSeekCustomRequestBody = DeepSeekCustomRequestBody,
				DeepSeekCustomHeaders = DeepSeekCustomHeaders,

				OllamaUrl = OllamaUrl,
				OllamaModel = OllamaModel,

				OpenCodeGoUrl = OpenCodeGoUrl,
				OpenCodeGoModelsUrl = OpenCodeGoModelsUrl,
				OpenCodeGoApiKey = OpenCodeGoApiKey,
				OpenCodeGoModel = OpenCodeGoModel,
				OpenCodeGoStream = OpenCodeGoStream,
				OpenCodeGoCtx = OpenCodeGoCtx,
				OpenCodeGoAsIs = OpenCodeGoAsIs,
				OpenCodeGoCustomRequestBody = OpenCodeGoCustomRequestBody,
				OpenCodeGoCustomHeaders = OpenCodeGoCustomHeaders,
				OpenCodeGoMode = OpenCodeGoMode,
				OpenCodeGoGeminiUrl = OpenCodeGoGeminiUrl,
				OpenCodeGoGeminiThinking = OpenCodeGoGeminiThinking,
				OpenCodeGoGeminiIncludeThoughts = OpenCodeGoGeminiIncludeThoughts,
				OpenCodeGoGeminiMidSystemTemplate = OpenCodeGoGeminiMidSystemTemplate,
				OpenCodeGoGeminiCustomRequestBody = OpenCodeGoGeminiCustomRequestBody,
				OpenCodeGoGeminiCustomHeaders = OpenCodeGoGeminiCustomHeaders,

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

	/// <summary>
	/// OpenCode Go 的接口模式。已保存配置按序号持久化，新模式只能追加在末尾。
	/// </summary>
	public enum OpenCodeGoMode
	{
		/// <summary>OpenAI 兼容路径（/openai/v1），Authorization: Bearer 鉴权。</summary>
		OpenAICompatible,

		/// <summary>Gemini 原生路径（/google/v1beta），Authorization: Bearer 鉴权。</summary>
		Gemini
	}
}
