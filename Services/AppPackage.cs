using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>
/// Installed from the Microsoft Store (an MSIX package) or as a plain program. The Store version
/// updates through the Store, starts with Windows through the package's startup task and is removed
/// in Windows Settings, so the island's own updater, Run key and uninstall button step aside.
/// </summary>
public static class AppPackage
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder? name);

    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        try
        {
            int len = 0;
            return GetCurrentPackageFullName(ref len, null) != 15700;   // APPMODEL_ERROR_NO_PACKAGE
        }
        catch { return false; }
    }
}
