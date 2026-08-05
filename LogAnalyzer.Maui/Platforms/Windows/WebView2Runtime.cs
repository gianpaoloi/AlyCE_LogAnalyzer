using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LogAnalyzer.Maui.WinUI;

/// <summary>
/// The whole UI is Blazor hosted in a WebView2, which is a separate OS component: it ships with
/// Windows 11 but is often missing on Windows 10 images and on freshly imaged machines. Without it
/// MAUI throws "Couldn't find a compatible WebView2 Runtime installation to host WebViews" before
/// any window appears, so the app checks first and says what to install.
/// </summary>
internal static class WebView2Runtime
{
    // The Evergreen runtime registers itself under this fixed product GUID (per-machine in
    // HKLM — under WOW6432Node on x64 — or per-user in HKCU when installed without admin).
    private const string ClientKey = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    /// <summary>Evergreen bootstrapper download (same link the installer uses).</summary>
    private const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public static bool IsInstalled =>
        HasVersion(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\" + ClientKey) ||
        HasVersion(Registry.LocalMachine, @"SOFTWARE\" + ClientKey) ||
        HasVersion(Registry.CurrentUser, @"SOFTWARE\" + ClientKey);

    private static bool HasVersion(RegistryKey root, string path)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            // An empty or all-zero "pv" means the runtime was uninstalled but the key stayed behind.
            return key?.GetValue("pv") is string pv && pv.Length > 0 && pv != "0.0.0.0";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView2 probe failed for {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns true when the app can run. Otherwise explains the problem, offers the download
    /// page, and returns false so the caller can bail out instead of crashing.
    /// </summary>
    public static bool EnsureInstalledOrExplain()
    {
        if (IsInstalled) return true;

        const uint MB_YESNO = 0x00000004, MB_ICONERROR = 0x00000010, MB_TOPMOST = 0x00040000;
        const int IDYES = 6;

        var answer = MessageBox(IntPtr.Zero,
            "AlyCE Log Analyzer needs the Microsoft Edge WebView2 Runtime, which is not installed "
            + "on this machine.\n\nIt is a free Microsoft component and is already included in "
            + "Windows 11. Installing it does not require administrator rights.\n\n"
            + "Open the download page now?",
            "WebView2 Runtime required",
            MB_YESNO | MB_ICONERROR | MB_TOPMOST);

        if (answer == IDYES)
        {
            try
            {
                Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not open {DownloadUrl}: {ex.Message}");
            }
        }

        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
