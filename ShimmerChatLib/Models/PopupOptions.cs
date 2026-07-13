namespace ShimmerChatLib.Models
{
	public class PopupOptions
	{
		public string Title { get; set; } = "";
		public string Message { get; set; } = "";
		public string ConfirmText { get; set; } = "";
		public string CancelText { get; set; } = "";
		public bool ShowCancel { get; set; } = false;
	}
}
