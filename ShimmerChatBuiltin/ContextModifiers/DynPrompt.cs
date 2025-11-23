using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatBuiltin.DynPrompt;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;

namespace ShimmerChatBuiltin.ContextModifiers
{
	public class DynPrompt : IContextModifier
	{
		IPluginDataService pluginData;
		public DynPrompt(IPluginDataService pluginDataService)
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
				// 简单的解析器实现，处理基本的逻辑运算
				// 实际项目中可能需要更复杂的解析器
				return EvaluateExpression(rule, contextText);
			}
			catch (Exception ex)
			{
				// 规则解析错误时默认不触发
				Console.WriteLine($"Error evaluating trigger rule: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// 解析和评估表达式
		/// </summary>
		private bool EvaluateExpression(string expression, string contextText)
		{
			// 去除首尾空白
			expression = expression.Trim();

			// 处理括号
			if (expression.StartsWith("(") && expression.EndsWith(")"))
			{
				// 检查括号是否匹配
				int bracketCount = 0;
				bool isBalanced = true;
				for (int i = 0; i < expression.Length; i++)
				{
					if (expression[i] == '(')
						bracketCount++;
					else if (expression[i] == ')')
					{
						bracketCount--;
						if (bracketCount < 0)
						{
							isBalanced = false;
							break;
						}
					}
				}

				if (!isBalanced || bracketCount != 0)
					throw new ArgumentException("Unbalanced parentheses in expression.");

				// 检查是否是最外层括号（不被其他括号包裹）
				int innerBracketCount = 0;
				bool isOuterBrackets = true;
				for (int i = 1; i < expression.Length - 1; i++)
				{
					if (expression[i] == '(')
						innerBracketCount++;
					else if (expression[i] == ')')
						innerBracketCount--;
					
					// 如果在内部遇到了不平衡的括号，说明这不是最外层括号
					if (innerBracketCount < 0)
					{
						isOuterBrackets = false;
						break;
					}
				}

				if (isOuterBrackets)
				{
					return EvaluateExpression(expression.Substring(1, expression.Length - 2), contextText);
				}
			}

			// 处理 NOT 操作符
			if (expression.StartsWith("!"))
			{
				return !EvaluateExpression(expression.Substring(1).Trim(), contextText);
			}

			// 处理 OR 操作符
			int orIndex = FindOperatorIndex(expression, '|');
			if (orIndex >= 0)
			{
				string left = expression.Substring(0, orIndex).Trim();
				string right = expression.Substring(orIndex + 1).Trim();
				return EvaluateExpression(left, contextText) || EvaluateExpression(right, contextText);
			}

			// 处理 AND 操作符
			int andIndex = FindOperatorIndex(expression, '&');
			if (andIndex >= 0)
			{
				string left = expression.Substring(0, andIndex).Trim();
				string right = expression.Substring(andIndex + 1).Trim();
				return EvaluateExpression(left, contextText) && EvaluateExpression(right, contextText);
			}

			// 处理正则表达式模式（应该是被引号包围的字符串）
			if (expression.StartsWith('"') && expression.EndsWith('"'))
			{
				// 移除引号并处理转义字符
				string pattern = expression.Substring(1, expression.Length - 2);
				// 替换 JSON 转义字符
				pattern = pattern.Replace("\\\"", "\"").Replace("\\\\", "\\");
				
				try
				{
					return Regex.IsMatch(contextText, pattern, RegexOptions.IgnoreCase);
				}
				catch (ArgumentException)
				{
					// 正则表达式无效时默认返回 false
					return false;
				}
			}

			// 默认返回 false
			return false;
		}

		/// <summary>
		/// 在表达式中查找操作符的索引（考虑括号嵌套）
		/// </summary>
		private int FindOperatorIndex(string expression, char op)
		{
			int bracketCount = 0;
			for (int i = 0; i < expression.Length; i++)
			{
				if (expression[i] == '(')
					bracketCount++;
				else if (expression[i] == ')')
					bracketCount--;
				else if (bracketCount == 0 && expression[i] == op)
					return i;
			}
			return -1;
		}

		/// <summary>
		/// 根据注入模式注入内容
		/// </summary>
		private void InjectTerm(PromptBuilder promptBuilder, DynPromptTerm term)
		{
			var newMessage = new ChatMessage(term.Content, null);
			
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
						messages[systemMessageIndex] = (new ChatMessage(newContent, null), from);
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
						messages[systemMessageIndex] = (new ChatMessage(newContent, null), from);
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
