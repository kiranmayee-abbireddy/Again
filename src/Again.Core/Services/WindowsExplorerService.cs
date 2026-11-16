using System;
using System.IO;
using System.Threading;
using Again.Core.Native;
using Microsoft.Extensions.Logging;

namespace Again.Core.Services;

public interface IWindowsExplorerService
{
    bool IsExplorer(string processName, string className, string executablePath);
    string? GetExplorerFolderPath(IntPtr hWnd, string? windowTitle = null);
    string FormatLaunchArguments(string folderPath);
}

public class WindowsExplorerService : IWindowsExplorerService
{
    private readonly ILogger<WindowsExplorerService>? _logger;

    public WindowsExplorerService(ILogger<WindowsExplorerService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsExplorer(string processName, string className, string executablePath)
    {
        if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase))
            return true;

        if (className is "CabinetWClass" or "ExploreWClass")
            return true;

        if (!string.IsNullOrEmpty(executablePath) && 
            Path.GetFileName(executablePath).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public string? GetExplorerFolderPath(IntPtr hWnd, string? windowTitle = null)
    {
        string? result = null;

        // Method 1: Query Shell.Application COM interface in STA thread
        var thread = new Thread(() =>
        {
            try
            {
                using var desktop = NativeMethods.EnsureInputDesktop();
                Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType != null)
                {
                    dynamic? shellApp = Activator.CreateInstance(shellAppType);
                    if (shellApp != null)
                    {
                        var windows = shellApp.Windows();
                        int count = windows.Count;
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                var item = windows.Item(i);
                                long itemHwnd = item.HWND;

                                if (hWnd == IntPtr.Zero || (long)hWnd == itemHwnd)
                                {
                                    string path = "";
                                    try { path = item.Document?.Folder?.Self?.Path ?? ""; } catch {}

                                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                                    {
                                        result = path;
                                        break;
                                    }

                                    string locUrl = item.LocationURL ?? "";
                                    if (!string.IsNullOrEmpty(locUrl) && locUrl.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var uri = new Uri(locUrl);
                                        string localPath = uri.LocalPath;
                                        if (Directory.Exists(localPath))
                                        {
                                            result = localPath;
                                            break;
                                        }
                                    }

                                    string locName = item.LocationName ?? "";
                                    if (!string.IsNullOrEmpty(locName) && locName.Equals("Home", StringComparison.OrdinalIgnoreCase))
                                    {
                                        result = "Home";
                                        break;
                                    }
                                }
                            }
                            catch {}
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to inspect Shell.Application for Explorer window {Hwnd}", hWnd);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(2500))
        {
            _logger?.LogWarning("Explorer inspection timed out for {Hwnd}", hWnd);
        }

        if (!string.IsNullOrEmpty(result))
        {
            return result;
        }

        // Method 2: Parse window title fallback (e.g. "D:\projects\Again - File Explorer" or "Downloads - File Explorer")
        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            string cleanTitle = windowTitle.Replace(" - File Explorer", "").Trim();
            if (Directory.Exists(cleanTitle))
            {
                return cleanTitle;
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string userFolderCandidate = Path.Combine(userProfile, cleanTitle);
            if (Directory.Exists(userFolderCandidate))
            {
                return userFolderCandidate;
            }

            // Check common special folders
            var specialFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(userProfile, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };

            foreach (var sf in specialFolders)
            {
                if (Path.GetFileName(sf).Equals(cleanTitle, StringComparison.OrdinalIgnoreCase) && Directory.Exists(sf))
                {
                    return sf;
                }
            }
        }

        return null;
    }

    public string FormatLaunchArguments(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || folderPath.Equals("Home", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return $"\"{folderPath}\"";
    }
}
