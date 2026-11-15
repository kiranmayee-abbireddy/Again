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

    public IReadOnlyList<DetectedWindowInfo> EnumerateVisibleWindows(int? excludeProcessId = null)
    {
        var windows = new List<DetectedWindowInfo>();
        var currentPid = Process.GetCurrentProcess().Id;
        var pidToExclude = excludeProcessId ?? currentPid;

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            // Check if window is cloaked (Windows 10/11 virtual desktops, hidden UWP apps)
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            // Check styles
            long style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE).ToInt64();
            long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();

            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
                return true;

            // Get Window Text
            int length = NativeMethods.GetWindowTextLength(hWnd);
            if (length == 0)
                return true;

            var sbTitle = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
            var title = sbTitle.ToString().Trim();
            if (string.IsNullOrEmpty(title))
                return true;

            // Get Class Name
            var sbClass = new StringBuilder(256);
            NativeMethods.GetClassName(hWnd, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();

            // Exclude common system/shell windows
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" 
                or "Windows.UI.Core.CoreWindow" or "DV2ControlHost" or "MsgrIMEWindowClass")
                return true;

            // Get Process ID
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0 || pid == pidToExclude)
                return true;

            // Get Window Bounds
            if (!NativeMethods.GetWindowRect(hWnd, out RECT rect))
                return true;

            if (rect.Width <= 120 || rect.Height <= 80)
                return true;

            // Get Executable Path & Process Name
            string? exePath = _processService.GetProcessExecutablePath((int)pid);
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return true;

            string processName = Path.GetFileNameWithoutExtension(exePath);

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
                Title = title ?? string.Empty,
                ClassName = className,
                Bounds = rect,
                WindowState = windowState,
                Monitor = monitor
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public bool SetPlacement(IntPtr hWnd, WindowPlacement placement, MonitorInfoModel? targetMonitor)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hWnd))
            return false;

        try
        {
            // Adjust coordinates safely for the target monitor
            var (adjX, adjY, adjW, adjH) = _monitorService.AdjustCoordinates(
                placement.X, placement.Y, placement.Width, placement.Height, 
                placement.MonitorDeviceId, placement.MonitorBounds);

            // 1. First ensure window is restored to Normal if it was minimized
            if (NativeMethods.IsIconic(hWnd))
            {
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
            }

            // 2. Position and size the window
            NativeMethods.SetWindowPos(
                hWnd,
                IntPtr.Zero,
                adjX,
                adjY,
                adjW,
                adjH,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);

            // 3. Apply target WindowState
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

    public IntPtr FindWindowForProcess(int processId, string? expectedTitle = null)
    {
        IntPtr foundHwnd = IntPtr.Zero;

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId)
            {
                if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                if (!string.IsNullOrEmpty(expectedTitle))
                {
                    var title = GetWindowTitle(hWnd);
                    if (title.Contains(expectedTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        foundHwnd = hWnd;
                        return false; // Stop enumeration
                    }
                }

                // If no expected title or partial match not yet found, keep as candidate
                if (foundHwnd == IntPtr.Zero)
                {
                    foundHwnd = hWnd;
                }
            }
            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }

    public IReadOnlyList<IntPtr> FindAllWindowsForProcess(int processId)
    {
        var handles = new List<IntPtr>();

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId)
            {
                if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                handles.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        return handles;
    }

    public bool IsWindowValid(IntPtr hWnd)
    {
        return hWnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hWnd);
    }

    public string GetWindowTitle(IntPtr hWnd)
    {
        int length = NativeMethods.GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;

        var sb = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    public RECT GetWindowBounds(IntPtr hWnd)
    {
        NativeMethods.GetWindowRect(hWnd, out RECT rect);
        return rect;
    }
}
