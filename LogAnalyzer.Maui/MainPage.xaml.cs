namespace LogAnalyzer.Maui;

public partial class MainPage : ContentPage
{
	private bool _dropHooksAttached;
	private bool _startupArgsHandled;

	public MainPage()
	{
		InitializeComponent();
		blazorWebView.HandlerChanged += OnBlazorWebViewHandlerChanged;
	}

	private void OnBlazorWebViewHandlerChanged(object? sender, EventArgs e)
	{
#if WINDOWS
		LoadStartupFileIfAny();

		if (_dropHooksAttached) return;

		if (blazorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement element)
		{
			element.AllowDrop = true;
			_dropHooksAttached = true;

			// DragEnter has to be handled as well as DragOver: the WebView2 forwards whatever reaches
			// it to Chromium, and a Chromium that saw the enter but none of the over/drop/leave that
			// follow leaves the Explorer thumbnail frozen where the pointer came in.
			void OnDragEnterOrOver(object _, Microsoft.UI.Xaml.DragEventArgs args)
			{
				args.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
				args.DragUIOverride.IsContentVisible = false;
				args.DragUIOverride.IsGlyphVisible = false;
				args.DragUIOverride.IsCaptionVisible = false;
				args.Handled = true;
			}

			element.DragEnter += OnDragEnterOrOver;
			element.DragOver += OnDragEnterOrOver;

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
	}

	/// <summary>
	/// Loads a file passed on the command line — how Explorer launches the app when a user
	/// right-clicks a .log file and picks "Open with AlyCE Log Analyzer" (see the installer's
	/// SystemFileAssociations registration). The app is unpackaged (not MSIX), so this is a plain
	/// argv, not an AppInstance activation payload. Runs once per process.
	/// </summary>
	private void LoadStartupFileIfAny()
	{
		if (_startupArgsHandled) return;
		_startupArgsHandled = true;

		var services = blazorWebView.Handler?.MauiContext?.Services;
		var store = services?.GetService<LogAnalyzer.Services.LogStore>();
		if (store is null)
			return;

		var paths = Environment.GetCommandLineArgs().Skip(1)
			.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
			.ToList();
		if (paths.Count == 0)
			return;

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
#endif
}
