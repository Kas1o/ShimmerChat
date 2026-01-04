using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatLib.Panel
{
	public interface IChatPanelEventHandler
	{
		void OnToolCallResult(Message message);
		void OnUserMessage(Message message);
		void OnAgentMessage(Message message);
	}
}
