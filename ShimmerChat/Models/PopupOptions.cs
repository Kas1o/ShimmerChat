namespace ShimmerChat.Models
{
	public class PopupOptions
	{
		public string Title { get; set; } = "提示";
		public string Message { get; set; } = "";
		public string ConfirmText { get; set; } = "确定";
		public string CancelText { get; set; } = "取消";
		public bool ShowCancel { get; set; } = false;
	}
}
