﻿﻿using SharperLLM.API;
using System.Text.Json.Serialization;

namespace ShimmerChatLib
{
	public class ApiSetting
	{
		// 不再持有运行时对象
		public string Name { get; set; } = string.Empty;

		[JsonConverter(typeof(JsonStringEnumConverter))]
		public ApiSettingType Type { get; set; }

		#region Kobold
		public string? KoboldUrl { get; set; } = "http://localhost:5001/api";
		public KoboldAPI.KoboldAPIConf? KoboldConf { get; set; } = new();

		#endregion

		#region OpenAI
		public string? OpenAIUrl { get; set; } = "http://localhost:5001/api/v1";
		public string? OpenAIApiKey { get; set; } = "key";
		public string? OpenAIModel { get; set; } = "gpt-4o";
		public bool OpenAIStream { get; set; } = true;
		public int OpenAICtx { get; set; } = 16384;

		/// <summary>
		/// 是否启用继续功能（prefix continuation），对最后一条AI消息附加 prefix: true 参数
		/// </summary>
		public bool OpenAIEnableContinuation { get; set; } = false;
		/// <summary>
		/// 用于高级调试的原版输出模式，不会对消息做防御性修改
		/// </summary>
		public bool OpenAIAsIs { get; set; } = false;

		#endregion OpenAI

		#region DeepSeek
		public string? DeepSeekUrl { get; set; } = "https://api.deepseek.com/v1";
		public string? DeepSeekApiKey { get; set; } = "";
		public string? DeepSeekModel { get; set; } = "deepseek-v4-flash";
		public bool DeepSeekStream { get; set; } = true;
		public int DeepSeekCtx { get; set; } = 8192;
		public bool DeepSeekEnableContinuation { get; set; } = false;
		public bool DeepSeekAsIs { get; set; } = false;
		/// <summary>
		/// 是否启用 DeepSeek 思考模式 (reasoning)
		/// </summary>
		public bool DeepSeekEnableThinking { get; set; } = false;

		#endregion DeepSeek

		#region Ollama
		public string? OllamaUrl { get; set; }
		public string? OllamaModel { get; set; }

		#endregion

		// Completion settings
		public CompletionType CompletionType { get; set; } = CompletionType.ChatCompletion;
		public List<TextCompletionSetting>? TextCompletionSettings { get; set; } = new List<TextCompletionSetting>();
		public int SelectedTextCompletionSettingIndex { get; set; } = 0;

		/// <summary>
		/// 获取当前 API 类型是否启用了继续功能
		/// </summary>
		[Newtonsoft.Json.JsonIgnore]
		public bool EnableContinuation => Type switch
		{
			ApiSettingType.OpenAI => OpenAIEnableContinuation,
			ApiSettingType.DeepSeek => DeepSeekEnableContinuation,
			_ => false
		};

		/// <summary>
		/// 获取当前 API 类型是否启用了流式输出
		/// </summary>
		[Newtonsoft.Json.JsonIgnore]
		public bool EnableStream => Type switch
		{
			ApiSettingType.OpenAI => OpenAIStream,
			ApiSettingType.DeepSeek => DeepSeekStream,
			_ => false
		};

		/// <summary>
		/// 根据当前配置创建对应的 ILLMAPI 实例。
		/// </summary>
		public ILLMAPI LLMApi
		{
			get
			{
				return Type switch
				{
					ApiSettingType.Kobold => new KoboldAPI(KoboldUrl ?? "http://localhost:5001/api")
					{
						conf = KoboldConf ?? new KoboldAPI.KoboldAPIConf()
					},

					ApiSettingType.OpenAI => new OpenAIAPI(
						_url: OpenAIUrl ?? "https://api.openai.com/v1",
						_apiKey: OpenAIApiKey ?? throw new InvalidOperationException("OpenAI API key is required."),
						_model: OpenAIModel ?? "gpt-4o",
						_max_tokens: OpenAICtx,
						_as_is: OpenAIAsIs
					),

					ApiSettingType.DeepSeek => new OpenAIAPI(
						_url: DeepSeekUrl ?? "https://api.deepseek.com/v1",
						_apiKey: DeepSeekApiKey ?? throw new InvalidOperationException("DeepSeek API key is required."),
						_model: DeepSeekModel ?? "deepseek-v4-flash",
						_max_tokens: DeepSeekCtx,
						_as_is: DeepSeekAsIs
					)
					{
						CustomRequestProperties = DeepSeekEnableThinking
							? new Dictionary<string, object> { ["thinking"] = new { type = "enabled" } }
							: new Dictionary<string, object> { ["thinking"] = new { type = "disabled" } }
					},

					ApiSettingType.Ollama => throw new NotImplementedException("Ollama support not implemented yet."),

					_ => throw new InvalidOperationException($"Unsupported API type: {Type}")
				};
			}
		}

		public ApiSetting Clone()
		{
			return new ApiSetting
			{
				Name = Name,
				Type = Type,

				KoboldUrl = KoboldUrl,
				KoboldConf = KoboldConf?.Clone() as KoboldAPI.KoboldAPIConf,

				OpenAIUrl = OpenAIUrl,
				OpenAIApiKey = OpenAIApiKey,
				OpenAIModel = OpenAIModel,
				OpenAIStream = OpenAIStream,
				OpenAICtx = OpenAICtx,
				OpenAIEnableContinuation = OpenAIEnableContinuation,
				OpenAIAsIs = OpenAIAsIs,

				DeepSeekUrl = DeepSeekUrl,
				DeepSeekApiKey = DeepSeekApiKey,
				DeepSeekModel = DeepSeekModel,
				DeepSeekStream = DeepSeekStream,
				DeepSeekCtx = DeepSeekCtx,
				DeepSeekEnableContinuation = DeepSeekEnableContinuation,
				DeepSeekAsIs = DeepSeekAsIs,
				DeepSeekEnableThinking = DeepSeekEnableThinking,

				OllamaUrl = OllamaUrl,
				OllamaModel = OllamaModel,

				CompletionType = CompletionType,
				TextCompletionSettings = TextCompletionSettings?.Select(tcs => tcs.Clone()).ToList(),
				SelectedTextCompletionSettingIndex = SelectedTextCompletionSettingIndex
			};
		}
	}

	public enum ApiSettingType
	{
		Kobold,
		Ollama,
		OpenAI,
		DeepSeek
	}
}