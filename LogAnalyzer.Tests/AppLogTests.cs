using LogAnalyzer.Services;
using LogAnalyzer.Services.Ai;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LogAnalyzer.Tests;

// AppLog is process-wide state, so only this class may call Initialize; each test gets its own folder.
public class AppLogTests
{
    private static string NewFolder() =>
        Path.Combine(Path.GetTempPath(), "alyce-applog-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Reads today's log while the app may still be writing to it, the way an editor would.</summary>
    private static string ReadLog()
    {
        using var file = new FileStream(AppLog.CurrentFilePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }

    private static void Cleanup(string folder)
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* temp folder */ }
    }

    [Fact]
    public void Writes_info_warning_and_error_with_exception_to_todays_file()
    {
        var folder = NewFolder();
        try
        {
            AppLog.Initialize(new LogSettings { Folder = folder, Level = AppLogLevel.Information });
            AppLog.Info("Test", "hello info");
            AppLog.Warn("Test", "hello warn");
            AppLog.Error("Test", "hello error", new InvalidOperationException("boom"));

            var text = ReadLog();
            Assert.Contains("INFO  [Test] hello info", text);
            Assert.Contains("WARN  [Test] hello warn", text);
            Assert.Contains("ERROR [Test] hello error", text);
            Assert.Contains("InvalidOperationException: boom", text);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public void Level_warning_drops_information_entries()
    {
        var folder = NewFolder();
        try
        {
            AppLog.Initialize(new LogSettings { Folder = folder, Level = AppLogLevel.Warning });
            AppLog.Info("Test", "quiet info");
            AppLog.Warn("Test", "loud warn");

            var text = ReadLog();
            Assert.DoesNotContain("quiet info", text);
            Assert.Contains("loud warn", text);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public void Disabled_log_writes_nothing()
    {
        var folder = NewFolder();
        try
        {
            AppLog.Initialize(new LogSettings { Folder = folder, Enabled = false });
            AppLog.Error("Test", "should not appear");

            Assert.Null(AppLog.CurrentFilePath);
            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public void Initialize_removes_daily_files_past_the_configured_retention()
    {
        var folder = NewFolder();
        Directory.CreateDirectory(folder);
        try
        {
            var old = Path.Combine(folder, "app-2000-01-01.log");
            var recent = Path.Combine(folder, "app-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            File.WriteAllText(old, "old");
            File.WriteAllText(recent, "recent");
            File.SetLastWriteTime(old, DateTime.Now.AddDays(-4));

            AppLog.Initialize(new LogSettings { Folder = folder, RetentionDays = 3 });

            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public void Settings_read_from_configuration_with_defaults_for_missing_or_bad_values()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LogAnalyzer:Log:Enabled"] = "false",
            ["LogAnalyzer:Log:Folder"] = "  diag  ",
            ["LogAnalyzer:Log:Level"] = "WARN",
            ["LogAnalyzer:Log:RetentionDays"] = "9999",
        }).Build();

        var settings = LogSettings.FromConfiguration(config);

        Assert.False(settings.Enabled);
        Assert.Equal("diag", settings.Folder);
        Assert.Equal(AppLogLevel.Warning, settings.Level);
        Assert.Equal(LogSettings.MaxRetentionDays, settings.RetentionDays);
    }

    [Fact]
    public void Missing_or_unparseable_settings_fall_back_to_defaults()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LogAnalyzer:Log:Enabled"] = "maybe",
            ["LogAnalyzer:Log:Folder"] = "",
            ["LogAnalyzer:Log:Level"] = "verbose",
            ["LogAnalyzer:Log:RetentionDays"] = "many",
        }).Build();

        var settings = LogSettings.FromConfiguration(config);

        Assert.True(settings.Enabled);
        Assert.Null(settings.Folder);
        Assert.Equal(AppLogLevel.Error, settings.Level);
        Assert.Equal(LogSettings.DefaultRetentionDays, settings.RetentionDays);
    }

    [Fact]
    public void Default_level_records_errors_only()
    {
        var folder = NewFolder();
        try
        {
            AppLog.Initialize(new LogSettings { Folder = folder });
            AppLog.Info("Test", "routine info");
            AppLog.Debug("Test", "routine debug");
            AppLog.Error("Test", "a failure");

            var text = ReadLog();
            Assert.DoesNotContain("routine", text);
            Assert.Contains("a failure", text);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public void Debug_level_records_debug_entries()
    {
        var folder = NewFolder();
        try
        {
            AppLog.Initialize(new LogSettings { Folder = folder, Level = AppLogLevel.Debug });
            AppLog.Debug("Test", "detail line");

            var text = ReadLog();
            Assert.Contains("DEBUG [Test] detail line", text);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public async Task A_failed_provider_test_is_logged_with_the_target_and_no_key()
    {
        var folder = NewFolder();
        try
        {
            AppLog.Initialize(new LogSettings { Folder = folder });
            using var http = new HttpClient(new UnreachableHandler());
            var settings = new AiSettings(AiProviderKind.Gateway, "http://localhost:4000/secret-path", "m");

            await Assert.ThrowsAsync<AiException>(() => AiAssistant.TestAsync(settings, http: http));

            var text = ReadLog();
            Assert.Contains("ERROR [AI] Connection test failed", text);
            Assert.Contains("Gateway at http://localhost:4000, model m", text);
            Assert.DoesNotContain("secret-path", text);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Fails every request as if the host could not be reached.</summary>
    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"));
    }

    [Theory]
    [InlineData("debug", AppLogLevel.Debug)]
    [InlineData("Info", AppLogLevel.Information)]
    [InlineData("WARN", AppLogLevel.Warning)]
    [InlineData("error", AppLogLevel.Error)]
    [InlineData("nonsense", AppLogLevel.Error)]
    public void Level_names_parse_ignoring_case_and_fall_back(string value, AppLogLevel expected)
    {
        Assert.Equal(expected, LogSettings.ParseLevel(value, AppLogLevel.Error));
    }
}
