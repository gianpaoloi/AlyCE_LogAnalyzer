using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radzen;
using LogAnalyzer.Services;
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

			// Same log-analysis services as the server app.
			builder.Services.AddSingleton<LogStore>();
			builder.Services.AddSingleton<LogWatcher>();
			builder.Services.AddScoped<SessionState>();

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
