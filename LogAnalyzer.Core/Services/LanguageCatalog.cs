using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Services;

/// <summary>
/// A language for the interface: a code (<c>en</c>, <c>it</c>, <c>pt-BR</c>…), a name shown in the menus, and the
/// text for each key. A key missing from <see cref="Strings"/> shows the English text.
/// </summary>
public sealed record AppLanguage(string Code, string Name, Dictionary<string, string> Strings);

/// <summary>One piece of interface text: its key and the English text it is translated from.</summary>
public sealed record LanguageKey(string Key, string English);

/// <summary>The result of reading a language file: the languages that were valid, and one message per problem found.</summary>
public sealed record LanguageImport(IReadOnlyList<AppLanguage> Languages, IReadOnlyList<string> Errors);

/// <summary>
/// The built-in languages and every key they translate, read from the embedded files in <c>Localization/</c>,
/// plus the rules for checking and exchanging custom languages. Each embedded file maps a key to its text in each
/// language: <c>{"nav.overview": {"en": "Overview", "it": "Panoramica", "sq": "Përmbledhje"}}</c>.
/// </summary>
public static class LanguageCatalog
{
    public const string FileFormat = "alyce-language";
    public const int FileVersion = 1;
    public const string EnglishCode = "en";
    public const int MaxNameLength = 40;
    public const int MaxValueLength = 500;

    private static readonly Regex CodePattern = new("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$", RegexOptions.Compiled);
    private static readonly Regex PlaceholderPattern = new(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.Compiled);

    private sealed record Loaded(IReadOnlyList<LanguageKey> Keys, IReadOnlyList<AppLanguage> BuiltIn);

    private static readonly Lazy<Loaded> Data = new(Load);

    /// <summary>Every translatable key, sorted, with its English text.</summary>
    public static IReadOnlyList<LanguageKey> Keys => Data.Value.Keys;

    /// <summary>English first, then the other built-in languages. Built-in languages cannot be changed.</summary>
    public static IReadOnlyList<AppLanguage> BuiltIn => Data.Value.BuiltIn;

    public static AppLanguage English => BuiltIn[0];

