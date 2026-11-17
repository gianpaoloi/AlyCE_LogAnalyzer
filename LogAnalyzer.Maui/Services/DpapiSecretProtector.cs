using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Services;

namespace LogAnalyzer.Maui.Services;

/// <summary>
/// Encrypts with Windows DPAPI for the current Windows account. Only that account on this computer can
/// open the result, so copying the browser storage elsewhere, or another account, gives nothing usable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    public string Protect(string plainText) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), null, DataProtectionScope.CurrentUser));

    public string? Unprotect(string protectedText)
    {
        try
        {
            var clear = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Made by another account or computer, or the text was damaged.
            return null;
        }
    }
}
