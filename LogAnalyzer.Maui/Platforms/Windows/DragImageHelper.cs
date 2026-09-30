using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LogAnalyzer.Maui.WinUI;

/// <summary>
/// Clears the shell drag image left on screen after a file is dropped onto the WebView2.
/// <para>
/// The WinUI WebView2 forwards the drag to Chromium, which registers the Explorer thumbnail with
/// the shell's drag-image helper (IDropTargetHelper.DragEnter). The app handles the drop itself on
/// the XAML side, and Chromium never gets the matching Drop/DragLeave, so the thumbnail stays
/// stuck where the pointer let go (microsoft-ui-xaml#10576, #7366). The helper is process-wide,
/// so telling a fresh instance "the drag left" takes the image down.
/// </para>
/// </summary>
internal static class DragImageHelper
{
    private static readonly Guid CLSID_DragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");

    public static void Dismiss()
    {
        object? instance = null;
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_DragDropHelper);
            if (type is null) return;
            instance = Activator.CreateInstance(type);
            if (instance is IDropTargetHelper helper)
                helper.DragLeave();
        }
        catch (Exception ex)
        {
            // Cosmetic only: a stuck image is better than a failed drop.
            Debug.WriteLine($"Could not dismiss drag image: {ex.Message}");
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
                Marshal.ReleaseComObject(instance);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [ComImport]
    [Guid("4657278B-411B-11D2-839A-00C04FD918D0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTargetHelper
    {
        void DragEnter(IntPtr hwndTarget, IntPtr pDataObject, ref POINT ppt, int dwEffect);
        void DragLeave();
        void DragOver(ref POINT ppt, int dwEffect);
        void Drop(IntPtr pDataObject, ref POINT ppt, int dwEffect);
        void Show([MarshalAs(UnmanagedType.Bool)] bool fShow);
    }
}
