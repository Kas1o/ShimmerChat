using ShimmerChat.Models;

namespace ShimmerChat.Singletons
{
	public class PopupService : IPopupService
	{
		private TaskCompletionSource<bool>? _tcs;
		private PopupOptions? _options;

		public event Action<PopupOptions?>? OnShow;
		public event Action? OnHide;

		public Task<bool> ShowAsync(string message, string title = "提示", bool showCancel = false)
			=> ShowAsync(new PopupOptions
			{
				Message = message,
				Title = title,
				ShowCancel = showCancel
			});

		public Task<bool> ShowAsync(PopupOptions options)
		{
			// Create a new TaskCompletionSource for each popup
			_tcs = new TaskCompletionSource<bool>();
			_options = options;
			OnShow?.Invoke(_options);
			return _tcs.Task;
		}

		public void Confirm()
		{
			_tcs?.SetResult(true);
			Hide();
		}

		public void Cancel()
		{
			_tcs?.SetResult(false);
			Hide();
		}

		private void Hide()
		{
			_tcs = null;
			_options = null;
			OnHide?.Invoke();
		}
	}
}
