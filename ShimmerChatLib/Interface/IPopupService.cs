using ShimmerChatLib.Models;

namespace ShimmerChatLib.Interface
{
	public interface IPopupService
	{
		public event Action<PopupOptions?>? OnShow;
		public event Action? OnHide;

		Task<bool> ShowAsync(string message, string title = "", bool showCancel = false);
		Task<bool> ShowAsync(PopupOptions options);
		public void Confirm();
		public void Cancel();
	}
}
