using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShimmerChatLib
{
    public class Chat
    {
		public bool dirty = true;
		public string Name {
			get => field;
			set
			{
				field = value;
				// Set the dirty flag to true when the name is changed
				dirty = true;
			}
		} // The name of the chat
		public ObservableCollection<Message> Messages { get;set; } // List of messages in the chat
		[JsonConstructor]
		public Chat(string name)
		{
			Name = name;
			Messages = new();
			Messages.CollectionChanged += (s, e) =>
			{
				// Set the dirty flag to true when messages are added or removed
				dirty = true;
			};
		}
		public Chat(string name, string greeting)
		{
			Name = name;
			Messages = new ();
			if (!string.IsNullOrEmpty(greeting))
			{
				Messages.Add(new Message { message = greeting, sender = "ai", timestamp = DateTime.Now});
			}

			Messages.CollectionChanged += (s,e) =>
			{
				// Set the dirty flag to true when messages are added or removed
				dirty = true;
			};
		}
		public void AddMessage(Message message)
		{
			Messages.Add(message);
		}
	}
}
