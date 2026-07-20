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
			_ => false
		};

		public APISetting ToAPISetting()
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
					);

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
		Pseudo
	}
}
