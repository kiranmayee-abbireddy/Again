using System.Diagnostics;
using System.Text.Json;
using Encore.Core.Domain;
using Encore.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Encore.Core.Services;

public interface IWorkspaceCaptureService
{
    Task<List<WorkspaceApplication>> CaptureCurrentWindowsAsync();
    Task<Workspace> CreateAndSaveWorkspaceAsync(
        string name, 
        string category, 
        string? description, 
        string? icon, 
        IEnumerable<WorkspaceApplication> applications);
}

public class WorkspaceCaptureService : IWorkspaceCaptureService
{
    private readonly IWindowsWindowService _windowService;
    private readonly IChromiumBrowserService _browserService;
    private readonly IWorkspaceRepository _repository;
    private readonly ILogger<WorkspaceCaptureService>? _logger;

    public WorkspaceCaptureService(
        IWindowsWindowService windowService,
        IChromiumBrowserService browserService,
        IWorkspaceRepository repository,
        ILogger<WorkspaceCaptureService>? logger = null)
    {
        _windowService = windowService;
        _browserService = browserService;
        _repository = repository;
        _logger = logger;
    }

    public Task<List<WorkspaceApplication>> CaptureCurrentWindowsAsync()
    {
        var currentPid = Process.GetCurrentProcess().Id;
        var detectedWindows = _windowService.EnumerateVisibleWindows(excludeProcessId: currentPid);

        var apps = new List<WorkspaceApplication>();
        int order = 0;

        foreach (var window in detectedWindows)
        {
            string? launchArguments = null;
            string? browserProfile = null;
            string? browserTabsJson = null;
            string windowTitle = window.Title;

            // Check if this is a supported browser (Chrome, Edge, Brave, etc.)
            if (_browserService.IsSupportedBrowser(window.ExecutablePath))
            {
                try
                {
                    var browserDetails = _browserService.InspectWindow(window.Handle, window.ExecutablePath);
                    if (browserDetails != null)
                    {
                        browserProfile = $"{browserDetails.ProfileDisplayName} ({browserDetails.ProfileDirectory})";
                        if (browserDetails.Tabs.Count > 0)
                        {
                            browserTabsJson = JsonSerializer.Serialize(browserDetails.Tabs);
                        }
                        launchArguments = browserDetails.LaunchArguments;

                        if (browserDetails.Tabs.Count > 0)
                        {
                            windowTitle = $"{browserDetails.Tabs[0].Title} — {browserDetails.BrowserName}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to inspect browser tabs for {Hwnd}", window.Handle);
                }
            }

            var app = new WorkspaceApplication
            {
                Id = Guid.NewGuid(),
                ExecutablePath = window.ExecutablePath,
                ProcessName = window.ProcessName,
                WindowTitle = windowTitle,
                LaunchArguments = launchArguments,
                BrowserProfile = browserProfile,
                BrowserTabsJson = browserTabsJson,
                LaunchOrder = order++,
                LaunchDelayMs = 250,
                IsEnabled = true,
                Placement = new WindowPlacement
                {
                    Id = Guid.NewGuid(),
                    X = window.Bounds.Left,
                    Y = window.Bounds.Top,
                    Width = window.Bounds.Width,
                    Height = window.Bounds.Height,
                    WindowState = window.WindowState,
                    MonitorDeviceId = window.Monitor?.DeviceId,
                    MonitorBounds = window.Monitor?.BoundsString
                }
            };

            apps.Add(app);
        }

        _logger?.LogInformation("Captured {Count} active application windows.", apps.Count);
        return Task.FromResult(apps);
    }

    public async Task<Workspace> CreateAndSaveWorkspaceAsync(
        string name, 
        string category, 
        string? description, 
        string? icon, 
        IEnumerable<WorkspaceApplication> applications)
    {
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = string.IsNullOrWhiteSpace(category) ? "Custom" : category,
            Description = description,
            Icon = icon,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPinned = false
        };

        int order = 0;
        foreach (var app in applications.Where(a => a.IsEnabled))
        {
            app.WorkspaceId = workspace.Id;
            app.LaunchOrder = order++;
            if (app.Placement != null)
            {
                app.Placement.WorkspaceApplicationId = app.Id;
            }
            workspace.Applications.Add(app);
        }

        await _repository.SaveWorkspaceAsync(workspace);
        _logger?.LogInformation("Created and saved workspace {WorkspaceName} with {Count} applications.", 
            workspace.Name, workspace.Applications.Count);

        return workspace;
    }
}
