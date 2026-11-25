using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LogAnalyzer.Services;

/// <summary>
/// An <see cref="ILoggerProvider"/> that writes the application's own troubleshooting log: a plain-text file per
/// day in a <c>logs</c> folder beside the executable, so a user who hits a problem can send the file that covers it.
/// The file rotates daily by name (<c>app-YYYY-MM-DD.log</c>), so no file grows past one day.
/// <para>
/// Code logs through the standard <see cref="ILogger"/> abstraction, like everywhere else in .NET; this provider
/// is what puts those entries in a file. Register it with <c>AddAppFileLog</c> in each host. Where the file goes,
/// which levels are recorded and how long files are kept come from <see cref="LogSettings"/>.
/// </para>
/// <para>
/// This is deliberately separate from the logs the app <em>analyses</em> (those live in the user's "AlyCE Logs"
/// folder). Nothing here is shown in the UI and nothing leaves the machine.
/// </para>
/// <para>
/// Logging must never break the app: every write is best-effort and swallows its own failures. When the log is
/// disabled, or its folder cannot be created, the loggers it hands out do nothing.
/// </para>
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly string? _folder;
    private readonly LogLevel _minLevel;

    public FileLoggerProvider(LogSettings? settings = null)
    {
        settings ??= new LogSettings();
        _minLevel = settings.Level;

        if (!settings.Enabled) return;

        try
        {
            var folder = settings.Folder is null
                ? DefaultFolder
                : Path.Combine(AppContext.BaseDirectory, settings.Folder); // an absolute path wins over the base
            System.IO.Directory.CreateDirectory(folder);
            _folder = folder;

            RemoveOldFiles(folder, settings.RetentionDays);
        }
        catch
        {
            // No log is better than a crash at start-up.
            _folder = null;
        }
    }

    /// <summary>The default location: a <c>logs</c> folder beside the running executable. The installer is per-user, so it is writable without admin rights.</summary>
    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>The folder the daily files are written to, or <c>null</c> when the log is off.</summary>
    public string? LogFolder => _folder;

    /// <summary>The file that receives today's entries, or <c>null</c> when the log is off.</summary>
    public string? CurrentFilePath =>
        _folder is null ? null : Path.Combine(_folder, $"app-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>The least severe level recorded. Entries below it are dropped.</summary>
    public LogLevel MinLevel => _minLevel;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        // Nothing is held open between writes, so there is nothing to release.
    }

    private void Write(LogLevel level, string category, string message, Exception? ex)
    {
        try
        {
            var path = CurrentFilePath;
            if (path is null) return;

            var text = new StringBuilder();
            text.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(' ').Append(LevelName(level).PadRight(5))
                .Append(" [").Append(category).Append("] ")
                .Append(message);

            if (ex is not null)
                text.Append(Environment.NewLine).Append(ex);

            text.Append(Environment.NewLine);

            // One writer at a time: entries arrive on background threads (watcher, UDP listener, load).
            lock (_gate)
            {
                // ReadWrite sharing: the log can be opened in an editor, or read by a test, while the app is writing.
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(file, Encoding.UTF8);
                writer.Write(text.ToString());
            }
        }
        catch
        {
            // The log is a diagnostic aid; a full disk or a locked file must not surface as an app error.
        }
    }

    private static void RemoveOldFiles(string folder, int retentionDays)
    {
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(folder, "app-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch
            {
                // In use or already gone: retention is best effort.
            }
        }
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        _ => "CRIT",
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && provider._folder is not null && logLevel >= provider._minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}

/// <summary>Adds the application's file log to the logging pipeline.</summary>
public static class FileLoggerExtensions
{
    /// <summary>Writes the standard <see cref="Microsoft.Extensions.Logging.ILogger"/> entries to the daily app log file.</summary>
    public static ILoggingBuilder AddAppFileLog(this ILoggingBuilder builder, LogSettings settings)
    {
        // A filter for this provider only: the console and the debugger keep their own levels, while the framework
        // still hands this provider the entries at its level (Debug entries would otherwise be dropped before here).
        builder.Services.Configure<LoggerFilterOptions>(options =>
            options.Rules.Add(new LoggerFilterRule(typeof(FileLoggerProvider).FullName, null, settings.Level, null)));
        builder.AddProvider(new FileLoggerProvider(settings));
        return builder;
    }
}
