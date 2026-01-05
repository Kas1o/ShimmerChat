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
			bool includeFirstSystem = false;
			var backup = promptBuilder.Messages.First();

			// 如果 输入 以 叹号开始则包含并去除叹号
			if (input.StartsWith('!'))
			{
				includeFirstSystem = true;
				input.Remove(0,1);
			}

			// 再校验是否以 System 开头
			includeFirstSystem &= promptBuilder.Messages.First().Item2 == PromptBuilder.From.system;

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

			// 取 LastN
			promptBuilder.Messages =  promptBuilder.Messages.TakeLast(n).ToArray();

			// 添加回列表
			if (includeFirstSystem)
			{
				promptBuilder.Messages = promptBuilder.Messages.Append(backup).ToArray();
			}
		}
	}
}
