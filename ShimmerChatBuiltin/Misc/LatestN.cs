using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.Misc
{
	public class LatestN : IContextModifier
	{
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = nameof(LatestN),
			Description = "Latest N messages, input N or !N, exclamation mark will remove first system message."
		};

		public void ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
		{
			// 默认不包含初始系统提示
			bool keepFirstSystem = true;
			var backup = promptBuilder.Messages.First();

			// 如果 输入 以 叹号开始则包含并去除叹号
			if (input.StartsWith('!'))
			{
				keepFirstSystem = false;
				input = input.Substring(1); // Use Substring instead of Remove for strings
			}

			// 再校验是否以 System 开头
			keepFirstSystem &= promptBuilder.Messages.First().Item2 == PromptBuilder.From.system;

			// 验证输入
			var n = int.Parse(input);
			if(n <= 0)
			{
				throw new Exception("LatestN Input Err, number should be > 0");
			}
			if(n >= promptBuilder.Messages.Count())
			{
				return;
			}

			// 获取要保留的消息
			var list = promptBuilder.Messages.TakeLast(n).ToList();

			// 添加回列表
			if (keepFirstSystem)
			{
				list.Insert(0, backup);
			}

			// 确保移除未配对的 ToolCallResult 消息
			// 首先找到被移除的消息中包含哪些 ToolCall（通过消息 ID 识别）
			var messagesToKeep = list.ToHashSet();
			var allMessages = promptBuilder.Messages;
			
			// 找出被移除的消息
			var removedMessages = allMessages.Except(messagesToKeep).ToList();

			// 收集被移除的 ToolCall 的 ID
			var removedToolCallIds = new HashSet<string>();
			foreach (var (message, from) in removedMessages)
			{
				if (from == PromptBuilder.From.assistant && message.toolCalls != null && message.toolCalls.Count > 0)
				{
					// 收集被移除的 ToolCall 的 ID
					foreach (var toolCall in message.toolCalls)
					{
						if (!string.IsNullOrEmpty(toolCall.id))
						{
							removedToolCallIds.Add(toolCall.id);
						}
					}
				}
			}

			// 过滤列表，移除未配对的 ToolCallResult 消息
			var finalList = new List<(ChatMessage, PromptBuilder.From)>();
			foreach (var (message, from) in list)
			{
				// 如果是 ToolCallResult 消息，且其对应的 ToolCall 已被移除，则跳过此消息
				if (from == PromptBuilder.From.tool_result)
				{
					// 检查该 ToolCallResult 是否对应一个已被移除的 ToolCall
					// ToolCallResult 的 message.id 通常对应 ToolCall 的 id
					if (string.IsNullOrEmpty(message.id) || !removedToolCallIds.Contains(message.id))
					{
						// 只有当 message.id 不为空且不在被移除的 ToolCall ID 列表中时，才保留
						finalList.Add((message, from));
					}
					// 否则（message.id 在被移除的列表中），跳过此消息（不添加到 finalList）
				}
				else
				{
					// 非 ToolCallResult 消息直接添加
					finalList.Add((message, from));
				}
			}

			promptBuilder.Messages = finalList.ToArray();
		}
	}
}
