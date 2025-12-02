using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatBuiltin.DynPrompt;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace ShimmerChatBuiltin.ContextModifiers
{
	public class DynPrompt : IContextModifier
	{
		IKVDataService pluginData;
		public DynPrompt(IKVDataService pluginDataService)
		{
			this.pluginData = pluginDataService;
		}

		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = "DynPrompt",
			Description = "Inject prompt dynamically based on rules, input the set name."
		};

		public void ModifyContext(PromptBuilder promptBuilder, string input)
		{
			var data = pluginData.Read("DynPrompt", "DynPromptSets");
			var sets = JsonConvert.DeserializeObject<List<DynPromptSet>>(data ?? "[]") ?? [];

			var set = sets.FindLast(s => s.Name == input);
			if(set == null)
				throw new InvalidOperationException($"No DynPromptSet found with name '{input}'");

			// 收集所有消息内容用于规则评估
			string contextText = CollectContextText(promptBuilder);

			// 处理每个动态提示项
			foreach (var term in set.Terms)
			{
				// 如果没有触发规则或者规则评估为true，则注入内容
				if (string.IsNullOrEmpty(term.TriggerRule) || EvaluateTriggerRule(term.TriggerRule, contextText))
				{
					InjectTerm(promptBuilder, term);
				}
			}
		}

		/// <summary>
		/// 收集上下文中的所有文本内容
		/// </summary>
		private string CollectContextText(PromptBuilder promptBuilder)
		{
			var sb = new StringBuilder();
			
			// 添加系统提示
			if (!string.IsNullOrEmpty(promptBuilder.System))
			{
				sb.Append(promptBuilder.System);
				sb.Append(Environment.NewLine);
			}
			
			// 添加所有消息
			foreach (var (message, _) in promptBuilder.Messages)
			{
				sb.Append(message.Content);
				sb.Append(Environment.NewLine);
			}
			
			return sb.ToString();
		}

		/// <summary>
		/// 评估触发规则
		/// </summary>
		private bool EvaluateTriggerRule(string rule, string contextText)
		{
			try
			{
				// 使用新的DynPromptEvaluator评估表达式
				return DynPromptEvaluator.Evaluate(rule, contextText);
			}
			catch (Exception ex)
			{
				// 规则解析错误时默认不触发
				Console.WriteLine($"Error evaluating trigger rule: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// 根据注入模式注入内容
		/// </summary>
		private void InjectTerm(PromptBuilder promptBuilder, DynPromptTerm term)
		{
			var newMessage = new ChatMessage { Content = term.Content };
			
			// 获取消息列表
			List<(ChatMessage, PromptBuilder.From)> messages = promptBuilder.Messages.ToList();
			int systemMessageIndex;
			
			switch (term.InjectionMode)
			{
				case DynPromptTermInjectionMode.BeforeSystem:
					// 优先查找第一个role为system的消息
					systemMessageIndex = messages.FindIndex(m => m.Item2 == PromptBuilder.From.system);
					if (systemMessageIndex >= 0)
					{
						// 修改第一个system消息的内容，在现有内容前添加新内容
						var (message, from) = messages[systemMessageIndex];
						string newContent = term.Content + Environment.NewLine + message.Content;
						messages[systemMessageIndex] = (newContent, from);
						promptBuilder.Messages = messages.ToArray();
					}
					else
					{
						// 如果没有system消息，则操作System字段
						string oldSystem = promptBuilder.System;
						promptBuilder.System = term.Content;
						if (!string.IsNullOrEmpty(oldSystem))
						{
							promptBuilder.System += Environment.NewLine + oldSystem;
						}
					}
					break;
				case DynPromptTermInjectionMode.AfterSystem:
					// 优先查找第一个role为system的消息
					systemMessageIndex = messages.FindIndex(m => m.Item2 == PromptBuilder.From.system);
					if (systemMessageIndex >= 0)
					{
						// 在第一个system消息后追加内容
						var (message, from) = messages[systemMessageIndex];
						string newContent = message.Content + Environment.NewLine + term.Content;
						messages[systemMessageIndex] = (newContent, from);
						promptBuilder.Messages = messages.ToArray();
					}
					else
					{
						// 如果没有system消息，则操作System字段
						if (!string.IsNullOrEmpty(promptBuilder.System))
						{
							promptBuilder.System += Environment.NewLine;
						}
						promptBuilder.System += term.Content;
					}
					break;
				case DynPromptTermInjectionMode.AtDepth:
					// 在指定深度添加内容
					InjectAtDepth(promptBuilder, term);
					break;
			}
		}

		/// <summary>
		/// 在指定深度注入内容
		/// </summary>
		private void InjectAtDepth(PromptBuilder promptBuilder, DynPromptTerm term)
		{
			List<(ChatMessage, PromptBuilder.From)> messages = promptBuilder.Messages.ToList();
			int injectionDepth = term.InjectionDepth;
			
			// 处理负索引逻辑：当depth为负数时从末尾反着数
			if (injectionDepth < 0)
			{
				// 计算实际索引：从末尾开始，-1表示最后一条消息后，-2表示倒数第二条消息后，以此类推
				injectionDepth = Math.Max(0, messages.Count + 1 + injectionDepth);
			}
			
			// 如果深度超过消息数量，则添加到末尾
			if (injectionDepth >= messages.Count)
			{
				messages.Add((term.Content, PromptBuilder.From.system));
			}
			else
			{
				// 在指定深度插入内容
				messages.Insert(injectionDepth, (term.Content, PromptBuilder.From.system));
			}
			
			promptBuilder.Messages = messages.ToArray();
		}
	}
}