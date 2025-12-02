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
        private ChatMessage _message;
        public required ChatMessage message 
        {
            get => _message;
            set 
            {
                string oldContent = _message?.Content ?? string.Empty;
                _message = value;
                // 如果内容发生变化，触发事件
                if (oldContent != value?.Content && ContentChanged != null)
                {
                    ContentChanged(this, EventArgs.Empty);
                }
            }
        }
        
        public required DateTime timestamp { get; set; } // The timestamp of the message
        public required string sender { get; set; } // The sender of the message
        
        // 流式状态属性
        private bool _isStreaming;
        public bool IsStreaming 
        {
            get => _isStreaming;
            set 
            {
                _isStreaming = value;
                if (StreamingStateChanged != null)
                {
                    StreamingStateChanged(this, EventArgs.Empty);
                }
            }
        }
        
        // 内容变化事件
        public event EventHandler ContentChanged;
        
        // 流式状态变化事件
        public event EventHandler StreamingStateChanged;
	}
}
