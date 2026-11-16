using System.Runtime.InteropServices;
using Again.Core.Domain;
using Again.Core.Native;
using Microsoft.Extensions.Logging;

namespace Again.Core.Services;

public interface IMonitorManagementService
{
    IReadOnlyList<MonitorInfoModel> GetMonitors();
    MonitorInfoModel? GetPrimaryMonitor();
    MonitorInfoModel? GetMonitorForWindow(IntPtr hWnd);
    MonitorInfoModel? FindBestMatchingMonitor(string? savedDeviceId, string? savedBounds);
    (int x, int y, int width, int height) AdjustCoordinates(int savedX, int savedY, int savedWidth, int savedHeight, string? savedDeviceId, string? savedBounds);
}

public class WindowsMonitorService : IMonitorManagementService
{
    private readonly ILogger<WindowsMonitorService>? _logger;

    public WindowsMonitorService(ILogger<WindowsMonitorService>? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyList<MonitorInfoModel> GetMonitors()
    {
        var monitors = new List<MonitorInfoModel>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var mi = new MONITORINFOEX();
            mi.Size = Marshal.SizeOf(typeof(MONITORINFOEX));

            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                uint dpiX = 96, dpiY = 96;
                try
                {
                    // 0 = MDT_EFFECTIVE_DPI
                    NativeMethods.GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                }
                catch
                {
                    // Fallback to standard 96 DPI
                    dpiX = 96;
                    dpiY = 96;
                }

                bool isPrimary = (mi.Flags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                var model = new MonitorInfoModel
                {
                    DeviceId = mi.DeviceName,
                    DeviceName = MonitorInfoModel.FormatFriendlyName(mi.DeviceName, isPrimary),
                    Left = mi.Monitor.Left,
                    Top = mi.Monitor.Top,
                    Right = mi.Monitor.Right,
                    Bottom = mi.Monitor.Bottom,
                    WorkLeft = mi.WorkArea.Left,
                    WorkTop = mi.WorkArea.Top,
                    WorkRight = mi.WorkArea.Right,
                    WorkBottom = mi.WorkArea.Bottom,
                    IsPrimary = isPrimary,
                    DpiX = dpiX > 0 ? dpiX : 96,
                    DpiY = dpiY > 0 ? dpiY : 96
                };

                monitors.Add(model);
            }
            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            // Ultimate fallback for headless or test environments
            monitors.Add(new MonitorInfoModel
            {
                DeviceId = "DISPLAY1",
                DeviceName = "Primary Display",
                Left = 0,
                Top = 0,
                Right = 1920,
                Bottom = 1080,
                WorkLeft = 0,
                WorkTop = 0,
                WorkRight = 1920,
                WorkBottom = 1040,
                IsPrimary = true
            });
        }

        return monitors;
    }

    public MonitorInfoModel? GetPrimaryMonitor()
    {
        var monitors = GetMonitors();
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
    }

    public MonitorInfoModel? GetMonitorForWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return GetPrimaryMonitor();

        var hMonitor = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return GetPrimaryMonitor();

        var mi = new MONITORINFOEX();
        mi.Size = Marshal.SizeOf(typeof(MONITORINFOEX));
        if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
        {
            var monitors = GetMonitors();
            return monitors.FirstOrDefault(m => m.DeviceId == mi.DeviceName) ?? GetPrimaryMonitor();
        }

        return GetPrimaryMonitor();
    }

    public MonitorInfoModel? FindBestMatchingMonitor(string? savedDeviceId, string? savedBounds)
    {
        var currentMonitors = GetMonitors();
        if (currentMonitors.Count == 0) return null;

        // 1. Try to match by exact device ID
        if (!string.IsNullOrEmpty(savedDeviceId))
        {
            var byId = currentMonitors.FirstOrDefault(m => string.Equals(m.DeviceId, savedDeviceId, StringComparison.OrdinalIgnoreCase));
            if (byId != null) return byId;
        }

        // 2. Try to match by bounds string
        if (!string.IsNullOrEmpty(savedBounds))
        {
            var byBounds = currentMonitors.FirstOrDefault(m => string.Equals(m.BoundsString, savedBounds, StringComparison.OrdinalIgnoreCase));
            if (byBounds != null) return byBounds;
        }

        // 3. Fallback to Primary Monitor
        return GetPrimaryMonitor();
    }

    public (int x, int y, int width, int height) AdjustCoordinates(
        int savedX, int savedY, int savedWidth, int savedHeight, 
        string? savedDeviceId, string? savedBounds)
    {
        var targetMonitor = FindBestMatchingMonitor(savedDeviceId, savedBounds) ?? GetPrimaryMonitor();
        if (targetMonitor == null)
        {
            return (Math.Max(0, savedX), Math.Max(0, savedY), Math.Max(300, savedWidth), Math.Max(200, savedHeight));
        }

        int width = Math.Min(savedWidth, targetMonitor.WorkWidth);
        int height = Math.Min(savedHeight, targetMonitor.WorkHeight);
        if (width < 200) width = Math.Min(800, targetMonitor.WorkWidth);
        if (height < 150) height = Math.Min(600, targetMonitor.WorkHeight);

        // Check if saved position is on the target monitor
        bool isInsideMonitor = savedX >= targetMonitor.WorkLeft && 
                               (savedX + 50) <= targetMonitor.WorkRight &&
                               savedY >= targetMonitor.WorkTop && 
                               (savedY + 50) <= targetMonitor.WorkBottom;

        int finalX;
        int finalY;

        if (isInsideMonitor)
        {
            // Clamp so entire window stays visible
            finalX = Math.Clamp(savedX, targetMonitor.WorkLeft, Math.Max(targetMonitor.WorkLeft, targetMonitor.WorkRight - width));
            finalY = Math.Clamp(savedY, targetMonitor.WorkTop, Math.Max(targetMonitor.WorkTop, targetMonitor.WorkBottom - height));
        }
        else
        {
            // Center or place safely on target monitor
            finalX = targetMonitor.WorkLeft + Math.Max(0, (targetMonitor.WorkWidth - width) / 2);
            finalY = targetMonitor.WorkTop + Math.Max(0, (targetMonitor.WorkHeight - height) / 2);
        }

        return (finalX, finalY, width, height);
    }
}
