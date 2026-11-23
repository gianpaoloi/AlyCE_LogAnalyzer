namespace LogAnalyzer.Maui;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "LogAnalyzer.Maui" };
#if WINDOWS
		// The system title bar is drawn in the default Windows color, which shows as a strip
		// above the app header. Color it to match the app background.
		window.HandlerChanged += (_, _) =>
		{
			if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
				ApplyWindowsTitleBarColors(nativeWindow);
		};
#endif
		return window;
	}

#if WINDOWS
	private void ApplyWindowsTitleBarColors(Microsoft.UI.Xaml.Window nativeWindow)
	{
		// The window's root content keeps the theme background. On a monitor with a fractional DPI
		// scale the WebView can sit a sub-pixel below the top edge and that default shows through,
		// so give the root the app background as well.
		var appBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
			global::Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x10, 0x21));
		switch (nativeWindow.Content)
		{
			case Microsoft.UI.Xaml.Controls.Panel panel:
				panel.Background = appBrush;
				break;
			case Microsoft.UI.Xaml.Controls.Control control:
				control.Background = appBrush;
				break;
		}

		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
		var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
		var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
		var titleBar = appWindow?.TitleBar;
		if (titleBar is null) return;

		var bg = global::Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x10, 0x21);
		var fg = global::Windows.UI.Color.FromArgb(0xFF, 0xE5, 0xE7, 0xEB);
		var hover = global::Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x20, 0x3D);
		var press = global::Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x2B, 0x4D);

		titleBar.BackgroundColor = bg;
		titleBar.InactiveBackgroundColor = bg;
		titleBar.ForegroundColor = fg;
		titleBar.InactiveForegroundColor = fg;
		titleBar.ButtonBackgroundColor = bg;
		titleBar.ButtonInactiveBackgroundColor = bg;
		titleBar.ButtonForegroundColor = fg;
		titleBar.ButtonInactiveForegroundColor = fg;
		titleBar.ButtonHoverBackgroundColor = hover;
		titleBar.ButtonHoverForegroundColor = fg;
		titleBar.ButtonPressedBackgroundColor = press;
		titleBar.ButtonPressedForegroundColor = fg;
	}
#endif
}
