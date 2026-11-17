using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace LogAnalyzer.Services;

/// <summary>
/// Encrypts with ASP.NET Core Data Protection. The keys stay on the server (on Windows they are protected
/// with DPAPI), so the text saved in the browser is useless without this server's keys.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("AlyCE.GrafanaSignIn");

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string? Unprotect(string protectedText)
    {
        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (CryptographicException)
        {
            // Made with other keys (for example before the server's key ring was reset), or damaged.
            return null;
        }
    }
}
