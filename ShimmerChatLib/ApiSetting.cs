﻿﻿﻿﻿﻿using SharperLLM.API;
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

		#region Ollama
		public string? OllamaUrl { get; set; }
		public string? OllamaModel { get; set; }

		#endregion

		// Completion settings
		public CompletionType CompletionType { get; set; } = CompletionType.ChatCompletion;
		public List<TextCompletionSetting>? TextCompletionSettings { get; set; } = new List<TextCompletionSetting>();
		public int SelectedTextCompletionSettingIndex { get; set; } = 0;

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
		OpenAI
	}
}