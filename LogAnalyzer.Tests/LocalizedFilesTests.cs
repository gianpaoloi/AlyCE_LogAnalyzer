using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class LocalizedFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "alyce-localized-" + Guid.NewGuid().ToString("N"));

    public LocalizedFilesTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void English_IsTheBaseFile()
    {
        File.WriteAllText(Path.Combine(_folder, "README-HOW-TO.it.md"), "x");

        Assert.Equal("README-HOW-TO.md", LocalizedFiles.Resolve("README-HOW-TO.md", "en", _folder));
    }

    [Fact]
    public void MissingTranslation_FallsBackToEnglish()
    {
        Assert.Equal("README-HOW-TO.md", LocalizedFiles.Resolve("README-HOW-TO.md", "sq", _folder));
    }

    [Fact]
    public void ExistingTranslation_IsChosen()
    {
        File.WriteAllText(Path.Combine(_folder, "README-HOW-TO.it.md"), "x");

        Assert.Equal("README-HOW-TO.it.md", LocalizedFiles.Resolve("README-HOW-TO.md", "it", _folder));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("it/../../x")]
    [InlineData("")]
    public void InvalidCode_NeverNamesAFile(string code)
    {
        Assert.Equal("README-HOW-TO.md", LocalizedFiles.Resolve("README-HOW-TO.md", code, _folder));
    }
}
