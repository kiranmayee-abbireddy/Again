using System.Diagnostics;
using Encore.Core.Domain;
using Encore.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Encore.Core.Services;

public class RestorationOptions
{
    public bool SkipAlreadyRunning { get; set; } = false;
    public bool RepositionExisting { get; set; } = true;
    public bool LaunchMissing { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 15;
    public HashSet<Guid>? SelectedApplicationIds { get; set; }
}

public class RestorationProgress
{
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public string CurrentApplication { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double Percentage => TotalSteps > 0 ? (double)CurrentStep / TotalSteps * 100 : 0;
}

public interface IWorkspaceRestoreService
{
    Task<RestorationSession> RestoreWorkspaceAsync(
        Workspace workspace, 
        RestorationOptions? options = null, 
        IProgress<RestorationProgress>? progress = null, 
        CancellationToken cancellationToken = default);
}

public class WorkspaceRestoreService : IWorkspaceRestoreService
{
    private readonly IWindowsProcessService _processService;
    private readonly IWindowsWindowService _windowService;
    private readonly IMonitorManagementService _monitorService;
    private readonly IWorkspaceRepository _repository;
    private readonly ILogger<WorkspaceRestoreService>? _logger;

    public WorkspaceRestoreService(
        IWindowsProcessService processService,
        IWindowsWindowService windowService,
        IMonitorManagementService monitorService,
        IWorkspaceRepository repository,
        ILogger<WorkspaceRestoreService>? logger = null)
    {
        _processService = processService;
        _windowService = windowService;
        _monitorService = monitorService;
        _repository = repository;
        _logger = logger;
    }

    public async Task<RestorationSession> RestoreWorkspaceAsync(
        Workspace workspace, 
        RestorationOptions? options = null, 
        IProgress<RestorationProgress>? progress = null, 
        CancellationToken cancellationToken = default)
    {
        options ??= new RestorationOptions();
        var sw = Stopwatch.StartNew();

        var session = new RestorationSession
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            WorkspaceName = workspace.Name,
            StartedAt = DateTime.UtcNow,
            Status = "Running"
        };

        var targetApps = workspace.Applications
            .Where(a => a.IsEnabled)
            .Where(a => options.SelectedApplicationIds == null || options.SelectedApplicationIds.Contains(a.Id))
            .OrderBy(a => a.LaunchOrder)
            .ToList();

        int totalCount = targetApps.Count;
        int currentStep = 0;
        int failedCount = 0;
        int successCount = 0;

        _logger?.LogInformation("Starting restoration for workspace '{WorkspaceName}' with {Count} apps.", workspace.Name, totalCount);

        foreach (var app in targetApps)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                session.Status = "Cancelled";
                break;
            }

            currentStep++;
            progress?.Report(new RestorationProgress
            {
                CurrentStep = currentStep,
                TotalSteps = totalCount,
                CurrentApplication = app.ProcessName,
                Message = $"Restoring {app.ProcessName} ({currentStep}/{totalCount})..."
            });

            var item = new RestorationItem
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                ApplicationPath = app.ExecutablePath,
                ProcessName = app.ProcessName,
                WindowTitle = app.WindowTitle,
                Status = "Pending"
            };

