using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radzen;
using LogAnalyzer.Services;
using LogAnalyzer.Services.Updates;
using LogAnalyzer.Maui.Services;
using System.Diagnostics;

namespace LogAnalyzer.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		try
		{
			var builder = MauiApp.CreateBuilder();
			builder
				.UseMauiApp<App>()
				.ConfigureFonts(fonts =>
				{
					fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				});

			builder.Services.AddMauiBlazorWebView();

			// Radzen UI services (DialogService, NotificationService, etc.)
			builder.Services.AddRadzenComponents();

			// Same log-analysis services as the server app, but singletons here: the desktop app is
			// one user with one window, and the loaded dataset should survive a WebView reload.
			builder.Services.AddSingleton<LogStore>();
			builder.Services.AddSingleton<LogWatcher>();
			builder.Services.AddScoped<SessionState>();
			// Recently used paths, persisted in the WebView's localStorage.
			builder.Services.AddScoped<PathHistory>();

			// Update check against this project's GitHub releases.
			//
			// Singleton so GitHub is asked once per run and every page shares the answer — the
			// sidebar asks on each render, and anonymous API calls are rate limited per IP address.
			// Built by a factory rather than by constructor injection because UpdateChecker's other
			// parameters exist for tests and are not registered services.
			builder.Services.AddSingleton(sp => new UpdateChecker(
				UpdateOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>())));
			// This host is the one that can actually update itself: it ships as a per-user Inno Setup
			// package, and re-running that package silently is a supported upgrade.
			builder.Services.AddSingleton<IUpdateInstaller, WindowsUpdateInstaller>();

			// Default log folder - use user's Documents for portability
			string defaultLogFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			defaultLogFolder = Path.Combine(defaultLogFolder, "AlyCE Logs");
			
			builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["LogAnalyzer:DefaultLogFolder"] = defaultLogFolder,
			});

			// Enable logging
			builder.Logging.ClearProviders();
			builder.Logging.AddDebug();
			builder.Logging.SetMinimumLevel(LogLevel.Information);

#if DEBUG
			builder.Services.AddBlazorWebViewDeveloperTools();
#endif

			var app = builder.Build();
			Debug.WriteLine("MauiApp created successfully");
			return app;
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"ERROR creating MauiApp: {ex}");
			Debug.WriteLine($"StackTrace: {ex.StackTrace}");
			throw;
		}
	}
}
