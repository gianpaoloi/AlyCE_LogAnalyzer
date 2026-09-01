using LogAnalyzer.Services.Updates;
using Xunit;

namespace LogAnalyzer.Tests;

/// <summary>
/// Decides whether the app is allowed to offer to update itself. Getting this wrong the permissive
/// way is the expensive direction: a portable copy would install a second, separate app into
/// %LOCALAPPDATA% and go on running the old build.
/// </summary>
public class InstallLocationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "alyce-install-test-" + Guid.NewGuid().ToString("N"));

    public InstallLocationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Theory]
    // The name Inno Setup writes on a first install, and after a reinstall into the same directory.
    [InlineData("unins000.exe")]
    [InlineData("unins001.exe")]
    public void Recognises_an_installation_by_its_uninstaller(string uninstaller)
    {
        File.WriteAllText(Path.Combine(_directory, uninstaller), "");

        Assert.True(InstallLocation.IsSetupInstall(_directory));
    }

    [Fact]
    public void Does_not_mistake_the_portable_package_for_an_installation()
    {
        // What the ZIP extracts to: the app, and no uninstaller.
        File.WriteAllText(Path.Combine(_directory, "LogAnalyzer.Maui.exe"), "");
        File.WriteAllText(Path.Combine(_directory, "Launch.bat"), "");

        Assert.False(InstallLocation.IsSetupInstall(_directory));
    }

    [Theory]
    // Close enough to fool a StartsWith or a Contains, but not an Inno uninstaller.
    [InlineData("uninstall.exe")]
    [InlineData("unins000.exe.bak")]
    [InlineData("unins00.exe")]
    [InlineData("unins0000.exe")]
    public void Does_not_accept_a_lookalike(string name)
    {
        File.WriteAllText(Path.Combine(_directory, name), "");

        Assert.False(InstallLocation.IsSetupInstall(_directory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Says_no_when_there_is_no_directory_to_look_at(string? directory)
    {
        Assert.False(InstallLocation.IsSetupInstall(directory));
    }

    [Fact]
    public void Says_no_for_a_directory_that_does_not_exist()
    {
        Assert.False(InstallLocation.IsSetupInstall(Path.Combine(_directory, "nope")));
    }
}
