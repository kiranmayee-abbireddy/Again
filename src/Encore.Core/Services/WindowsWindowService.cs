using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Encore.Core.Domain;
using Encore.Core.Native;
using Microsoft.Extensions.Logging;

namespace Encore.Core.Services;

public class DetectedWindowInfo
{
    public IntPtr Handle { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public RECT Bounds { get; set; }
    public string WindowState { get; set; } = "Normal";
    public MonitorInfoModel? Monitor { get; set; }
}

public interface IWindowsWindowService
{
    IReadOnlyList<DetectedWindowInfo> EnumerateVisibleWindows(int? excludeProcessId = null);
    bool SetPlacement(IntPtr hWnd, WindowPlacement placement, MonitorInfoModel? targetMonitor);
    IntPtr FindWindowForProcess(int processId, string? expectedTitle = null);
    IntPtr FindTopLevelWindow(string processName, string? executablePath = null, string? expectedTitle = null);
    bool HasVisibleTopLevelWindow(string processName, string? executablePath = null);
    IReadOnlyList<IntPtr> FindAllWindowsForProcess(int processId);
    bool IsWindowValid(IntPtr hWnd);
    string GetWindowTitle(IntPtr hWnd);
    RECT GetWindowBounds(IntPtr hWnd);
}

public class WindowsWindowService : IWindowsWindowService
{
    private readonly IWindowsProcessService _processService;
    private readonly IMonitorManagementService _monitorService;
    private readonly ILogger<WindowsWindowService>? _logger;

    public WindowsWindowService(
        IWindowsProcessService processService,
        IMonitorManagementService monitorService,
        ILogger<WindowsWindowService>? logger = null)
    {
        _processService = processService;
        _monitorService = monitorService;
        _logger = logger;
    }

    private static void EnumerateDesktopWindowsSafe(NativeMethods.EnumWindowsProc proc)
    {
        using (NativeMethods.EnsureInputDesktop())
        {
            IntPtr hDesktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_ENUMERATE);
            if (hDesktop != IntPtr.Zero)
            {
                try
                {
                    if (NativeMethods.EnumDesktopWindows(hDesktop, proc, IntPtr.Zero))
                        return;
                }
                finally
                {
                    NativeMethods.CloseDesktop(hDesktop);
                }
            }

            NativeMethods.EnumWindows(proc, IntPtr.Zero);
        }
    }

    private static bool IsExcludedWindowClass(string className)
    {
        return className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" 
            or "DV2ControlHost" or "MsgrIMEWindowClass" or "IME" or "MSCTFIME UI"
            or "Chrome_StatusTrayWindow" or "Base_PowerMessageWindow" or "crashpad_SessionEndWatcher";
    }

    public IReadOnlyList<DetectedWindowInfo> EnumerateVisibleWindows(int? excludeProcessId = null)
    {
        var windows = new List<DetectedWindowInfo>();
        var currentPid = Process.GetCurrentProcess().Id;
        var pidToExclude = excludeProcessId ?? currentPid;

        EnumerateDesktopWindowsSafe((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            // Check if window is cloaked (Windows 10/11 virtual desktops, hidden UWP apps)
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            // Check styles
            long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
                return true;

            // Get Class Name
            var sbClass = new StringBuilder(256);
            NativeMethods.GetClassName(hWnd, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();

            if (IsExcludedWindowClass(className))
                return true;

            // Get Window Text
            int length = NativeMethods.GetWindowTextLength(hWnd);
            var title = string.Empty;
            if (length > 0)
            {
                var sbTitle = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
                title = sbTitle.ToString().Trim();
            }

            // Get Process ID
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0 || pid == pidToExclude)
                return true;

            // Get Window Bounds
            if (!NativeMethods.GetWindowRect(hWnd, out RECT rect))
                return true;

            if (rect.Width <= 150 || rect.Height <= 100)
                return true;

            // Get Executable Path & Process Name
            string? exePath = _processService.GetProcessExecutablePath((int)pid);
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return true;

            string processName = Path.GetFileNameWithoutExtension(exePath);

            // Handle title fallback for browsers/apps where title is temporarily empty
            if (string.IsNullOrEmpty(title))
            {
                if (className == "Chrome_WidgetWin_1")
                    title = processName.Contains("chrome", StringComparison.OrdinalIgnoreCase) ? "Google Chrome" : processName;
                else if (className == "CabinetWClass")
                    title = "File Explorer";
                else
                    return true;
            }

            // Determine Window State
            string windowState = "Normal";
            if (NativeMethods.IsIconic(hWnd))
                windowState = "Minimized";
            else if (NativeMethods.IsZoomed(hWnd))
                windowState = "Maximized";

            var monitor = _monitorService.GetMonitorForWindow(hWnd);

            windows.Add(new DetectedWindowInfo
            {
                Handle = hWnd,
                ProcessId = (int)pid,
                ProcessName = processName,
                ExecutablePath = exePath,
                Title = title,
                ClassName = className,
                Bounds = rect,
                WindowState = windowState,
                Monitor = monitor
            });

            return true;
        });

        return windows;
    }

