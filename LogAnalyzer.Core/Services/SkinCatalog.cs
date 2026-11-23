using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Services;

/// <summary>
/// A skin: a named set of colours for the app. Each entry maps a CSS custom property (for example
/// <c>--bg</c>) to a <c>#rrggbb</c> colour. A skin may list only some of the properties; the rest come from
/// <see cref="SkinCatalog.Default"/> when it is applied.
/// </summary>
public sealed record AppSkin(string Name, string Description, Dictionary<string, string> Variables);

/// <summary>One colour the skin editor can change.</summary>
public sealed record SkinColor(string Variable, string Label, string Group);

/// <summary>The result of reading a skin file: the skins that were valid, and one message per problem found.</summary>
public sealed record SkinImport(IReadOnlyList<AppSkin> Skins, IReadOnlyList<string> Errors);

/// <summary>
/// The colour roles a skin controls, the built-in skins, and the rules for reading, checking and writing
/// skin files. Pure data and rules, so it is shared by the server and desktop hosts and covered by tests.
/// </summary>
public static class SkinCatalog
{
    public const string FileFormat = "alyce-skin";
    public const int FileVersion = 1;
    public const int MaxNameLength = 40;
    public const int MaxDescriptionLength = 200;

    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    /// <summary>The colours a skin may set. Only these properties are accepted in a skin file.</summary>
    public static readonly IReadOnlyList<SkinColor> Colors = new SkinColor[]
    {
        new("--bg", "Page background", "Surfaces"),
        new("--panel", "Panels and cards", "Surfaces"),
        new("--panel-2", "Raised panels", "Surfaces"),
        new("--surface-side", "Sidebar", "Surfaces"),
        new("--surface-top", "Top bar and sidebar heading", "Surfaces"),
        new("--txt", "Primary text", "Text"),
        new("--muted", "Secondary text", "Text"),
        new("--text-soft", "Sidebar items", "Text"),
        new("--text-faint", "Sidebar group headings", "Text"),
        new("--brand", "Primary accent", "Accents"),
        new("--brand-2", "Secondary accent", "Accents"),
        new("--fluobrand", "Links and highlights", "Accents"),
        new("--rz-primary", "Buttons and controls", "Components"),
        new("--rz-primary-light", "Buttons, light state", "Components"),
        new("--rz-primary-dark", "Buttons, dark state", "Components"),
        new("--rz-secondary", "Secondary controls", "Components"),
        new("--rz-text-color", "Component text", "Components"),
        new("--rz-text-secondary-color", "Component secondary text", "Components"),
        new("--rz-body-background-color", "Component page background", "Components"),
        new("--rz-base-background-color", "Component base background", "Components"),
        new("--rz-panel-background-color", "Component panel background", "Components"),
        new("--rz-card-background-color", "Component card background", "Components"),
        new("--rz-on-primary", "Text on buttons", "Components"),
        new("--rz-on-secondary", "Text on secondary buttons", "Components"),
        new("--rz-base-50", "Grey 50 (titles and values)", "Component greys"),
        new("--rz-base-100", "Grey 100 (labels)", "Component greys"),
        new("--rz-base-200", "Grey 200 (placeholders)", "Component greys"),
        new("--rz-base-300", "Grey 300 (borders)", "Component greys"),
        new("--rz-base-400", "Grey 400 (disabled)", "Component greys"),
        new("--rz-base-500", "Grey 500 (hover)", "Component greys"),
        new("--rz-base-600", "Grey 600 (control fill)", "Component greys"),
        new("--rz-base-700", "Grey 700 (table rows)", "Component greys"),
        new("--rz-base-800", "Grey 800 (cards)", "Component greys"),
        new("--rz-base-900", "Grey 900 (page)", "Component greys"),
    };

    /// <summary>The launcher look the app shipped with. Its values match the <c>:root</c> block in app.css and Radzen's dark greys.</summary>
    public const string DefaultName = "Default";

