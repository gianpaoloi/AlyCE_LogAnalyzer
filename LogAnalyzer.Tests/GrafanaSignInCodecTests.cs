using System.Text;
using System.Text.Json;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class GrafanaSignInCodecTests
{
    private const string Token = "glsa_example_token_value";
    private const string Cookie = "example-session-cookie-value";

    /// <summary>Reversible stand-in: round-trips, but is not encryption. The tests check what is stored, not the cipher.</summary>
    private sealed class ReversingProtector : ISecretProtector
    {
        public string Protect(string plainText) => Convert.ToBase64String(Encoding.UTF8.GetBytes(Reverse(plainText)));
        public string? Unprotect(string protectedText) => Reverse(Encoding.UTF8.GetString(Convert.FromBase64String(protectedText)));
        private static string Reverse(string text) => new(text.Reverse().ToArray());
    }

    /// <summary>A host with no key: it cannot protect anything and cannot open anything.</summary>
    private sealed class NoKeyProtector : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException("no key");
        public string? Unprotect(string protectedText) => null;
    }

    private static GrafanaSignIn SignIn(bool useToken) =>
        new("https://teamsystem.grafana.net", "loki-uid", Token, Cookie, useToken);

    [Fact]
    public void Seal_keeps_the_token_and_cookie_out_of_the_stored_text()
    {
        var json = GrafanaSignInCodec.Seal(SignIn(useToken: true), new ReversingProtector());

        Assert.NotNull(json);
        Assert.DoesNotContain(Token, json);
        Assert.DoesNotContain(Cookie, json);
        Assert.Contains("https://teamsystem.grafana.net", json);
    }

    [Fact]
    public void Open_gives_back_the_sign_in_that_was_sealed()
    {
        var protector = new ReversingProtector();
        var json = GrafanaSignInCodec.Seal(SignIn(useToken: true), protector)!;

        var read = GrafanaSignInCodec.Open(json, protector);

        Assert.NotNull(read);
        Assert.False(read.Legacy);
        Assert.Equal(SignIn(useToken: true), read.SignIn);
    }

    [Fact]
    public void Open_reads_a_plain_text_save_from_before_encryption_and_marks_it_legacy()
    {
        var plain = JsonSerializer.Serialize(SignIn(useToken: false));

        var read = GrafanaSignInCodec.Open(plain, new ReversingProtector());

        Assert.NotNull(read);
        Assert.True(read.Legacy);
        Assert.Equal(Cookie, read.SignIn.SessionCookie);
    }

    [Fact]
    public void Seal_returns_null_when_there_is_no_key_so_nothing_is_stored_in_plain_text()
    {
        Assert.Null(GrafanaSignInCodec.Seal(SignIn(useToken: true), new NoKeyProtector()));
    }

    [Fact]
    public void Open_keeps_the_address_but_drops_the_secrets_when_the_key_cannot_open_them()
    {
        var json = GrafanaSignInCodec.Seal(SignIn(useToken: true), new ReversingProtector())!;

        var read = GrafanaSignInCodec.Open(json, new NoKeyProtector());

        Assert.NotNull(read);
        Assert.False(read.Legacy);
        Assert.Equal("https://teamsystem.grafana.net", read.SignIn.BaseUrl);
        Assert.Equal("loki-uid", read.SignIn.DatasourceUid);
        Assert.True(read.SignIn.UseToken);
        Assert.Null(read.SignIn.Token);
        Assert.Null(read.SignIn.SessionCookie);
    }

    [Fact]
    public void Open_returns_null_for_damaged_text()
    {
        Assert.Null(GrafanaSignInCodec.Open("not json at all", new ReversingProtector()));
    }
}
