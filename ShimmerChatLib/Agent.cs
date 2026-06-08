using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChatLib
{
    /// <summary>
    /// 关于智能体定义的主对象
    /// </summary>
    public class Agent
    {
        /// <summary>
        /// 智能体GUID
        /// </summary>
		public Guid Guid { get; set; }
        /// <summary>
        /// 智能体名称，用于显示
        /// </summary>
		public string Name { get; set; }
        /// <summary>
        /// 描述，SystemPrompt组织中的主要部分
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// Greeting，新对话时以AI Role发送的第一天Message，可为空
        /// </summary>
		public string? Greeting { get; set; }
        /// <summary>
        /// 其他Greeting，新对话时添加至 <see cref="Message.Versions"/>
        /// </summary>
		public List<string> AlternativeGreetings { get; set; } = new List<string>();
        /// <summary>
        /// 在此智能体进行的对话的Guids
        /// </summary>
		public List<Guid> ChatGuids
        {
            get => field;
            set
            {
                field = value;
            }
        }
        /// <summary>
        /// 头像的Guid
        /// </summary>
		public Guid? AvatarGuid { get; set; }
        /// <summary>
        /// 背景图的Guid
        /// </summary>
        public Guid? BackgroundGuid { get; set; }
        /// <summary>
        /// 此智能体所自定义的工具名，使用时和主配置求并集。
        /// </summary>
        public List<string> CustomToolNames { get; set; } = new List<string>();
        /// <summary>
        /// 仅用户可见的介绍文本
        /// </summary>
		public string UserIntro { get; set; } = "";
        /// <summary>
        /// Tag，辅助检索用
        /// </summary>
		public List<string> Tags { get; set; } = new List<string>();

		#region Export & Import
        /// <summary>
        /// 导出智能体，顺便打包对应的背景图头像
        /// </summary>
        /// <param name="clearChat">清除对话后导出</param>
        /// <returns>导出智能体格式的JSON</returns>
        public string Export(bool clearChat = true)
        {
            var copy = this.MemberwiseClone() as Agent;
            if (clearChat)
                copy!.ChatGuids = [];

            string? avatar = null;
            string? background = null;
			string avatarPath = $"{AppContext.BaseDirectory}/UserUploadImage/{this.AvatarGuid}.png";
			string backgroundPath = $"{AppContext.BaseDirectory}/UserUploadImage/{this.BackgroundGuid}.png";

			if (AvatarGuid != null)
            if (File.Exists(avatarPath))
            {
                var content = File.ReadAllBytes(avatarPath);
                avatar = Convert.ToBase64String(content);
            }

			if (BackgroundGuid != null)
			if (File.Exists(backgroundPath))
			{
				var content = File.ReadAllBytes(backgroundPath);
				background = Convert.ToBase64String(content);
			}


            return JsonConvert.SerializeObject(new AgentExportStructure
            (
                Agent : copy!,
                AvatarBase64 : avatar!,
                BackgroundBase64 : background!
            ), new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include,
            });
        }

        /// <summary>
        /// 导入智能体，顺便导入对应的背景头像（如果包含了）
        /// </summary>
        /// <param name="importJson">输入的JSON</param>
        /// <param name="clearChat">导入时清除对话数据</param>
        /// <returns>Agent 对象</returns>
        public static Agent Import(string importJson, bool clearChat = true)
        {
            var importStructure = JsonConvert.DeserializeObject<AgentExportStructure>(importJson);

            if (importStructure?.Agent == null)
            {
                throw new InvalidOperationException($"Agent Import Failed, Invalid JSON.");
            }
            
            var agent = importStructure.Agent;
            if (clearChat)
            {
                agent.ChatGuids = [];
            }
            if (!string.IsNullOrEmpty(importStructure.AvatarBase64))
            {
                var avatarBytes = Convert.FromBase64String(importStructure.AvatarBase64);
                var avatarGuid = Guid.NewGuid();
                var avatarPath = $"{AppContext.BaseDirectory}/UserUploadImage/{avatarGuid}.png";
                File.WriteAllBytes(avatarPath, avatarBytes);
                agent.AvatarGuid = avatarGuid;
            }
            if (!string.IsNullOrEmpty(importStructure.BackgroundBase64))
            {
                var backgroundBytes = Convert.FromBase64String(importStructure.BackgroundBase64);
                var backgroundGuid = Guid.NewGuid();
                var backgroundPath = $"{AppContext.BaseDirectory}/UserUploadImage/{backgroundGuid}.png";
                File.WriteAllBytes(backgroundPath, backgroundBytes);
                agent.BackgroundGuid = backgroundGuid;
            }
            return agent;
		}
		#endregion
		#region Save & Load
        /// <summary>
        /// 保存Agent定义（不包括每个对话的数据）
        /// </summary>
        /// <param name="kvDataService"></param>
		public void Save(IKVDataService kvDataService)
        {
            var agentJson = JsonConvert.SerializeObject(this);
            kvDataService.Write("Agents", Guid.ToString(), agentJson);
        }

        /// <summary>
        /// 加载Agent 定义
        /// </summary>
        /// <param name="guid">需要加载的Agent 的 GUID </param>
        /// <param name="kvDataService"></param>
        /// <returns>Agent对象</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static Agent Load(Guid guid, IKVDataService kvDataService)
        {
            var agentJson = kvDataService.Read("Agents", guid.ToString());
            if (agentJson == null)
            {
                throw new InvalidOperationException($"Agent with GUID '{guid}' not found.");
            }
            return JsonConvert.DeserializeObject<Agent>(agentJson) ?? throw new InvalidOperationException($"Agent with GUID '{guid}' not found.");
        }
		#endregion
		#region Statics
        /// <summary>
        /// 获得所有的Agent的 GUIDs （适用于获取所有Agent时）
        /// </summary>
        /// <param name="kvDataService"></param>
        /// <returns>所有Agent 的 GUIDs 的列表</returns>
        /// <exception cref="IOException"/>
		public static List<Guid> GetAllAgentGuids(IKVDataService kvDataService)
        {
            var agentsJson = kvDataService.Read("Agents", "__AllAgents__");
            if (agentsJson == null)
            {
                return new List<Guid>();
            }
            return JsonConvert.DeserializeObject<List<Guid>>(agentsJson) ?? throw new IOException("无法正常加载所有Agent的GUIDs的数据");
        }

        /// <summary>
        /// 保存所有Agent的GUIDs列表 （新增、删除Agent时）
        /// </summary>
        /// <param name="kvDataService"></param>
        /// <param name="agentGuids"></param>
        private static void SaveAllAgentGuids(IKVDataService kvDataService, List<Guid> agentGuids)
        {
            var agentsJson = JsonConvert.SerializeObject(agentGuids);
            kvDataService.Write("Agents", "__AllAgents__", agentsJson);
        }

        /// <summary>
        /// 添加一个新的Agent GUID 到所有Agent GUIDs 的列表。（典型场景：新建Agent）
        /// </summary>
        /// <param name="agent"></param>
        /// <param name="kvDataService"></param>
        public static void AddAgentToAll(Agent agent, IKVDataService kvDataService)
        {
            var agentGuids = GetAllAgentGuids(kvDataService);
            if (!agentGuids.Contains(agent.Guid))
            {
                agentGuids.Add(agent.Guid);
                SaveAllAgentGuids(kvDataService, agentGuids);
            }
        }

        /// <summary>
        /// 从所有Agent GUIDs 的列表中删除一个Agent GUID （典型场景：删除Agent）
        /// </summary>
        /// <param name="agent"></param>
        /// <param name="kvDataService"></param>
        public static void RemoveAgentFromAll(Agent agent, IKVDataService kvDataService)
        {
            var agentGuids = GetAllAgentGuids(kvDataService);
            if (agentGuids.Contains(agent.Guid))
            {
                agentGuids.Remove(agent.Guid);
                SaveAllAgentGuids(kvDataService, agentGuids);
            }
        }
		#endregion
		#region ChatUtil
        /// <summary>
        /// 向此智能体添加一个对话，新对话插入到列表开头（最新的在前）
        /// </summary>
        /// <param name="chatGuid">要添加的对话 GUID</param>
        public void AddChatGuid(Guid chatGuid)
        {
            if (!ChatGuids.Contains(chatGuid))
            {
                ChatGuids.Insert(0, chatGuid);
            }
        }

        /// <summary>
        /// 从此智能体中移除一个对话
        /// </summary>
        /// <param name="chatGuid">要移除的对话 GUID</param>
        public void RemoveChatGuid(Guid chatGuid)
        {
            ChatGuids.Remove(chatGuid);
        }

        /// <summary>
        /// 将指定聊天移动到列表开头（表示最新更新）
        /// </summary>
        public void MoveChatToTop(Guid chatGuid)
        {
            if (ChatGuids.Contains(chatGuid))
            {
                ChatGuids.Remove(chatGuid);
                ChatGuids.Insert(0, chatGuid);
            }
        }

        /// <summary>
        /// 获取此智能体下的所有对话
        /// </summary>
        /// <param name="kvDataService">持久化数据服务</param>
        /// <returns>对话列表</returns>
        public List<Chat> GetChats(IKVDataService kvDataService)
        {
            var chats = new List<Chat>();
            foreach (var chatGuid in ChatGuids)
            {
                try
                {
                    var chat = Chat.Load(chatGuid, kvDataService);
                    chats.Add(chat);
                }
                catch (Exception ex)
                {
                    // Handle exception if chat fails to load
                    Console.WriteLine($"Failed to load chat with GUID '{chatGuid}': {ex.Message}");
                }
            }
            return chats;
        }

        /// <summary>
        /// 获取指定范围的聊天列表
        /// chatGuids 已经按 LastModifyTime 降序排列，直接按索引范围返回
        /// </summary>
        public List<Chat> GetChatsRange(IKVDataService kvDataService, int startIndex, int count)
        {
            var rangeGuids = ChatGuids
                .Skip(startIndex)
                .Take(count)
                .ToList();

            var chats = new List<Chat>();
            foreach (var chatGuid in rangeGuids)
            {
                try
                {
                    var chat = Chat.Load(chatGuid, kvDataService);
                    chats.Add(chat);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load chat with GUID '{chatGuid}': {ex.Message}");
                }
            }

            return chats;
        }

        /// <summary>
        /// 获取指定范围的对话摘要列表
        /// chatGuids 已经按 LastModifyTime 降序排列，直接按索引范围返回
        /// </summary>
        /// <param name="kvDataService">持久化数据服务</param>
        /// <param name="startIndex">起始索引</param>
        /// <param name="count">要获取的数量</param>
        /// <returns>对话摘要列表</returns>
        public List<ChatSummary> GetChatSummariesRange(IKVDataService kvDataService, int startIndex, int count)
        {
            var rangeGuids = ChatGuids
                .Skip(startIndex)
                .Take(count)
                .ToList();

            var summaries = new List<ChatSummary>();
            foreach (var chatGuid in rangeGuids)
            {
                try
                {
                    var chat = Chat.Load(chatGuid, kvDataService);
                    summaries.Add(new ChatSummary
                    {
                        Guid = chat.Guid,
                        Name = chat.Name,
                        CreateTime = chat.CreateTime,
                        LastModifyTime = chat.LastModifyTime,
                        LastMessagePreview = chat.LastMessagePreview,
                        MessageCount = chat.MessageCount
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load chat summary for GUID '{chatGuid}': {ex.Message}");
                }
            }

            return summaries;
        }

        /// <summary>
        /// 获取单个对话的摘要信息
        /// </summary>
        /// <param name="chatGuid">对话 GUID</param>
        /// <param name="kvDataService">持久化数据服务</param>
        /// <returns>对话摘要对象</returns>
        public ChatSummary GetChatSummary(Guid chatGuid, IKVDataService kvDataService)
        {
            var chat = Chat.Load(chatGuid, kvDataService);
            return new ChatSummary
            {
                Guid = chat.Guid,
                Name = chat.Name,
                CreateTime = chat.CreateTime,
                LastModifyTime = chat.LastModifyTime,
                LastMessagePreview = chat.LastMessagePreview,
                MessageCount = chat.MessageCount
            };
        }

        /// <summary>
        /// 获取聊天总数
        /// </summary>
        public int GetChatCount()
        {
            return ChatGuids.Count;
        }

        /// <summary>
        /// 获取指定 GUID 的对话对象
        /// </summary>
        /// <param name="chatGuid">对话 GUID</param>
        /// <param name="kvDataService">持久化数据服务</param>
        /// <returns>对话对象</returns>
        /// <exception cref="InvalidOperationException">当对话 GUID 不存在于此智能体中时抛出</exception>
        public Chat GetChat(Guid chatGuid, IKVDataService kvDataService)
        {
            if (!ChatGuids.Contains(chatGuid))
            {
                throw new InvalidOperationException($"Chat with GUID '{chatGuid}' not found in agent.");
            }
            return Chat.Load(chatGuid, kvDataService);
        }
		#endregion
        /// <summary>
        /// 留由 Newtonsoft.Json 使用。
        /// </summary>
        #pragma warning disable CS8618
        private Agent()
        {

        }
        #pragma warning restore CS8618
        /// <summary>
        /// 创建一个新的智能体实例
        /// </summary>
        /// <param name="name">智能体名称</param>
        /// <param name="desc">描述</param>
        /// <param name="greeting">欢迎语，可为空</param>
        /// <param name="alternativeGreetings">备选欢迎语列表，可为空</param>
        /// <returns>新创建的 Agent 实例</returns>
        public static Agent Create(string name, string desc, string? greeting = null, List<string>? alternativeGreetings = null)
		{
			return new Agent
			{
				ChatGuids = new List<Guid>(),
				Name = name,
				Description = desc,
				Guid= Guid.NewGuid(),
				Greeting = greeting,
				AlternativeGreetings = alternativeGreetings ?? new List<string>(),
				CustomToolNames = new List<string>(),
				UserIntro = "",
				Tags = new List<string>()
			};
		}
		#region Equal
        /// <summary>
        /// 基于 GUID 比较两个 Agent 是否相等
        /// </summary>
        /// <param name="obj">要比较的对象</param>
        /// <returns>若 GUID 相同则为 true</returns>
        public override bool Equals(object? obj)
		{
			if(obj is Agent agent)
			{
				return agent.Guid == this.Guid;
			}
			return false;
		}

        /// <summary>
        /// 返回基于 GUID 的哈希码
        /// </summary>
        /// <returns>GUID 的哈希码</returns>
        public override int GetHashCode()
		{
			return Guid.GetHashCode();
		}
		#endregion
	}

    /// <summary>
    /// 导出的 Agent 结构。
    /// 因为 Agent 保存的是图像的 Guid，所以导出时需要包含图像的 Base64 编码。
    /// </summary>
    public record AgentExportStructure
    (
        Agent? Agent,
        string? AvatarBase64,
        string? BackgroundBase64
    );
}
