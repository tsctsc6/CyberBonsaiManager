using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CyberBonsaiManager.Utilities;

public static partial class WindowsHelper
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_MINIMIZE = 6;

    public static async Task MinimzeProcessMainWindowsAsync(Process process)
    {
        try
        {
            process.WaitForInputIdle();
        }
        catch (InvalidOperationException)
        {
        }
        IntPtr hWnd = 0;
        while (hWnd == 0)
        {
            await Task.Delay(1);
            hWnd = process.MainWindowHandle;
        }
        ShowWindow(hWnd, SW_MINIMIZE);
    }
}