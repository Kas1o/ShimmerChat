using ShimmerChat.Models;

namespace ShimmerChat.Singletons
{
	public class PopupService : IPopupService
	{
		private readonly TaskCompletionSource<bool> _tcs = new();
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
			_options = null;
			OnHide?.Invoke();
		}
	}
}