    public static readonly AppSkin Default = new(DefaultName, "The original dark purple look.", new Dictionary<string, string>
    {
        ["--bg"] = "#0f1021",
        ["--panel"] = "#17182f",
        ["--panel-2"] = "#1e2038",
        ["--surface-side"] = "#0f1021",
        ["--surface-top"] = "#17182f",
        ["--txt"] = "#f5f7fb",
        ["--muted"] = "#9aa3b2",
        ["--text-soft"] = "#cbd2de",
        ["--text-faint"] = "#6f7787",
        ["--brand"] = "#8a63f4",
        ["--brand-2"] = "#ff5d8f",
        ["--fluobrand"] = "#21d4fd",
        ["--rz-primary"] = "#8a63f4",
        ["--rz-primary-light"] = "#a689f7",
        ["--rz-primary-dark"] = "#6f49d8",
        ["--rz-secondary"] = "#21d4fd",
        ["--rz-text-color"] = "#f5f7fb",
        ["--rz-text-secondary-color"] = "#9aa3b2",
        ["--rz-body-background-color"] = "#0f1021",
        ["--rz-base-background-color"] = "#17182f",
        ["--rz-panel-background-color"] = "#17182f",
        ["--rz-card-background-color"] = "#17182f",
        ["--rz-on-primary"] = "#ffffff",
        ["--rz-on-secondary"] = "#ffffff",
        ["--rz-base-50"] = "#e0e0e0",
        ["--rz-base-100"] = "#a0a0a0",
        ["--rz-base-200"] = "#6c6c6c",
        ["--rz-base-300"] = "#545454",
        ["--rz-base-400"] = "#383838",
        ["--rz-base-500"] = "#333333",
        ["--rz-base-600"] = "#2e2e2e",
        ["--rz-base-700"] = "#252525",
        ["--rz-base-800"] = "#1e1e1e",
        ["--rz-base-900"] = "#121212",
    });