    public static bool IsBuiltInCode(string code) =>
        BuiltIn.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    public static bool IsBuiltInName(string name) =>
        BuiltIn.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    private static Loaded Load()
    {
        var assembly = typeof(LanguageCatalog).Assembly;
        var entries = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        var documents = new List<JsonDocument>();

        try
        {
            foreach (var resource in assembly.GetManifestResourceNames()
                         .Where(n => n.Contains(".Localization.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
                         .OrderBy(n => n, StringComparer.Ordinal))
            {
                using var stream = assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException($"Translation file {resource} is missing.");
                var document = JsonDocument.Parse(stream);
                documents.Add(document);

                foreach (var entry in document.RootElement.EnumerateObject())
                {
                    if (!entries.TryAdd(entry.Name, entry.Value.Clone()))
                        throw new InvalidOperationException($"Translation key \"{entry.Name}\" is defined twice.");
                }
            }
        }
        finally
        {
            foreach (var document in documents) document.Dispose();
        }

        var keys = new List<LanguageKey>();
        var codes = new SortedSet<string>(StringComparer.Ordinal) { EnglishCode };
        foreach (var (key, value) in entries)
        {
            var english = value.TryGetProperty(EnglishCode, out var en) && en.ValueKind == JsonValueKind.String ? en.GetString() ?? "" : "";
            keys.Add(new LanguageKey(key, english));
            foreach (var prop in value.EnumerateObject()) codes.Add(prop.Name);
        }

        // English has the first claim on every key; the other languages follow in a fixed order.
        var names = new Dictionary<string, string> { ["en"] = "English", ["it"] = "Italiano", ["sq"] = "Shqip" };
        var ordered = new[] { "en", "it", "sq" }.Concat(codes.Where(c => c is not ("en" or "it" or "sq")));

        var builtIn = new List<AppLanguage>();
        foreach (var code in ordered)
        {
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in entries)
            {
                if (value.TryGetProperty(code, out var text) && text.ValueKind == JsonValueKind.String)
                    strings[key] = text.GetString() ?? "";
            }
            builtIn.Add(new AppLanguage(code, names.GetValueOrDefault(code, code.ToUpperInvariant()), strings));
        }

        return new Loaded(keys, builtIn);
    }

    /// <summary>The placeholders ({0}, {1}…) in a text, as a sorted list. A translation must use the same ones as the English.</summary>
    public static string Placeholders(string text) =>
        string.Join(",", PlaceholderPattern.Matches(text).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>The largest README a language can carry, in characters.</summary>
    public const int MaxReadmeLength = 256 * 1024;

    /// <summary>The first problem with an uploaded README, or null when it can be saved.</summary>
    public static string? ValidateReadme(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "The file is empty or is not a text file.";
        if (text.Length > MaxReadmeLength) return $"The file is too large for a README (at most {MaxReadmeLength / 1024} KB).";
        if (text.Contains('\0')) return "The file is not a text file.";
        return null;
    }

    /// <summary>True for a language code such as <c>fr</c> or <c>pt-BR</c>.</summary>
    public static bool IsValidCode(string? code) => code is not null && CodePattern.IsMatch(code);

    /// <summary>The first problem with the language, or null when it can be saved.</summary>
    public static string? Validate(AppLanguage language)
    {
        var code = language.Code?.Trim() ?? "";
        var name = language.Name?.Trim() ?? "";
        if (!IsValidCode(code)) return "The code must look like \"fr\" or \"pt-BR\".";
        if (IsBuiltInCode(code)) return $"\"{code}\" is the code of a built-in language.";
        if (name.Length == 0) return "The language needs a name.";
        if (name.Length > MaxNameLength) return $"The name can be at most {MaxNameLength} characters.";
        if (IsBuiltInName(name)) return $"\"{name}\" is the name of a built-in language. Choose another name.";

        var known = Keys.ToDictionary(k => k.Key, k => k.English, StringComparer.Ordinal);
        foreach (var (key, text) in language.Strings)
        {
            if (!known.TryGetValue(key, out var english)) return $"Unknown text key \"{key}\".";
            if (text is null) return $"{key} has no text.";
            if (text.Length > MaxValueLength) return $"{key} is longer than {MaxValueLength} characters.";
            if (text.Trim().Length > 0 && Placeholders(text) != Placeholders(english))
                return $"{key} must keep the same {{0}}-style placeholders as the English text.";
        }
        return null;
    }

    /// <summary>
    /// Reads a language file. Accepts the export format (<c>{"format":"alyce-language","languages":[…]}</c>) or one
    /// bare language object. A language with a problem is left out and its problem reported.
    /// </summary>
    public static LanguageImport Parse(string json)
    {
        var languages = new List<AppLanguage>();
        var errors = new List<string>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new LanguageImport(languages, new[] { $"The file is not valid JSON: {ex.Message}" });
        }

        using (document)
        {
            var root = document.RootElement;
            IEnumerable<JsonElement> items;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("languages", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                if (root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String && format.GetString() != FileFormat)
                    return new LanguageImport(languages, new[] { "This file is not an AlyCE language file." });
                items = list.EnumerateArray();
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                items = new[] { root };
            }
            else
            {
                return new LanguageImport(languages, new[] { "The file does not contain a language." });
            }

            var index = 0;
            foreach (var item in items)
            {
                index++;
                if (item.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"language {index}: not a language object.");
                    continue;
                }

                var code = ReadString(item, "code");
                var name = ReadString(item, "name");
                var label = name.Length > 0 ? $"\"{name}\"" : code.Length > 0 ? $"\"{code}\"" : $"language {index}";

                if (!item.TryGetProperty("strings", out var stringsEl) || stringsEl.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{label}: no \"strings\" object.");
                    continue;
                }

                var strings = new Dictionary<string, string>(StringComparer.Ordinal);
                var badValue = false;
                foreach (var prop in stringsEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String)
                    {
                        errors.Add($"{label}: {prop.Name} must be text.");
                        badValue = true;
                        break;
                    }
                    strings[prop.Name] = prop.Value.GetString() ?? "";
                }
                if (badValue) continue;

                var language = new AppLanguage(code, name, strings);
                var problem = Validate(language);
                if (problem is not null)
                {
                    errors.Add($"{label}: {problem}");
                    continue;
                }
                languages.Add(language with { Code = code.ToLowerInvariant(), Name = name.Trim(), Strings = Trimmed(strings) });
            }
        }

        return new LanguageImport(languages, errors);
    }

    /// <summary>The language as a file that <see cref="Parse"/> reads back. Only the texts that were translated are written.</summary>
    public static string Export(AppLanguage language)
    {
        var document = new
        {
            format = FileFormat,
            version = FileVersion,
            languages = new[]
            {
                new
                {
                    code = language.Code,
                    name = language.Name,
                    strings = Trimmed(language.Strings),
                },
            },
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Drops texts that are blank, so an untranslated key falls back to English instead of showing nothing.</summary>
    public static Dictionary<string, string> Trimmed(IReadOnlyDictionary<string, string> strings) =>
        strings.Where(p => p.Value is not null && p.Value.Trim().Length > 0)
            .ToDictionary(p => p.Key, p => p.Value.Trim(), StringComparer.Ordinal);

    /// <summary>A unique name for a language, with " (2)", " (3)"… added when the name is taken.</summary>
    public static string UniqueName(string name, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var baseName = string.IsNullOrWhiteSpace(name) ? "Language" : name.Trim();
        if (baseName.Length > MaxNameLength) baseName = baseName[..MaxNameLength];
        if (!used.Contains(baseName) && !IsBuiltInName(baseName)) return baseName;

        for (var n = 2; ; n++)
        {
            var suffix = $" ({n})";
            var stem = baseName.Length + suffix.Length > MaxNameLength ? baseName[..(MaxNameLength - suffix.Length)] : baseName;
            var candidate = stem + suffix;
            if (!used.Contains(candidate) && !IsBuiltInName(candidate)) return candidate;
        }
    }

    /// <summary>A code that is not built in and not already taken, derived from <paramref name="code"/>.</summary>
    public static string UniqueCode(string code, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var baseCode = IsValidCode(code) ? code : "xx";
        if (!used.Contains(baseCode) && !IsBuiltInCode(baseCode)) return baseCode;
        // The region part of a code needs at least two characters, so the suffix is "-x2", "-x3"…
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseCode}-x{n}";
            if (IsValidCode(candidate) && !used.Contains(candidate) && !IsBuiltInCode(candidate)) return candidate;
        }
    }

    private static string ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "").Trim() : "";
}
