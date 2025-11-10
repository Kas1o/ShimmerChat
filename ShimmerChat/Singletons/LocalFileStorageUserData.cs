using ShimmerChatLib;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace ShimmerChat.Singletons
{
	public class LocalFileStorageUserData :  IUserData
	{
		#region terms
		public ObservableCollection<ApiSetting> ApiSettings { get; set; }
		public ObservableCollection<Agent> Agents { get;set; }
		public ObservableCollection<TextCompletionSetting> textCompletionSettings { get; set; }
		public int CurrentTextCompletionSettingIndex { get; set; }
		public int CurrentAPISettingIndex { get; set; }
		public CompletionType CompletionType 
		{
			get => field;
			set
			{
				if(field != value)
				{
					field = value;
				}
			}
		}
		#endregion

		#region listens
		private void TextCompletionSettings_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			SaveUserData();
		}

		private void ApiSettings_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			SaveUserData();
		}

		private void Agents_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			if(e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
			{
				foreach(var item in e.NewItems)
				{
					SaveSpecificAgent((Agent)item);
				}
			}
			if(e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
			{
				foreach (var item in e.OldItems)
				{
					DeleteAgent((Agent)item);
				}
			}
			if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
			{
				// 重新保存所有的 agent
				foreach (var item in Agents)
				{
					SaveSpecificAgent(item);
				}
			}
			if(e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
			{
				foreach (var item in e.OldItems)
				{
					DeleteAgent((Agent)item);
				}
				foreach (var item in e.NewItems)
				{
					SaveSpecificAgent((Agent)item);
				}
			}
		}

		#endregion


		public LocalFileStorageUserData()
		{
			InitializeUserDataFolder();
			ReadUserData();
			InitListener();
		}

		void ReadUserData()
		{
			const string root = "./UserData";

			try
			{
				var apiContent = File.ReadAllText($"{root}/apisettings.json");
				ApiSettings = JsonSerializer.Deserialize<ObservableCollection<ApiSetting>>(apiContent) ?? new ObservableCollection<ApiSetting>();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error while reading api settings: {ex.Message}");
				ApiSettings = new ObservableCollection<ApiSetting>();
			}

			var textCompletionContent = File.ReadAllText($"{root}/textcompletionsettings.json");
			textCompletionSettings = JsonSerializer.Deserialize<ObservableCollection<TextCompletionSetting>>(textCompletionContent) ?? new ObservableCollection<TextCompletionSetting>();

			var completionTypeContent = File.ReadAllText($"{root}/completionType.json");
			CompletionType = JsonSerializer.Deserialize<CompletionType>(completionTypeContent);

			var selectedAPISettingIndexContent = File.ReadAllText($"{root}/selectedAPIsetting.idx");
			CurrentAPISettingIndex = JsonSerializer.Deserialize<int>(selectedAPISettingIndexContent);

			var selectedTextCompletionSettingIndexContent = File.ReadAllText($"{root}/selectedTextCompletionsetting.idx");
			CurrentTextCompletionSettingIndex = JsonSerializer.Deserialize<int>(selectedTextCompletionSettingIndexContent);

			Agents = new();
			foreach (var item in Directory.GetDirectories($"{root}/Agents"))
			{
				try
				{
					Agents.Add( Agent.ReadFrom(item));
				}
				catch(Exception ex)
				{
					Console.WriteLine($"Error While read agent {item}, {ex.Message}");
				}
			}

			
		}
		void InitializeUserDataFolder()
		{
			const string root = "./UserData";

			if(!Directory.Exists(root))
				Directory.CreateDirectory(root);

			if(!File.Exists($"{root}/apisettings.json"))
				File.WriteAllText($"{root}/apisettings.json","[]");

			if (!File.Exists($"{root}/textcompletionsettings.json"))
				File.WriteAllText($"{root}/textcompletionsettings.json", "[]");

			if (!File.Exists($"{root}/completionType.json"))
				File.WriteAllText($"{root}/completionType.json", "1");

			if(!File.Exists($"{root}/selectedAPIsetting.idx"))
				File.WriteAllText($"{root}/selectedAPIsetting.idx", "0");

			if (!File.Exists($"{root}/selectedTextCompletionsetting.idx"))
				File.WriteAllText($"{root}/selectedTextCompletionsetting.idx", "0");

			if (!Directory.Exists($"{root}/Agents")) 
				Directory.CreateDirectory($"{root}/Agents");

		}

		void InitListener()
		{
			ApiSettings.CollectionChanged += ApiSettings_CollectionChanged;
			Agents.CollectionChanged += Agents_CollectionChanged;
			textCompletionSettings.CollectionChanged += TextCompletionSettings_CollectionChanged;
		}

		#region SaveUtil

		public void SaveUserData()
		{
			const string root = "./UserData";

			var apisettingsContent = JsonSerializer.Serialize(ApiSettings);
			File.WriteAllText($"{root}/apisettings.json",apisettingsContent);

			var textCompletionContent = JsonSerializer.Serialize(textCompletionSettings);
			File.WriteAllText($"{root}/textcompletionsettings.json", textCompletionContent);

			var completionTypeContent = JsonSerializer.Serialize(CompletionType);
			File.WriteAllText($"{root}/completionType.json", completionTypeContent);

			var selectedAPISettingIndexContent = JsonSerializer.Serialize(CurrentAPISettingIndex);
			File.WriteAllText($"{root}/selectedAPIsetting.idx", selectedAPISettingIndexContent);

			var selectedTextCompletionSettingIndexContent = JsonSerializer.Serialize(CurrentTextCompletionSettingIndex);
			File.WriteAllText($"{root}/selectedTextCompletionsetting.idx", selectedTextCompletionSettingIndexContent);

			// 保存所有的 Agents
			foreach (var agent in Agents ?? [])
			{
				SaveSpecificAgent(agent);
			}
		}

		public void SaveSpecificAgent(Agent agent)
		{
			const string root = "./UserData";
			if (!Agents.Contains(agent))
			{
				throw new Exception("You try to save an agent that is not in the Agents collection. " +
					"Please add the agent to the collection before saving.");
			}
			Console.WriteLine(
				$"======\n" +
				$"Saving agent!\n" +
				$"guid: {agent.guid}\n" +
				$"name: {agent.name}\n" +
				$"chat count: {agent.chats.Count}\n" +
				$"======");

			// 使用 Agent 的 SaveTo 方法保存到指定路径
			string agentPath = Path.Combine(root, "Agents", agent.guid.ToString() );
			if (!Directory.Exists(agentPath))
				Directory.CreateDirectory(agentPath);

			agent.SaveTo(agentPath);
		}

		private void DeleteAgent(Agent agent)
		{
			const string root = "./UserData";
			if (Agents.Contains(agent))
			{
				throw new InvalidOperationException("Try to delete a agent still in the agents list.");
			}
			else
			{
				Agents.Remove(agent);
				string agentPath = Path.Combine(root, "Agents", agent.guid.ToString());
				if (Directory.Exists(agentPath))
					Directory.Delete(agentPath, true);
			}
		}

		#endregion

	}
}
