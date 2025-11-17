using ShimmerChat.Models;

namespace ShimmerChat.Singletons
{
	public interface IPopupService
	{
		public event Action<PopupOptions?>? OnShow;
		public event Action? OnHide;

		Task<bool> ShowAsync(string message, string title = "提示", bool showCancel = false);
		Task<bool> ShowAsync(PopupOptions options);
		public void Confirm();
		public void Cancel();
	}
}
