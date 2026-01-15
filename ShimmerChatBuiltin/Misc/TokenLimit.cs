using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;
using System;
using System.Collections.Generic;
using System.Text;
using Tokenizers.HuggingFace.Tokenizer;

namespace ShimmerChatBuiltin.Misc
{
	public class TokenLimit : IContextModifier
	{
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = "Token Limit",
			Description = "input the token budget(int), remove old messages if exceed"
		};

		private IKVDataService _kvDataService;
		public TokenLimit(IKVDataService kvDataService)
		{
			_kvDataService = kvDataService;
		}

		private Tokenizer _tokenizer;	
		private string _currentVocab;

		public void TryRecreateTokenizer(){
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

		public void ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
		{
			var tokenBudget = 0;
			if (!int.TryParse(input, out tokenBudget))
			{
				throw new ArgumentException("Token budget must be an integer");
			}
			TryRecreateTokenizer();

			var tokenCounts = promptBuilder.Messages.Select(m => _tokenizer.Encode(m.Item1.Content,true).FirstOrDefault()?.Ids.Count).ToList();

			// 打印缩减前的token数量
			Console.WriteLine($"Token count before limit: {tokenCounts.Sum()}");
			
			// 从最后一项往前累加，直到超过tokenBudget
			var totalTokens = 0;
			var messages = promptBuilder.Messages.ToList();
			for (int i = tokenCounts.Count - 1; i >= 0; i--)
			{
				totalTokens += tokenCounts[i].Value;
				if (totalTokens > tokenBudget)
				{
					// 移除超过budget的项
					messages.RemoveAt(i);
					tokenCounts.RemoveAt(i);
				}
			}

			// 筛选出所有没有对应tool call的tool result
			List<string> Ids = [];
			foreach(var message in messages)
			{
				if (message.Item2 == PromptBuilder.From.assistant)
				{
					foreach(var toolCall in message.Item1.toolCalls ?? [])
					{
						Ids.Add(toolCall.id);
					}
				}
			}

			// 移除所有没有对应tool call的tool result
			foreach(var message in messages.ToList())
			{
				if (message.Item2 == PromptBuilder.From.tool_result && !Ids.Contains(message.Item1.id!))
				{
					messages.Remove(message);
				}
			}
			
			// 打印缩减后的token数量
			tokenCounts = messages.Select(m => _tokenizer.Encode(m.Item1.Content,true).FirstOrDefault()?.Ids.Count).ToList();
			Console.WriteLine($"Token count after limit: {tokenCounts.Sum()}");
			
			promptBuilder.Messages = messages.ToArray();
		}
	}
}
