using Microsoft.Extensions.Logging;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using SharperLLM.Util;

namespace ShimmerChat.Singletons
{
	/// <summary>
	/// 创建对话的统一实现。首页的“快速新建聊天”和 Agent 页面的“新建对话”
	/// 都走这里，保证问候语、顺序与持久化行为一致。
	/// </summary>
	public sealed class ChatCreationService : IChatCreationService
	{
		private readonly IKVDataService _kvData;
		private readonly IMessageStoreService _messageStore;
		private readonly ILogger<ChatCreationService> _logger;

		public ChatCreationService(
			IKVDataService kvData,
			IMessageStoreService messageStore,
			ILogger<ChatCreationService> logger)
		{
			_kvData = kvData;
			_messageStore = messageStore;
			_logger = logger;
		}

		public Task<ChatCreationResult> CreateChatAsync(Guid agentGuid, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (agentGuid == Guid.Empty)
			{
				_logger.LogError("[ChatCreation] Rejected an empty agent guid.");
				return Task.FromResult(new ChatCreationResult(
					false, Guid.Empty, ChatCreationErrorCodes.InvalidAgent, "Agent GUID is empty."));
			}

			Agent agent;
			try
			{
				agent = Agent.Load(agentGuid, _kvData);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[ChatCreation] Failed to load agent {AgentGuid}.", agentGuid);
				return Task.FromResult(new ChatCreationResult(
					false, Guid.Empty, ChatCreationErrorCodes.AgentNotFound, ex.Message));
			}

			try
			{
				var now = DateTime.Now;
				var chat = new Chat
				{
					Guid = Guid.NewGuid(),
					Name = now.ToString().Replace(" ", "-").Replace("/", "-").Replace(":", "-"),
					CreateTime = now,
					LastModifyTime = now
				};

				AddGreetingMessages(agent, chat, now);

				chat.Save(_kvData);
				Agent.Update(agentGuid, _kvData, a => a.AddChatGuid(chat.Guid));

				return Task.FromResult(new ChatCreationResult(true, chat.Guid));
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[ChatCreation] Failed to create a chat for agent {AgentGuid}.", agentGuid);
				return Task.FromResult(new ChatCreationResult(
					false, Guid.Empty, ChatCreationErrorCodes.StorageFailure, ex.Message));
			}
		}

		/// <summary>
		/// 发送 Agent 的问候语（含备选版本）。没有配置问候语时不写任何消息。
		/// </summary>
		private void AddGreetingMessages(Agent agent, Chat chat, DateTime now)
		{
			if (string.IsNullOrEmpty(agent.Greeting) && (agent.AlternativeGreetings == null || agent.AlternativeGreetings.Count == 0))
				return;

			var welcomeMessage = new Message
			{
				sender = Sender.AI,
				timestamp = now,
				message = new ChatMessage
				{
					Content = agent.Greeting ?? ""
				}
			};

			if (agent.AlternativeGreetings != null)
			{
				foreach (var altGreeting in agent.AlternativeGreetings)
				{
					if (!string.IsNullOrEmpty(altGreeting) && altGreeting != agent.Greeting)
					{
						welcomeMessage.AddVersion(new ChatMessage { Content = altGreeting });
					}
				}
			}

			welcomeMessage.CurrentVersionIndex = 0;
			chat.AddMessage(welcomeMessage);
			chat.SaveMessage(_messageStore, welcomeMessage);
		}
	}
}