            try
            {
                // Step 1: Validate executable path
                if (!File.Exists(app.ExecutablePath))
                {
                    item.Status = "Failed";
                    item.ErrorMessage = $"Executable not found: {app.ExecutablePath}";
                    failedCount++;
                    item.CompletedAt = DateTime.UtcNow;
                    session.Items.Add(item);
                    _logger?.LogWarning("Restoration validation failed for {ProcessName}: file not found {Path}", app.ProcessName, app.ExecutablePath);
                    continue;
                }

                // Step 2: Check if already running AND has an active matching top-level window
                bool isProcessRunning = _processService.IsProcessRunning(app.ProcessName, app.ExecutablePath);

                bool isExplorer = app.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) || 
                                  (!string.IsNullOrEmpty(app.ExecutablePath) && Path.GetFileName(app.ExecutablePath).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase));

                string? searchTitle = app.WindowTitle;
                if (isExplorer && !string.IsNullOrWhiteSpace(app.FolderViewPath) && !app.FolderViewPath.Equals("Home", StringComparison.OrdinalIgnoreCase))
                {
                    string? folderName = Path.GetFileName(app.FolderViewPath.TrimEnd('\\', '/'));
                    if (!string.IsNullOrEmpty(folderName))
                    {
                        searchTitle = folderName;
                    }
                }

                var existingHwnd = _windowService.FindTopLevelWindow(app.ProcessName, app.ExecutablePath, searchTitle);
                bool hasMatchingWindow = existingHwnd != IntPtr.Zero;

                // An app is considered already open ONLY if it matches the specific target window (folder, tabs, or document)
                bool hasSpecificTarget = isExplorer || 
                                         !string.IsNullOrWhiteSpace(app.BrowserTabsJson) || 
                                         !string.IsNullOrWhiteSpace(app.FolderViewPath) ||
                                         (!string.IsNullOrWhiteSpace(app.LaunchArguments) && (app.LaunchArguments.Contains("--new-window") || app.LaunchArguments.Contains("--profile-directory") || app.LaunchArguments.Length > 2));

                bool isAlreadyOpen = isProcessRunning && (hasMatchingWindow || (!hasSpecificTarget && _windowService.HasVisibleTopLevelWindow(app.ProcessName, app.ExecutablePath)));

                if (isAlreadyOpen)
                {
                    if (options.SkipAlreadyRunning)
                    {
                        item.Status = "Skipped";
                        item.ErrorMessage = "Already running with window (skipped by user option)";
                        item.CompletedAt = DateTime.UtcNow;
                        session.Items.Add(item);
                        continue;
                    }

                    if (options.RepositionExisting)
                    {
                        var hWnd = existingHwnd != IntPtr.Zero ? existingHwnd : _windowService.FindTopLevelWindow(app.ProcessName, app.ExecutablePath, searchTitle);
                        if (hWnd != IntPtr.Zero && app.Placement != null)
                        {
                            var monitor = _monitorService.FindBestMatchingMonitor(app.Placement.MonitorDeviceId, app.Placement.MonitorBounds);
                            bool placed = _windowService.SetPlacement(hWnd, app.Placement, monitor);
                            if (placed)
                            {
                                item.Status = "RepositionedOnly";
                                successCount++;
                            }
                            else
                            {
                                item.Status = "AlreadyRunning";
                                item.ErrorMessage = "Found window, but SetPlacement failed";
                            }
                        }
                        else
                        {
                            item.Status = "AlreadyRunning";
                            item.ErrorMessage = "Running, but matching top-level window was not repositionable";
                        }
                    }
                    else
                    {
                        item.Status = "AlreadyRunning";
                    }

                    item.CompletedAt = DateTime.UtcNow;
                    session.Items.Add(item);
                    continue;
                }

                // Step 3: Launch missing application (or launch window if only background process existed)
                if (!options.LaunchMissing)
                {
                    item.Status = "Skipped";
                    item.ErrorMessage = "Application window is not present and LaunchMissing is false";
                    item.CompletedAt = DateTime.UtcNow;
                    session.Items.Add(item);
                    continue;
                }

                // Optional launch delay
                if (app.LaunchDelayMs > 0)
                {
                    await Task.Delay(app.LaunchDelayMs, cancellationToken);
                }

                // Handle launch arguments for Explorer and Browsers
                string? launchArgs = app.LaunchArguments;

                if (isExplorer)
                {
                    if (string.IsNullOrWhiteSpace(launchArgs) && !string.IsNullOrWhiteSpace(app.FolderViewPath))
                    {
                        launchArgs = app.FolderViewPath.Equals("Home", StringComparison.OrdinalIgnoreCase) 
                            ? string.Empty 
                            : $"\"{app.FolderViewPath}\"";
                    }
                }
                else if (isProcessRunning && !hasMatchingWindow && 
                    (app.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase) || 
                     app.ProcessName.Contains("msedge", StringComparison.OrdinalIgnoreCase)))
                {
                    if (string.IsNullOrWhiteSpace(launchArgs))
                    {
                        launchArgs = "--new-window";
                    }
                }

                var procStarted = _processService.LaunchApplication(app.ExecutablePath, launchArgs);
                if (procStarted == null && !isProcessRunning)
                {
                    item.Status = "Failed";
                    item.ErrorMessage = "Failed to start process";
                    failedCount++;
                    item.CompletedAt = DateTime.UtcNow;
                    session.Items.Add(item);
                    continue;
                }

                int launchedPid = procStarted?.Id ?? 0;

                // Step 4: Wait for main window readiness with polling & timeout across all processes of this app
                IntPtr targetHwnd = IntPtr.Zero;
                var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                var pollStart = Stopwatch.StartNew();

                while (pollStart.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
                {
                    // Check best top-level window matching this process/executable
                    targetHwnd = _windowService.FindTopLevelWindow(app.ProcessName, app.ExecutablePath, searchTitle);
                    if (targetHwnd != IntPtr.Zero && _windowService.IsWindowValid(targetHwnd))
                    {
                        break;
                    }

                    // Check by specific PID if available
                    if (launchedPid > 0)
                    {
                        targetHwnd = _windowService.FindWindowForProcess(launchedPid, app.WindowTitle);
                        if (targetHwnd != IntPtr.Zero && _windowService.IsWindowValid(targetHwnd))
                        {
                            break;
                        }
                    }

                    await Task.Delay(250, cancellationToken);
                }

                // Step 5: Restore window position & state
                if (targetHwnd != IntPtr.Zero && app.Placement != null)
                {
                    var targetMonitor = _monitorService.FindBestMatchingMonitor(app.Placement.MonitorDeviceId, app.Placement.MonitorBounds);
                    bool placed = _windowService.SetPlacement(targetHwnd, app.Placement, targetMonitor);
                    item.Status = placed ? "LaunchedAndPlaced" : "LaunchedWithoutPlacement";
                    successCount++;
                }
                else
                {
                    item.Status = targetHwnd != IntPtr.Zero ? "LaunchedAndPlaced" : "LaunchedWithoutPlacement";
                    if (targetHwnd == IntPtr.Zero)
                    {
                        item.ErrorMessage = "Process launched, but window readiness timed out.";
                    }
                    successCount++;
                }

                item.CompletedAt = DateTime.UtcNow;
                session.Items.Add(item);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error restoring application {ProcessName}", app.ProcessName);
                item.Status = "Failed";
                item.ErrorMessage = ex.Message;
                failedCount++;
                item.CompletedAt = DateTime.UtcNow;
                session.Items.Add(item);
            }
        }

        sw.Stop();
        session.DurationMs = sw.ElapsedMilliseconds;
        session.CompletedAt = DateTime.UtcNow;

        if (session.Status != "Cancelled")
        {
            if (failedCount == 0)
            {
                session.Status = "Success";
            }
            else if (successCount > 0)
            {
                session.Status = "Partial";
            }
            else
            {
                session.Status = "Failed";
            }
        }

        // Save session in history & update last restored timestamp on workspace
        try
        {
            await _repository.SaveRestorationSessionAsync(session);
            await _repository.UpdateLastRestoredAtAsync(workspace.Id, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to persist restoration session history.");
        }

        progress?.Report(new RestorationProgress
        {
            CurrentStep = totalCount,
            TotalSteps = totalCount,
            CurrentApplication = string.Empty,
            Message = $"Restoration complete: {session.Status} ({sw.ElapsedMilliseconds} ms)"
        });

        return session;
    }
}
