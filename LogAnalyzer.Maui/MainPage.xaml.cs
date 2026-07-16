namespace LogAnalyzer.Maui;

public partial class MainPage : ContentPage
{
	private bool _dropHooksAttached;

	public MainPage()
	{
		InitializeComponent();
		blazorWebView.HandlerChanged += OnBlazorWebViewHandlerChanged;
	}

	private void OnBlazorWebViewHandlerChanged(object? sender, EventArgs e)
	{
#if WINDOWS
		if (_dropHooksAttached) return;

		if (blazorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement element)
		{
			element.AllowDrop = true;
			_dropHooksAttached = true;

			element.DragOver += (_, args) =>
			{
				args.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
				args.DragUIOverride.IsContentVisible = false;
				args.DragUIOverride.IsGlyphVisible = false;
				args.DragUIOverride.IsCaptionVisible = false;
				args.Handled = true;
			};

			element.Drop += async (_, args) =>
			{
				args.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
				args.Handled = true;

				if (!args.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
					return;

				var services = blazorWebView.Handler?.MauiContext?.Services;
				var store = services?.GetService<LogAnalyzer.Services.LogStore>();
				if (store is null)
					return;

				var deferral = args.GetDeferral();
				try
				{
					var items = await args.DataView.GetStorageItemsAsync();
					var paths = items
						.OfType<Windows.Storage.StorageFile>()
						.Select(f => f.Path)
						.Where(p => !string.IsNullOrWhiteSpace(p))
						.ToList();

					if (paths.Count == 0)
						return;

					// Keep native drop transaction short; parse/load in background.
					_ = Task.Run(async () =>
					{
						try
						{
							await store.LoadFromPathsAsync(paths, includeDebug: store.IncludeDebug, CancellationToken.None);
						}
						catch
						{
							// Best effort: load errors are surfaced via LogStore state.
						}
					});
				}
				finally
				{
					deferral.Complete();
				}
			};
		}
#endif
	}
}