    /// <summary>Skins that ship with the app. They cannot be changed; duplicate one to make an editable copy.</summary>
    public static readonly IReadOnlyList<AppSkin> BuiltIn = new[]
    {
        Default,
        new AppSkin("Blue", "Deep blue dark with bright blue accents.", new Dictionary<string, string>
        {
            ["--bg"] = "#0a1a33",
            ["--panel"] = "#112a4d",
            ["--panel-2"] = "#173760",
            ["--surface-side"] = "#071327",
            ["--surface-top"] = "#112a4d",
            ["--txt"] = "#f2f7ff",
            ["--muted"] = "#9fb8d9",
            ["--text-soft"] = "#cfe0f5",
            ["--text-faint"] = "#7f9cc4",
            ["--brand"] = "#2f80ed",
            ["--brand-2"] = "#f2a93b",
            ["--fluobrand"] = "#56ccf2",
            ["--rz-primary"] = "#2f80ed",
            ["--rz-primary-light"] = "#5aa0f7",
            ["--rz-primary-dark"] = "#1f63c4",
            ["--rz-secondary"] = "#56ccf2",
            ["--rz-text-color"] = "#f2f7ff",
            ["--rz-text-secondary-color"] = "#9fb8d9",
            ["--rz-body-background-color"] = "#0a1a33",
            ["--rz-base-background-color"] = "#112a4d",
            ["--rz-panel-background-color"] = "#112a4d",
            ["--rz-card-background-color"] = "#112a4d",
            ["--rz-on-primary"] = "#ffffff",
            ["--rz-on-secondary"] = "#06203d",
            ["--rz-base-50"] = "#f2f7ff",
            ["--rz-base-100"] = "#9fb8d9",
            ["--rz-base-200"] = "#7f9cc4",
            ["--rz-base-300"] = "#3d5f8f",
            ["--rz-base-400"] = "#2a4a75",
            ["--rz-base-500"] = "#22406a",
            ["--rz-base-600"] = "#1b3760",
            ["--rz-base-700"] = "#15304f",
            ["--rz-base-800"] = "#112a4d",
            ["--rz-base-900"] = "#0a1a33",
        }),
        new AppSkin("Daylight", "Light background with dark text and clear borders.", new Dictionary<string, string>
        {
            ["--bg"] = "#e4e8f2",
            ["--panel"] = "#ffffff",
            ["--panel-2"] = "#eef1f8",
            ["--surface-side"] = "#d5dbe9",
            ["--surface-top"] = "#ffffff",
            ["--txt"] = "#0b0e1c",
            ["--muted"] = "#3a4259",
            ["--text-soft"] = "#1c2338",
            ["--text-faint"] = "#4a5470",
            ["--brand"] = "#5a3bd1",
            ["--brand-2"] = "#c8215f",
            ["--fluobrand"] = "#00658a",
            ["--rz-primary"] = "#5a3bd1",
            ["--rz-primary-light"] = "#7d5fe6",
            ["--rz-primary-dark"] = "#4327b0",
            ["--rz-secondary"] = "#00658a",
            ["--rz-text-color"] = "#0b0e1c",
            ["--rz-text-secondary-color"] = "#3a4259",
            ["--rz-body-background-color"] = "#e4e8f2",
            ["--rz-base-background-color"] = "#ffffff",
            ["--rz-panel-background-color"] = "#ffffff",
            ["--rz-card-background-color"] = "#ffffff",
            ["--rz-on-primary"] = "#ffffff",
            ["--rz-on-secondary"] = "#ffffff",
            ["--rz-base-50"] = "#0b0e1c",
            ["--rz-base-100"] = "#3a4259",
            ["--rz-base-200"] = "#4a5470",
            ["--rz-base-300"] = "#9aa3bc",
            ["--rz-base-400"] = "#bfc6d8",
            ["--rz-base-500"] = "#d3d9e7",
            ["--rz-base-600"] = "#eef1f8",
            ["--rz-base-700"] = "#f4f6fb",
            ["--rz-base-800"] = "#ffffff",
            ["--rz-base-900"] = "#e4e8f2",
        }),
        new AppSkin("High contrast", "Pure black and white with bright yellow and cyan accents, for easier reading.", new Dictionary<string, string>
        {
            ["--bg"] = "#000000",
            ["--panel"] = "#0a0a0a",
            ["--panel-2"] = "#1a1a1a",
            ["--surface-side"] = "#000000",
            ["--surface-top"] = "#000000",
            ["--txt"] = "#ffffff",
            ["--muted"] = "#e6e6e6",
            ["--text-soft"] = "#ffffff",
            ["--text-faint"] = "#cccccc",
            ["--brand"] = "#ffd400",
            ["--brand-2"] = "#ff8a00",
            ["--fluobrand"] = "#00e5ff",
            ["--rz-primary"] = "#ffd400",
            ["--rz-primary-light"] = "#ffe866",
            ["--rz-primary-dark"] = "#c7a500",
            ["--rz-secondary"] = "#00e5ff",
            ["--rz-text-color"] = "#ffffff",
            ["--rz-text-secondary-color"] = "#e6e6e6",
            ["--rz-body-background-color"] = "#000000",
            ["--rz-base-background-color"] = "#0a0a0a",
            ["--rz-panel-background-color"] = "#0a0a0a",
            ["--rz-card-background-color"] = "#0a0a0a",
            ["--rz-on-primary"] = "#000000",
            ["--rz-on-secondary"] = "#000000",
            ["--rz-base-50"] = "#ffffff",
            ["--rz-base-100"] = "#e6e6e6",
            ["--rz-base-200"] = "#cccccc",
            ["--rz-base-300"] = "#bfbfbf",
            ["--rz-base-400"] = "#7a7a7a",
            ["--rz-base-500"] = "#3d3d3d",
            ["--rz-base-600"] = "#1a1a1a",
            ["--rz-base-700"] = "#141414",
            ["--rz-base-800"] = "#0a0a0a",
            ["--rz-base-900"] = "#000000",
        }),
    };

    /// <summary>True for a colour written as #rrggbb, the only form a skin may use.</summary>
    public static bool IsHexColor(string? value) => value is not null && HexColor.IsMatch(value);

