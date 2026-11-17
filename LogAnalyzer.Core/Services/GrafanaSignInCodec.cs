using System.Text.Json;

namespace LogAnalyzer.Services;

/// <summary>Encrypts and decrypts secrets for storage. Each host supplies the key it keeps outside the browser.</summary>
public interface ISecretProtector
{
    /// <summary>Encrypts the text. May throw when the host has no key; the caller then stores nothing.</summary>
    string Protect(string plainText);

    /// <summary>Decrypts text made by <see cref="Protect"/>, or returns null when it cannot be opened (another account, a changed key, damaged text).</summary>
    string? Unprotect(string protectedText);
}

/// <summary>
/// The remembered sign-in as it is written to browser storage. The token and cookie are inside
/// <see cref="Secret"/>, encrypted; the address, datasource and mode stay readable.
/// </summary>
public sealed record StoredGrafanaSignIn(string BaseUrl, string DatasourceUid, bool UseToken, string Secret);

/// <summary>The two secrets, before they are encrypted.</summary>
public sealed record GrafanaSecrets(string? Token, string? SessionCookie);

/// <summary>A sign-in read from storage. <see cref="Legacy"/> is true for a save from before encryption, which holds the secrets in plain text.</summary>
public sealed record SignInRead(GrafanaSignIn SignIn, bool Legacy);

public static class GrafanaSignInCodec
{
    /// <summary>
    /// The JSON to store for this sign-in, with its secrets encrypted. Returns null when they cannot be
    /// encrypted, so that plain text is never the fallback.
    /// </summary>
    public static string? Seal(GrafanaSignIn signIn, ISecretProtector protector)
    {
        try
        {
            var secrets = JsonSerializer.Serialize(new GrafanaSecrets(signIn.Token, signIn.SessionCookie));
            var stored = new StoredGrafanaSignIn(signIn.BaseUrl, signIn.DatasourceUid, signIn.UseToken, protector.Protect(secrets));
            return JsonSerializer.Serialize(stored);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a stored sign-in. A secret that cannot be decrypted comes back as null, so the dialog asks for it again.
    /// A save from before encryption is read as <see cref="SignInRead.Legacy"/>. Returns null for damaged text.
    /// </summary>
    public static SignInRead? Open(string json, ISecretProtector protector)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(nameof(StoredGrafanaSignIn.Secret), out _))
            {
                var legacy = JsonSerializer.Deserialize<GrafanaSignIn>(json);
                return legacy is null ? null : new SignInRead(legacy, Legacy: true);
            }

            var stored = JsonSerializer.Deserialize<StoredGrafanaSignIn>(json);
            if (stored is null) return null;

            var clear = protector.Unprotect(stored.Secret);
            var secrets = clear is null ? null : JsonSerializer.Deserialize<GrafanaSecrets>(clear);
            var signIn = new GrafanaSignIn(stored.BaseUrl, stored.DatasourceUid, secrets?.Token, secrets?.SessionCookie, stored.UseToken);
            return new SignInRead(signIn, Legacy: false);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
