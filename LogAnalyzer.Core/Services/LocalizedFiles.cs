namespace LogAnalyzer.Services;

/// <summary>
/// Picks the translated copy of a document that ships next to the app. For <c>README-HOW-TO.md</c> and the
/// language <c>it</c> the file <c>README-HOW-TO.it.md</c> is used when it exists; otherwise the English file is.
/// </summary>
public static class LocalizedFiles
{
    /// <summary>The file to show for <paramref name="languageCode"/>, falling back to <paramref name="fileName"/>.</summary>
    public static string Resolve(string fileName, string languageCode, string? baseDirectory = null)
    {
        // English is the file itself. A code that is not a plain language code never names a file on disk.
        if (!LanguageCatalog.IsValidCode(languageCode) ||
            string.Equals(languageCode, LanguageCatalog.EnglishCode, StringComparison.OrdinalIgnoreCase))
            return fileName;

        var candidate = $"{Path.GetFileNameWithoutExtension(fileName)}.{languageCode}{Path.GetExtension(fileName)}";
        var folder = baseDirectory ?? AppContext.BaseDirectory;
        return File.Exists(Path.Combine(folder, candidate)) ? candidate : fileName;
    }
}
