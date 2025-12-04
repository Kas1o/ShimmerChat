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
		public required string Name {
			get => field;
			set
			{
				field = value;
				// Set the dirty flag to true when the name is changed
				dirty = true;
			}
		} // The name of the chat
		public Guid Guid { get; set; } // The unique identifier of the chat
		public ObservableCollection<Message> Messages { get;set; } // List of messages in the chat

		public Chat()
		{
			Messages = new ObservableCollection<Message>();
			Messages.CollectionChanged += (s, e) =>
			{
				// Set the dirty flag to true when messages are added or removed
				dirty = true;
			};
			Guid = Guid.NewGuid();
		}

		public void AddMessage(Message message)
		{
			Messages.Add(message);
		}
	}
}
