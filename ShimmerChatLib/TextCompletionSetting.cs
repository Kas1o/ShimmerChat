using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShimmerChatLib
{
    public class TextCompletionSetting
    {
        public string name { get; set; }
        public string SystemMessageTemplate { get; set; }
        public string UserMessageTemplate { get; set; }
        public string CharMessageTemplate { get; set; }

        public TextCompletionSetting()
        {
            // used by json deserializer.
        }

        public TextCompletionSetting(string name,string systemMessageTemplate, string userMessageTemplate, string charMessageTemplate)
        {
            this.name = name;
            this.UserMessageTemplate = userMessageTemplate;
            this.CharMessageTemplate = charMessageTemplate;
            this.SystemMessageTemplate = systemMessageTemplate;
        }

        public (string sys_start, string sys_stop, string user_start, string user_stop, string char_start, string char_stop) GetMessageTemplates()
		{
			var sys_start = SystemMessageTemplate.Split("<|>")[0];
			var sys_stop = SystemMessageTemplate.Split("<|>")[1];
			var user_start = UserMessageTemplate.Split("<|>")[0];
			var user_stop = UserMessageTemplate.Split("<|>")[1];
			var char_start = CharMessageTemplate.Split("<|>")[0];
			var char_stop = CharMessageTemplate.Split("<|>")[1];

            return (sys_start, sys_stop, user_start, user_stop, char_start, char_stop);
		}
	}
}
