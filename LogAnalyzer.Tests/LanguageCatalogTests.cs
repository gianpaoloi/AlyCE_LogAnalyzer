using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class LanguageCatalogTests
{
    [Fact]
    public void BuiltIn_HasEnglishItalianAndAlbanianInThatOrder()
    {
        Assert.Equal(new[] { "en", "it", "sq" }, LanguageCatalog.BuiltIn.Select(l => l.Code).Take(3).ToArray());
        Assert.Equal("English", LanguageCatalog.BuiltIn[0].Name);
        Assert.Equal("Italiano", LanguageCatalog.BuiltIn[1].Name);
        Assert.Equal("Shqip", LanguageCatalog.BuiltIn[2].Name);
    }

    [Fact]
    public void BuiltIn_TranslatesEveryKey()
    {
        // A key without text in a built-in language would silently show English there.
        foreach (var language in LanguageCatalog.BuiltIn)
        {
            var missing = LanguageCatalog.Keys.Where(k => !language.Strings.ContainsKey(k.Key)).Select(k => k.Key).ToList();
            Assert.True(missing.Count == 0, $"{language.Code} is missing: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void BuiltIn_EveryEnglishTextIsNonEmpty()
    {
        var empty = LanguageCatalog.Keys.Where(k => string.IsNullOrWhiteSpace(k.English)).Select(k => k.Key).ToList();
        Assert.True(empty.Count == 0, $"No English text for: {string.Join(", ", empty)}");
    }

    [Fact]
    public void BuiltIn_TranslationsKeepThePlaceholdersOfTheEnglish()
    {
        foreach (var language in LanguageCatalog.BuiltIn)
        {
            foreach (var key in LanguageCatalog.Keys)
            {
                if (!language.Strings.TryGetValue(key.Key, out var text)) continue;
                Assert.True(LanguageCatalog.Placeholders(text) == LanguageCatalog.Placeholders(key.English),
                    $"{language.Code} {key.Key} has different placeholders from the English text.");
            }
        }
    }

    [Fact]
    public void Validate_RejectsUnknownKey()
    {
        var language = new AppLanguage("fr", "Français", new() { ["no.such.key"] = "x" });

        Assert.Contains("no.such.key", LanguageCatalog.Validate(language));
    }

    [Fact]
    public void Validate_RejectsBuiltInCodeAndName()
    {
        Assert.NotNull(LanguageCatalog.Validate(new AppLanguage("it", "Mine", new())));
        Assert.NotNull(LanguageCatalog.Validate(new AppLanguage("fr", "Italiano", new())));
    }

    [Fact]
    public void Validate_RejectsBadCode()
    {
        Assert.NotNull(LanguageCatalog.Validate(new AppLanguage("french", "Mine", new())));
        Assert.Null(LanguageCatalog.Validate(new AppLanguage("pt-BR", "Português", new())));
    }

    [Fact]
    public void Validate_RejectsChangedPlaceholders()
    {
        var key = LanguageCatalog.Keys.First(k => k.English.Contains("{0}"));
        var language = new AppLanguage("fr", "Français", new() { [key.Key] = "Sans marqueur" });

        Assert.NotNull(LanguageCatalog.Validate(language));
    }

    [Fact]
    public void Export_ThenParse_RoundTrips()
    {
        var key = LanguageCatalog.Keys.First();
        var language = new AppLanguage("fr", "Français", new() { [key.Key] = "Traduit" });

        var result = LanguageCatalog.Parse(LanguageCatalog.Export(language));

        Assert.Empty(result.Errors);
        var read = Assert.Single(result.Languages);
        Assert.Equal("fr", read.Code);
        Assert.Equal("Français", read.Name);
        Assert.Equal("Traduit", read.Strings[key.Key]);
    }

    [Fact]
    public void Parse_KeepsValidLanguagesAndReportsBadOnes()
    {
        const string json = """
            {"format":"alyce-language","languages":[
              {"code":"fr","name":"Français","strings":{}},
              {"code":"it","name":"Doppione","strings":{}}
            ]}
            """;

        var result = LanguageCatalog.Parse(json);

        Assert.Equal("fr", Assert.Single(result.Languages).Code);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_RejectsForeignFormatAndBadJson()
    {
        Assert.Single(LanguageCatalog.Parse("{\"format\":\"alyce-skin\",\"languages\":[]}").Errors);
        Assert.Single(LanguageCatalog.Parse("{ not json").Errors);
    }

    [Fact]
    public void UniqueCode_AvoidsBuiltInAndTakenCodes()
    {
        Assert.Equal("it-x2", LanguageCatalog.UniqueCode("it", Array.Empty<string>()));
        Assert.Equal("it-x3", LanguageCatalog.UniqueCode("it", new[] { "it-x2" }));
        Assert.Equal("fr", LanguageCatalog.UniqueCode("fr", Array.Empty<string>()));
    }

    [Fact]
    public void ValidateReadme_AcceptsMarkdownText()
    {
        Assert.Null(LanguageCatalog.ValidateReadme("# Guida\n\nTesto."));
    }

    [Fact]
    public void ValidateReadme_RejectsEmptyOversizedAndBinaryText()
    {
        Assert.NotNull(LanguageCatalog.ValidateReadme("   "));
        Assert.NotNull(LanguageCatalog.ValidateReadme(new string('a', LanguageCatalog.MaxReadmeLength + 1)));
        Assert.NotNull(LanguageCatalog.ValidateReadme("text\0binary"));
    }

    [Fact]
    public void UniqueName_AvoidsBuiltInNames()
    {
        Assert.Equal("Italiano (2)", LanguageCatalog.UniqueName("Italiano", Array.Empty<string>()));
    }
}
