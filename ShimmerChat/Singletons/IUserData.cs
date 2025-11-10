using ShimmerChatLib;
using System.Collections.ObjectModel;

namespace ShimmerChat.Singletons
{
	public interface IUserData
	{
		public ObservableCollection<ApiSetting> ApiSettings { get; set; }
		public ObservableCollection<Agent> Agents { get; set; }
		public ObservableCollection<TextCompletionSetting> textCompletionSettings { get; set; }
		public int CurrentTextCompletionSettingIndex { get; set; }
		public int CurrentAPISettingIndex { get; set; }
		public CompletionType CompletionType { get; set; }

		public void SaveSpecificAgent(Agent agent);
		public void SaveUserData();
	}
}
