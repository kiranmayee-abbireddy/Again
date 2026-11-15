using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Encore.Core.Native;
using Encore.Core.Services;
using Xunit;

namespace Encore.Tests;

public class ChromeRealTest2
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumDesktopWindows(IntPtr hDesktop, NativeMethods.EnumWindowsProc lpfn, IntPtr lParam);

    [Fact]
    public void TestChromeWithInputDesktop()
    {
        var procService = new WindowsProcessService();
        var monService = new WindowsMonitorService();
        var sb = new StringBuilder();

        IntPtr hDesktop = OpenInputDesktop(0, false, 0x0100 /* DESKTOP_ENUMERATE */);
        sb.AppendLine($"OpenInputDesktop handle: {hDesktop}");

        int count = 0;
        int chromeCount = 0;

        if (hDesktop != IntPtr.Zero)
        {
            EnumDesktopWindows(hDesktop, (hWnd, lParam) =>
            {
                count++;
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                string? exe = procService.GetProcessExecutablePath((int)pid);
                int len = NativeMethods.GetWindowTextLength(hWnd);
                var tSb = new StringBuilder(len + 1);
                if (len > 0) NativeMethods.GetWindowText(hWnd, tSb, tSb.Capacity);
                string title = tSb.ToString();

                var cSb = new StringBuilder(256);
                NativeMethods.GetClassName(hWnd, cSb, 256);
                string cls = cSb.ToString();

                bool vis = NativeMethods.IsWindowVisible(hWnd);
                NativeMethods.GetWindowRect(hWnd, out RECT r);

                if (!string.IsNullOrEmpty(exe) && exe.Contains("chrome", StringComparison.OrdinalIgnoreCase))
                {
                    chromeCount++;
                    sb.AppendLine($"CHROME: HWND={hWnd} PID={pid} Vis={vis} TLen={len} Title='{title}' Class='{cls}' Bounds={r.Width}x{r.Height} at ({r.Left},{r.Top})");
                }
                return true;
            }, IntPtr.Zero);

            CloseDesktop(hDesktop);
        }

        sb.AppendLine($"Total windows: {count}, Chrome windows: {chromeCount}");
        File.WriteAllText("D:\\projects\\Encore\\chrome_real_test2.txt", sb.ToString());
    }
}
