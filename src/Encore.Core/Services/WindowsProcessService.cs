using System.Diagnostics;
using System.Text;
using Encore.Core.Native;
using Microsoft.Extensions.Logging;

namespace Encore.Core.Services;

public interface IWindowsProcessService
{
    Process? LaunchApplication(string executablePath, string? arguments = null, string? workingDirectory = null);
    bool IsProcessRunning(string processName, string? executablePath = null);
    IReadOnlyList<Process> GetRunningProcessesByName(string processName);
    string? GetProcessExecutablePath(int processId);
}

public class WindowsProcessService : IWindowsProcessService
{
    private readonly ILogger<WindowsProcessService>? _logger;

    public WindowsProcessService(ILogger<WindowsProcessService>? logger = null)
    {
        _logger = logger;
    }

    public Process? LaunchApplication(string executablePath, string? arguments = null, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            _logger?.LogWarning("LaunchApplication called with empty executable path.");
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                WorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory) 
                    ? workingDirectory 
                    : Path.GetDirectoryName(executablePath) ?? string.Empty
            };

            var process = Process.Start(startInfo);
            _logger?.LogInformation("Launched process {FileName} (PID: {Pid}) with arguments '{Args}'", 
                executablePath, process?.Id, arguments);
            return process;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to launch application '{ExecutablePath}'", executablePath);
            return null;
        }
    }

    public bool IsProcessRunning(string processName, string? executablePath = null)
    {
        var cleanName = Path.GetFileNameWithoutExtension(processName);
        var processes = Process.GetProcessesByName(cleanName);

        if (processes.Length == 0) return false;

        if (string.IsNullOrEmpty(executablePath))
        {
            return true;
        }

        foreach (var proc in processes)
        {
            try
            {
                var path = GetProcessExecutablePath(proc.Id);
                if (!string.IsNullOrEmpty(path) && string.Equals(path, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore process inspection errors
            }
            finally
            {
                proc.Dispose();
            }
        }

        return false;
    }

    public IReadOnlyList<Process> GetRunningProcessesByName(string processName)
    {
        var cleanName = Path.GetFileNameWithoutExtension(processName);
        return Process.GetProcessesByName(cleanName);
    }

    public string? GetProcessExecutablePath(int processId)
    {
        // 1. Try QueryFullProcessImageName via P/Invoke (most reliable on Windows 10/11)
        IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (hProcess != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    return sb.ToString();
                }
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }

        // 2. Fallback to Process.MainModule
        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
