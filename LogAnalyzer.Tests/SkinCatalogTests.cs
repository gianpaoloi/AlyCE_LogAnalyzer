using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class SkinCatalogTests
{
    private static Dictionary<string, string> Colours(string bg = "#101010") =>
        new() { ["--bg"] = bg, ["--txt"] = "#ffffff" };

    [Fact]
    public void Resolve_FillsMissingColoursFromDefault()
    {
        var resolved = SkinCatalog.Resolve(new AppSkin("Partial", "", Colours()));

        Assert.Equal("#101010", resolved.Variables["--bg"]);
        Assert.Equal(SkinCatalog.Default.Variables["--brand"], resolved.Variables["--brand"]);
        Assert.Equal(SkinCatalog.Colors.Count, resolved.Variables.Count);
    }

    [Fact]
    public void BuiltInSkins_SetEveryColour()
    {
        // A missing colour would quietly take the dark default, which is how a light skin ends up with dark parts.
        foreach (var skin in SkinCatalog.BuiltIn)
        {
            var missing = SkinCatalog.Colors.Where(c => !skin.Variables.ContainsKey(c.Variable)).Select(c => c.Variable).ToList();
            Assert.True(missing.Count == 0, $"{skin.Name} is missing: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void Resolve_ReplacesInvalidColourWithDefault()
    {
        var resolved = SkinCatalog.Resolve(new AppSkin("Bad", "", new() { ["--bg"] = "red" }));

        Assert.Equal(SkinCatalog.Default.Variables["--bg"], resolved.Variables["--bg"]);
    }

    [Theory]
    [InlineData("#1a2B3c", true)]
    [InlineData("#abc", false)]
    [InlineData("1a2b3c", false)]
    [InlineData("rgba(0,0,0,.5)", false)]
    [InlineData(null, false)]
    public void IsHexColor_AcceptsOnlySixDigitHex(string? value, bool expected) =>
        Assert.Equal(expected, SkinCatalog.IsHexColor(value));

    [Fact]
    public void Validate_RejectsEmptyName()
    {
        Assert.NotNull(SkinCatalog.Validate(new AppSkin("  ", "", Colours())));
    }

    [Fact]
    public void Validate_RejectsBuiltInName()
    {
        Assert.NotNull(SkinCatalog.Validate(new AppSkin("blue", "", Colours())));
    }

    [Fact]
    public void Validate_RejectsUnknownVariable()
    {
        var problem = SkinCatalog.Validate(new AppSkin("X", "", new() { ["--evil"] = "#000000" }));

        Assert.Contains("--evil", problem);
    }

    [Fact]
    public void Validate_RejectsBadHex()
    {
        Assert.NotNull(SkinCatalog.Validate(new AppSkin("X", "", new() { ["--bg"] = "not-a-colour" })));
    }

    [Fact]
    public void Validate_AcceptsGoodSkin()
    {
        Assert.Null(SkinCatalog.Validate(new AppSkin("Mine", "A test skin", Colours())));
    }

    [Fact]
    public void UniqueName_AddsNumberWhenTaken()
    {
        Assert.Equal("Mine (2)", SkinCatalog.UniqueName("Mine", new[] { "Mine" }));
        Assert.Equal("Mine (3)", SkinCatalog.UniqueName("Mine", new[] { "Mine", "Mine (2)" }));
    }

    [Fact]
    public void UniqueName_AvoidsBuiltInNames()
    {
        Assert.Equal("Blue (2)", SkinCatalog.UniqueName("Blue", Array.Empty<string>()));
    }

    [Fact]
    public void Export_ThenParse_RoundTrips()
    {
        var skin = new AppSkin("Round trip", "Keeps its colours", Colours("#202020"));

        var result = SkinCatalog.Parse(SkinCatalog.Export(skin));

        Assert.Empty(result.Errors);
        var read = Assert.Single(result.Skins);
        Assert.Equal("Round trip", read.Name);
        Assert.Equal("Keeps its colours", read.Description);
        Assert.Equal("#202020", read.Variables["--bg"]);
        Assert.Equal(SkinCatalog.Colors.Count, read.Variables.Count);
    }

    [Fact]
    public void Parse_AcceptsBareSkinObject()
    {
        const string json = "{\"name\":\"Bare\",\"variables\":{\"--bg\":\"#123456\"}}";

        var result = SkinCatalog.Parse(json);

        Assert.Empty(result.Errors);
        Assert.Equal("Bare", Assert.Single(result.Skins).Name);
    }

    [Fact]
    public void Parse_KeepsValidSkinsAndReportsBadOnes()
    {
        const string json = """
            {"format":"alyce-skin","skins":[
              {"name":"Good","variables":{"--bg":"#111111"}},
              {"name":"Bad","variables":{"--bg":"blue"}}
            ]}
            """;

        var result = SkinCatalog.Parse(json);

        Assert.Equal("Good", Assert.Single(result.Skins).Name);
        Assert.Single(result.Errors);
        Assert.Contains("Bad", result.Errors[0]);
    }

    [Fact]
    public void Parse_RejectsForeignFormat()
    {
        var result = SkinCatalog.Parse("{\"format\":\"something-else\",\"skins\":[]}");

        Assert.Empty(result.Skins);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ReportsInvalidJson()
    {
        var result = SkinCatalog.Parse("{ not json");

        Assert.Empty(result.Skins);
        Assert.Single(result.Errors);
    }
}
