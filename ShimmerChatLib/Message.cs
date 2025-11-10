using SharperLLM.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShimmerChatLib
{
	public class Message
    {
        public required ChatMessage message { get; set; } // The message object containing the content and image
        public string thinking { get; set; } = null;
        public required DateTime timestamp { get; set; } // The timestamp of the message
        public required string sender { get; set; } // The sender of the message
	}
}
