using LogAnalyzer.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LogAnalyzer.Tests;

// AppLog is process-wide state, so only this class may call Initialize; each test gets its own folder.
public class AppLogTests
{
    private static string NewFolder() =>
        Path.Combine(Path.GetTempPath(), "alyce-applog-tests-" + Guid.NewGuid().ToString("N"));

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

            var text = File.ReadAllText(AppLog.CurrentFilePath!);
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

            var text = File.ReadAllText(AppLog.CurrentFilePath!);
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

            var text = File.ReadAllText(AppLog.CurrentFilePath!);
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

            var text = File.ReadAllText(AppLog.CurrentFilePath!);
            Assert.Contains("DEBUG [Test] detail line", text);
        }
        finally
        {
            Cleanup(folder);
        }
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
