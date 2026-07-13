using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChat.Singletons
{
	public class PopupService : IPopupService
	{
		private readonly ILocService _loc;
		private TaskCompletionSource<bool>? _tcs;
		private PopupOptions? _options;

		public PopupService(ILocService loc)
		{
			_loc = loc;
		}

		public event Action<PopupOptions?>? OnShow;
		public event Action? OnHide;

		public Task<bool> ShowAsync(string message, string title = "", bool showCancel = false)
			=> ShowAsync(new PopupOptions
			{
				Message = message,
				Title = title,
				ShowCancel = showCancel
			});

		public Task<bool> ShowAsync(PopupOptions options)
		{
			// Apply default values from Loc if not set
			if (string.IsNullOrEmpty(options.Title))
				options.Title = _loc["popup.default_title"];
			if (string.IsNullOrEmpty(options.ConfirmText))
				options.ConfirmText = _loc["popup.default_confirm"];
			if (string.IsNullOrEmpty(options.CancelText))
				options.CancelText = _loc["popup.default_cancel"];

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
