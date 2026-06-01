using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;
using Tokenizers.HuggingFace.Tokenizer;

namespace ShimmerChatBuiltin.Misc
{
	public class TokenLimitConfig : ModifierConfig
	{
		[UiHint("Token 预算", "超过此数量将从最早的消息开始裁剪")]
		public int TokenBudget { get; set; } = 4096;
	}

	public class TokenLimit : IContextModifier
	{
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = "Token Limit",
			Description = "Trim old messages when total token count exceeds the budget."
		};

		private IKVDataService _kvDataService;
		public TokenLimit(IKVDataService kvDataService)
		{
			_kvDataService = kvDataService;
		}

		private Tokenizer _tokenizer;
		private string? _currentVocab;

		public void TryRecreateTokenizer()
		{
			var vocab = _kvDataService.Read("Tokenize", "local_vocab_path");
			if (string.IsNullOrEmpty(vocab))
			{
				throw new ArgumentException("Vocab path is empty");
			}
			if (_currentVocab != vocab)
			{
				_tokenizer = Tokenizer.FromFile(vocab);
				_currentVocab = vocab;
			}
		}

		public Type ConfigType => typeof(TokenLimitConfig);

		public (bool IsValid, string Error) Validate(ModifierConfig config)
		{
			var cfg = (TokenLimitConfig)config;
			if (cfg.TokenBudget <= 0)
				return (false, "Token budget must be greater than 0");
			return (true, "");
		}

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			var cfg = (TokenLimitConfig)config;
			var tokenBudget = cfg.TokenBudget;

			TryRecreateTokenizer();

			var segments = context.Segments;
			var tokenCounts = segments.Select(s => _tokenizer.Encode(s.Message.Content, true).FirstOrDefault()?.Ids?.Count ?? 0).ToList();

			Console.WriteLine($"Token count before limit: {tokenCounts.Sum()}");

			var totalTokens = 0;
			for (int i = tokenCounts.Count - 1; i >= 0; i--)
			{
				totalTokens += tokenCounts[i];
				if (totalTokens > tokenBudget)
				{
					segments.RemoveAt(i);
					tokenCounts.RemoveAt(i);
				}
			}

			List<string> Ids = new();
			foreach (var segment in segments)
			{
				if (segment.From == PromptBuilder.From.assistant)
				{
					foreach (var toolCall in segment.Message.toolCalls ?? new())
					{
						Ids.Add(toolCall.id);
					}
				}
			}

			var toRemove = segments
				.Where(s => s.From == PromptBuilder.From.tool_result && !Ids.Contains(s.Message.id!))
				.ToList();

			foreach (var r in toRemove)
				segments.Remove(r);

			tokenCounts = segments.Select(s => _tokenizer.Encode(s.Message.Content, true).FirstOrDefault()?.Ids?.Count ?? 0).ToList();
			Console.WriteLine($"Token count after limit: {tokenCounts.Sum()}");
		}

	}
}