    public static bool IsBuiltInName(string name) =>
        BuiltIn.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A copy with every colour filled in: the skin's own values, then the default for anything it leaves out.</summary>
    public static AppSkin Resolve(AppSkin skin)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var color in Colors)
        {
            if (skin.Variables.TryGetValue(color.Variable, out var value) && IsHexColor(value))
                variables[color.Variable] = value.ToLowerInvariant();
            else
                variables[color.Variable] = Default.Variables[color.Variable];
        }
        return skin with { Variables = variables };
    }

    /// <summary>The first problem with the skin, or null when it can be saved.</summary>
    public static string? Validate(AppSkin skin)
    {
        var name = skin.Name?.Trim() ?? "";
        if (name.Length == 0) return "The skin needs a name.";
        if (name.Length > MaxNameLength) return $"The name can be at most {MaxNameLength} characters.";
        if (IsBuiltInName(name)) return $"\"{name}\" is the name of a built-in skin. Choose another name.";
        if ((skin.Description ?? "").Length > MaxDescriptionLength)
            return $"The description can be at most {MaxDescriptionLength} characters.";

        foreach (var (variable, value) in skin.Variables)
        {
            if (Colors.All(c => c.Variable != variable)) return $"Unknown colour \"{variable}\".";
            if (!IsHexColor(value)) return $"{variable} must be a colour like #1a2b3c.";
        }
        return null;
    }

    /// <summary>Returns <paramref name="name"/> with " (2)", " (3)"… added when <paramref name="taken"/> already has it.</summary>
    public static string UniqueName(string name, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var baseName = string.IsNullOrWhiteSpace(name) ? "Skin" : name.Trim();
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

    /// <summary>
    /// Reads a skin file. Accepts the export format (<c>{"format":"alyce-skin","skins":[…]}</c>) or one bare skin
    /// object. A skin with a problem is left out and its problem reported; the others are still read.
    /// </summary>
    public static SkinImport Parse(string json)
    {
        var skins = new List<AppSkin>();
        var errors = new List<string>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new SkinImport(skins, new[] { $"The file is not valid JSON: {ex.Message}" });
        }

        using (document)
        {
            var root = document.RootElement;
            IEnumerable<JsonElement> items;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("skins", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                if (root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String && format.GetString() != FileFormat)
                    return new SkinImport(skins, new[] { "This file is not an AlyCE skin file." });
                items = list.EnumerateArray();
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                items = new[] { root };
            }
            else
            {
                return new SkinImport(skins, new[] { "The file does not contain a skin." });
            }

            var index = 0;
            foreach (var item in items)
            {
                index++;
                var label = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? $"\"{n.GetString()}\"" : $"skin {index}";

                if (item.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{label}: not a skin object.");
                    continue;
                }

                var name = item.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString() ?? "" : "";
                var description = item.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String ? descEl.GetString() ?? "" : "";

                if (!item.TryGetProperty("variables", out var varsEl) || varsEl.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{label}: no \"variables\" object.");
                    continue;
                }

                var variables = new Dictionary<string, string>(StringComparer.Ordinal);
                var badValue = false;
                foreach (var prop in varsEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String)
                    {
                        errors.Add($"{label}: {prop.Name} must be a text colour.");
                        badValue = true;
                        break;
                    }
                    variables[prop.Name] = prop.Value.GetString() ?? "";
                }
                if (badValue) continue;

                var skin = new AppSkin(name.Trim(), description, variables);
                var problem = Validate(skin);
                if (problem is not null)
                {
                    errors.Add($"{label}: {problem}");
                    continue;
                }
                skins.Add(Resolve(skin));
            }
        }

        return new SkinImport(skins, errors);
    }

    /// <summary>The skin as a file that <see cref="Parse"/> reads back, with every colour written out.</summary>
    public static string Export(AppSkin skin)
    {
        var resolved = Resolve(skin);
        var document = new
        {
            format = FileFormat,
            version = FileVersion,
            skins = new[]
            {
                new { name = resolved.Name, description = resolved.Description, variables = resolved.Variables },
            },
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }
}
