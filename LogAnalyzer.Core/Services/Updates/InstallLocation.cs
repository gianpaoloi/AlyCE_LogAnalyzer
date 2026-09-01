namespace LogAnalyzer.Services.Updates;

/// <summary>
/// Tells apart the ways this app can be sitting on a disk, because only one of them can be updated
/// in place.
/// </summary>
public static class InstallLocation
{
    /// <summary>
    /// Broad enough to let the filesystem do the cheap part of the work; the exact shape is then
    /// checked in <see cref="IsInnoUninstaller"/>.
    /// <para>
    /// Deliberately not <c>unins???.exe</c>: the <c>?</c> in a search pattern gets Win32's DOS
    /// semantics, where it can also match zero characters, so that pattern would accept names this
    /// is meant to reject.
    /// </para>
    /// </summary>
    private const string UninstallerPattern = "unins*.exe";

    /// <summary>Length of <c>unins000.exe</c> — the name Inno Setup actually writes.</summary>
    private const int UninstallerNameLength = 12;

    /// <summary>
    /// True when the directory looks like an installation made by the project's setup package —
    /// which is the only case where running that setup again is an update rather than a second,
    /// separate copy of the app.
    /// <para>
    /// The portable ZIP is extracted wherever the user chose and has no uninstaller, so installing
    /// over it would leave the extracted copy behind, still stale, while the "update" landed in
    /// <c>%LOCALAPPDATA%</c>. A development build has the same problem. Both are steered to the
    /// download page instead.
    /// </para>
    /// </summary>
    public static bool IsSetupInstall(string? applicationDirectory)
    {
        if (string.IsNullOrWhiteSpace(applicationDirectory)) return false;

        try
        {
            return Directory.Exists(applicationDirectory)
                && Directory.EnumerateFiles(applicationDirectory, UninstallerPattern)
                            .Any(path => IsInnoUninstaller(Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable directory: assume it is not an updatable install rather than offering an
            // update that would then fail.
            return false;
        }
    }

    /// <summary>
    /// <c>unins</c> followed by exactly three digits and <c>.exe</c> — Inno Setup starts at
    /// <c>unins000.exe</c> and counts up if that name is taken.
    /// </summary>
    private static bool IsInnoUninstaller(string fileName) =>
        fileName.Length == UninstallerNameLength
        && fileName.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
        && char.IsAsciiDigit(fileName[5])
        && char.IsAsciiDigit(fileName[6])
        && char.IsAsciiDigit(fileName[7])
        && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}
