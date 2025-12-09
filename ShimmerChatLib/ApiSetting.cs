using SharperLLM.API;
using System.Text.Json.Serialization;

namespace ShimmerChatLib
{
	public class ApiSetting
	{
		// 不再持有运行时对象
		public string Name { get; set; } = string.Empty;

		[JsonConverter(typeof(JsonStringEnumConverter))]
		public ApiSettingType Type { get; set; }

		// Kobold 配置（仅当 Type == Kobold 时有效）
		public string? KoboldUrl { get; set; } = "http://localhost:5001/api";
		public KoboldAPI.KoboldAPIConf? KoboldConf { get; set; } = new();

		// OpenAI 配置（仅当 Type == OpenAI 时有效）
		public string? OpenAIUrl { get; set; } = "http://localhost:5001/api/v1";
		public string? OpenAIApiKey { get; set; } = "key";
		public string? OpenAIModel { get; set; } = "gpt-4o";
		public bool OpenAIStream { get; set; } = true;
		public long OpenAICtx { get; set; } = 16384;

		// Ollama 配置（可后续扩展）
		public string? OllamaUrl { get; set; }
		public string? OllamaModel { get; set; }

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
						_model: OpenAIModel ?? "gpt-4o"
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

				OllamaUrl = OllamaUrl,
				OllamaModel = OllamaModel
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