    public bool SetPlacement(IntPtr hWnd, WindowPlacement placement, MonitorInfoModel? targetMonitor)
    {
        if (hWnd == IntPtr.Zero)
            return false;

        using (NativeMethods.EnsureInputDesktop())
        {
            try
            {
                // Adjust coordinates safely for the target monitor
                var (adjX, adjY, adjW, adjH) = _monitorService.AdjustCoordinates(
                    placement.X, placement.Y, placement.Width, placement.Height, 
                    placement.MonitorDeviceId, placement.MonitorBounds);

                // 1. If iconic (minimized) or maximized, restore to normal first
                if (NativeMethods.IsIconic(hWnd) || NativeMethods.IsZoomed(hWnd))
                {
                    NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
                }

                // 2. Position and size the window using SetWindowPos
                NativeMethods.SetWindowPos(
                    hWnd,
                    IntPtr.Zero,
                    adjX,
                    adjY,
                    adjW,
                    adjH,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);

                // 3. Synchronize WINDOWPLACEMENT normal bounds
                var wp = new WINDOWPLACEMENT();
                wp.Length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
                if (NativeMethods.GetWindowPlacement(hWnd, ref wp))
                {
                    wp.NormalPosition.Left = adjX;
                    wp.NormalPosition.Top = adjY;
                    wp.NormalPosition.Right = adjX + adjW;
                    wp.NormalPosition.Bottom = adjY + adjH;

                    if (string.Equals(placement.WindowState, "Maximized", StringComparison.OrdinalIgnoreCase))
                    {
                        wp.ShowCmd = NativeMethods.SW_MAXIMIZE;
                    }
                    else if (string.Equals(placement.WindowState, "Minimized", StringComparison.OrdinalIgnoreCase))
                    {
                        wp.ShowCmd = NativeMethods.SW_SHOWMINIMIZED;
                    }
                    else
                    {
                        wp.ShowCmd = NativeMethods.SW_SHOWNORMAL;
                    }

                    NativeMethods.SetWindowPlacement(hWnd, ref wp);
                }

                // 4. Final ShowWindow to enforce requested state
                if (string.Equals(placement.WindowState, "Maximized", StringComparison.OrdinalIgnoreCase))
                {
                    NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MAXIMIZE);
                }
                else if (string.Equals(placement.WindowState, "Minimized", StringComparison.OrdinalIgnoreCase))
                {
                    NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MINIMIZE);
                }
                else
                {
                    NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOWNORMAL);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to set placement for window handle {Hwnd}", hWnd);
                return false;
            }
        }
    }

    public IntPtr FindWindowForProcess(int processId, string? expectedTitle = null)
    {
        IntPtr foundHwnd = IntPtr.Zero;

        EnumerateDesktopWindowsSafe((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId)
            {
                if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                NativeMethods.GetWindowRect(hWnd, out RECT rect);
                if (rect.Width < 150 || rect.Height < 100)
                    return true;

                var cSb = new StringBuilder(256);
                NativeMethods.GetClassName(hWnd, cSb, 256);
                if (IsExcludedWindowClass(cSb.ToString()))
                    return true;

                if (!string.IsNullOrEmpty(expectedTitle))
                {
                    var title = GetWindowTitle(hWnd);
                    if (title.Contains(expectedTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        foundHwnd = hWnd;
                        return false;
                    }
                }

                if (foundHwnd == IntPtr.Zero)
                {
                    foundHwnd = hWnd;
                }
            }
            return true;
        });

        return foundHwnd;
    }

    public IntPtr FindTopLevelWindow(string processName, string? executablePath = null, string? expectedTitle = null)
    {
        var cleanProcName = Path.GetFileNameWithoutExtension(processName);
        IntPtr bestMatchHwnd = IntPtr.Zero;
        int bestScore = -1;

        EnumerateDesktopWindowsSafe((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            NativeMethods.GetWindowRect(hWnd, out RECT rect);
            if (rect.Width < 150 || rect.Height < 100)
                return true;

            var cSb = new StringBuilder(256);
            NativeMethods.GetClassName(hWnd, cSb, 256);
            var className = cSb.ToString();
            if (IsExcludedWindowClass(className))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            string? exe = _processService.GetProcessExecutablePath((int)pid);
            if (string.IsNullOrEmpty(exe)) return true;

            string windowProcName = Path.GetFileNameWithoutExtension(exe);

            bool matchesProcess = string.Equals(windowProcName, cleanProcName, StringComparison.OrdinalIgnoreCase) ||
                                  (!string.IsNullOrEmpty(executablePath) && string.Equals(exe, executablePath, StringComparison.OrdinalIgnoreCase));

            if (!matchesProcess) return true;

            var title = GetWindowTitle(hWnd);
            int score = 0;

            if (!string.IsNullOrEmpty(expectedTitle) && title.Contains(expectedTitle, StringComparison.OrdinalIgnoreCase))
            {
                score = 100;
            }
            else if (className == "Chrome_WidgetWin_1")
            {
                score = 80; // Definite Chromium browser window
            }
            else if (!string.IsNullOrEmpty(title))
            {
                score = 50;
            }
            else
            {
                score = 20;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestMatchHwnd = hWnd;

                if (score == 100)
                {
                    return false; // Found exact match
                }
            }

            return true;
        });

        return bestMatchHwnd;
    }

    public bool HasVisibleTopLevelWindow(string processName, string? executablePath = null)
    {
        return FindTopLevelWindow(processName, executablePath) != IntPtr.Zero;
    }

    public IReadOnlyList<IntPtr> FindAllWindowsForProcess(int processId)
    {
        var handles = new List<IntPtr>();

        EnumerateDesktopWindowsSafe((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId)
            {
                if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                NativeMethods.GetWindowRect(hWnd, out RECT rect);
                if (rect.Width >= 150 && rect.Height >= 100)
                {
                    handles.Add(hWnd);
                }
            }
            return true;
        });

        return handles;
    }

    public bool IsWindowValid(IntPtr hWnd)
    {
        return hWnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hWnd);
    }

    public string GetWindowTitle(IntPtr hWnd)
    {
        using (NativeMethods.EnsureInputDesktop())
        {
            int length = NativeMethods.GetWindowTextLength(hWnd);
            if (length == 0) return string.Empty;

            var sb = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString().Trim();
        }
    }

    public RECT GetWindowBounds(IntPtr hWnd)
    {
        using (NativeMethods.EnsureInputDesktop())
        {
            NativeMethods.GetWindowRect(hWnd, out RECT rect);
            return rect;
        }
    }
}
