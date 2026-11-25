using System.Text;

namespace LogAnalyzer.Services;

/// <summary>
/// The application's own troubleshooting log: a plain-text file per day in a <c>logs</c> folder beside
/// the executable, so a user who hits a problem can send the file that covers it. The file rotates
/// daily by name (<c>app-YYYY-MM-DD.log</c>), so no file grows past one day. Where it is written, how
/// much it records and how long it is kept come from <see cref="LogSettings"/>.
/// <para>
/// This is deliberately separate from the logs the app <em>analyses</em> (those live in the user's
/// "AlyCE Logs" folder). Nothing here is shown in the UI and nothing leaves the machine.
/// </para>
/// <para>
/// Logging must never break the app: every write is best-effort and swallows its own failures. Until
/// <see cref="Initialize"/> runs (or when the log is disabled) the calls are no-ops, so library code
/// and tests can log without setup.
/// </para>
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _directory;
    private static AppLogLevel _minLevel = LogSettings.DefaultLevel;
    private static int _retentionDays = LogSettings.DefaultRetentionDays;

    /// <summary>The folder the daily log files are written to, or <c>null</c> when the log is off or not yet started.</summary>
    public static string? Directory => _directory;

    /// <summary>
    /// The default location: a <c>logs</c> folder in the application's root folder, the one holding the
    /// running executable. The installer is per-user, so that folder is writable without admin rights.
    /// </summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>
    /// Starts the log with the given settings: resolves the folder, removes daily files past the retention
    /// period, and writes a start-up line recording the version, OS and effective settings. Safe to call
    /// more than once; the last call wins. With <see cref="LogSettings.Enabled"/> false it turns the log off.
    /// </summary>
    public static void Initialize(LogSettings? settings = null)
    {
        settings ??= new LogSettings();

        try
        {
            if (!settings.Enabled)
            {
                lock (Gate) _directory = null;
                return;
            }

            var folder = settings.Folder is null
                ? DefaultDirectory
                : Path.Combine(AppContext.BaseDirectory, settings.Folder); // an absolute path wins over the base
            System.IO.Directory.CreateDirectory(folder);

            lock (Gate)
            {
                _directory = folder;
                _minLevel = settings.Level;
                _retentionDays = settings.RetentionDays;
            }

            var cutoff = DateTime.Now.AddDays(-settings.RetentionDays);
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

            Write(AppLogLevel.Information, "Startup",
                $"AlyCE Log Analyzer {AppVersion.Display} ({AppVersion.ShortCommit ?? "no commit"}) " +
                $"on {Environment.OSVersion} | .NET {Environment.Version} | log settings: {settings}",
                null);
        }
        catch
        {
            // No log is better than a crash at start-up.
        }
    }

    /// <summary>The file that receives today's entries, or <c>null</c> when the log is off.</summary>
    public static string? CurrentFilePath =>
        _directory is null ? null : Path.Combine(_directory, $"app-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>The retention period in force, in days.</summary>
    public static int RetentionDays => _retentionDays;

    /// <summary>The least severe level currently recorded. Entries below it are dropped.</summary>
    public static AppLogLevel Level => _minLevel;

    public static void Debug(string category, string message) => Write(AppLogLevel.Debug, category, message, null);

    public static void Info(string category, string message) => Write(AppLogLevel.Information, category, message, null);

    public static void Warn(string category, string message) => Write(AppLogLevel.Warning, category, message, null);

    public static void Error(string category, string message, Exception? ex = null) =>
        Write(AppLogLevel.Error, category, message, ex);

    private static void Write(AppLogLevel level, string category, string message, Exception? ex)
    {
        try
        {
            if (level < _minLevel) return;

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

            // One writer at a time: events arrive on background threads (watcher, UDP listener, load).
            lock (Gate)
                File.AppendAllText(path, text.ToString(), Encoding.UTF8);
        }
        catch
        {
            // The log is a diagnostic aid; a full disk or a locked file must not surface as an app error.
        }
    }

    private static string LevelName(AppLogLevel level) => level switch
    {
        AppLogLevel.Debug => "DEBUG",
        AppLogLevel.Information => "INFO",
        AppLogLevel.Warning => "WARN",
        _ => "ERROR",
    };
}
