using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Encore.Core.Domain;
using Encore.Core.Native;
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
    private readonly IApplicationCatalogService? _catalogService;
    private readonly IChromiumBrowserService? _browserService;
    private readonly IWindowsExplorerService? _explorerService;
    private readonly ILogger<WorkspaceRestoreService>? _logger;

    public WorkspaceRestoreService(
        IWindowsProcessService processService,
        IWindowsWindowService windowService,
        IMonitorManagementService monitorService,
        IWorkspaceRepository repository,
        ILogger<WorkspaceRestoreService>? logger = null,
        IApplicationCatalogService? catalogService = null,
        IChromiumBrowserService? browserService = null,
        IWindowsExplorerService? explorerService = null)
    {
        _processService = processService;
        _windowService = windowService;
        _monitorService = monitorService;
        _repository = repository;
        _logger = logger;
        _catalogService = catalogService;
        _browserService = browserService;
        _explorerService = explorerService;
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

        // Tracks windows claimed by previous apps in this restore session to prevent double-claiming
        var claimedHwnds = new HashSet<IntPtr>();

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
                // Step 1: Validate / resolve executable path
                if (!File.Exists(app.ExecutablePath))
                {
                    var resolved = _catalogService?.ResolveExecutablePath(app.ExecutablePath);
                    if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                    {
                        app.ExecutablePath = resolved;
                    }
                    else
                    {
                        item.Status = "Failed";
                        item.ErrorMessage = $"Executable not found: {app.ExecutablePath}";
                        failedCount++;
                        item.CompletedAt = DateTime.UtcNow;
                        session.Items.Add(item);
                        _logger?.LogWarning("Restoration validation failed for {ProcessName}: file not found {Path}", app.ProcessName, app.ExecutablePath);
                        continue;
                    }
                }

                bool isExplorer = app.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) || 
                                  (!string.IsNullOrEmpty(app.ExecutablePath) && Path.GetFileName(app.ExecutablePath).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase));

                bool isBrowser = (_browserService != null && _browserService.IsSupportedBrowser(app.ExecutablePath)) ||
                                 app.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
                                 app.ProcessName.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
                                 app.ProcessName.Contains("brave", StringComparison.OrdinalIgnoreCase) ||
                                 app.ProcessName.Contains("firefox", StringComparison.OrdinalIgnoreCase);

                string? searchTitle = app.WindowTitle;
                if (isExplorer && !string.IsNullOrWhiteSpace(app.FolderViewPath) && !app.FolderViewPath.Equals("Home", StringComparison.OrdinalIgnoreCase))
                {
                    string? folderName = Path.GetFileName(app.FolderViewPath.TrimEnd('\\', '/'));
                    if (!string.IsNullOrEmpty(folderName))
                    {
                        searchTitle = folderName;
                    }
                }

                // Step 2: Check if an active window for this application is ALREADY OPEN on the desktop
                IntPtr existingHwnd = FindUnclaimedWindow(app.ProcessName, app.ExecutablePath, searchTitle, claimedHwnds);
                if (existingHwnd == IntPtr.Zero && !isExplorer)
                {
                    // Find any active, visible top-level window for this process that hasn't been claimed yet
                    existingHwnd = FindUnclaimedWindow(app.ProcessName, app.ExecutablePath, null, claimedHwnds);
                }

                if (existingHwnd != IntPtr.Zero)
                {
                    // Window is ALREADY OPEN! Claim it so subsequent applications don't overlap.
                    claimedHwnds.Add(existingHwnd);

                    if (options.SkipAlreadyRunning)
                    {
                        item.Status = "Skipped";
                        item.ErrorMessage = "Already running with window (skipped by user option)";
                        item.CompletedAt = DateTime.UtcNow;
                        session.Items.Add(item);
                        continue;
                    }

                    // --- BROWSER RE-USE LOGIC ---
                    // "like if chrome is already open dont open another windows if one tab misses add that tab in current process instead"
                    if (isBrowser)
                    {
                        var targetTabs = GetTargetTabs(app);
                        if (targetTabs.Count > 0)
                        {
                            var details = _browserService?.InspectWindow(existingHwnd, app.ExecutablePath);

                            var openUrls = details?.Tabs?.Select(t => NormalizeUrl(t.Url)).Where(u => !string.IsNullOrEmpty(u)).ToHashSet() ?? new();
                            var openTitles = details?.Tabs?.Select(t => t.Title.Trim().ToLowerInvariant()).Where(t => !string.IsNullOrEmpty(t)).ToHashSet() ?? new();

                            string currentTitle = _windowService.GetWindowTitle(existingHwnd);
                            if (!string.IsNullOrEmpty(currentTitle))
                            {
                                openTitles.Add(currentTitle.Trim().ToLowerInvariant());
                            }

                            var missingTabs = targetTabs.Where(t => !IsTabAlreadyOpen(t, openUrls, openTitles)).ToList();

                            if (missingTabs.Count > 0)
                            {
                                // Open ONLY missing tabs into the existing process WITHOUT --new-window
                                var profileArg = ExtractProfileArg(app.BrowserProfile, app.LaunchArguments);
                                var argsBuilder = new StringBuilder();
                                if (!string.IsNullOrEmpty(profileArg))
                                {
                                    argsBuilder.Append(profileArg).Append(' ');
                                }
                                foreach (var mt in missingTabs)
                                {
                                    if (!string.IsNullOrWhiteSpace(mt.Url))
                                    {
                                        argsBuilder.Append($"\"{mt.Url}\" ");
                                    }
                                }

                                string finalArgs = argsBuilder.ToString().Trim();
                                if (!string.IsNullOrEmpty(finalArgs))
                                {
                                    _processService.LaunchApplication(app.ExecutablePath, finalArgs);
                                    _logger?.LogInformation("Browser is already open. Added {Count} missing tab(s) to current process without creating a new window.", missingTabs.Count);
                                }
                            }
                        }

                        // Reposition existing browser window
                        if (options.RepositionExisting && app.Placement != null)
                        {
                            var monitor = _monitorService.FindBestMatchingMonitor(app.Placement.MonitorDeviceId, app.Placement.MonitorBounds);
                            _windowService.SetPlacement(existingHwnd, app.Placement, monitor);
                        }

                        NativeMethods.SetForegroundWindow(existingHwnd);
                        item.Status = "RepositionedOnly";
                        successCount++;
                        item.CompletedAt = DateTime.UtcNow;
                        session.Items.Add(item);
                        continue;
                    }

                    // --- FILE EXPLORER RE-USE LOGIC ---
                    if (isExplorer && !string.IsNullOrWhiteSpace(app.FolderViewPath))
                    {
                        string currentFolder = _explorerService?.GetExplorerFolderPath(existingHwnd, _windowService.GetWindowTitle(existingHwnd)) ?? string.Empty;
                        if (!string.IsNullOrEmpty(currentFolder) && string.Equals(currentFolder, app.FolderViewPath, StringComparison.OrdinalIgnoreCase))
                        {
                            // Already showing the exact target folder
                            if (options.RepositionExisting && app.Placement != null)
                            {
                                var monitor = _monitorService.FindBestMatchingMonitor(app.Placement.MonitorDeviceId, app.Placement.MonitorBounds);
                                _windowService.SetPlacement(existingHwnd, app.Placement, monitor);
                            }
                            NativeMethods.SetForegroundWindow(existingHwnd);
                            item.Status = "RepositionedOnly";
                            successCount++;
                            item.CompletedAt = DateTime.UtcNow;
                            session.Items.Add(item);
                            continue;
                        }
                    }

                    // --- GENERAL APPS RE-USE LOGIC ---
                    // "if something is already running, while restoring dont create same thing again ... and for apps we cant do anything so run new instance only if needed"
                    if (options.RepositionExisting && app.Placement != null)
                    {
                        var monitor = _monitorService.FindBestMatchingMonitor(app.Placement.MonitorDeviceId, app.Placement.MonitorBounds);
                        bool placed = _windowService.SetPlacement(existingHwnd, app.Placement, monitor);
                        item.Status = placed ? "RepositionedOnly" : "AlreadyRunning";
                        successCount++;
                    }
                    else
                    {
                        item.Status = "AlreadyRunning";
                        successCount++;
                    }

                    NativeMethods.SetForegroundWindow(existingHwnd);
                    item.CompletedAt = DateTime.UtcNow;
                    session.Items.Add(item);
                    continue;
                }

                // Step 3: No existing window found — "run new instance only if needed"
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

                // Prepare launch arguments
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
                else if (isBrowser)
                {
                    // Clean args: if launch arguments were empty or had only profile, pass target tabs
                    var targetTabs = GetTargetTabs(app);
                    if (targetTabs.Count > 0 && (string.IsNullOrWhiteSpace(launchArgs) || !launchArgs.Contains("http", StringComparison.OrdinalIgnoreCase)))
                    {
                        var sb = new StringBuilder();
                        var profileArg = ExtractProfileArg(app.BrowserProfile, launchArgs);
                        if (!string.IsNullOrEmpty(profileArg)) sb.Append(profileArg).Append(' ');
                        sb.Append("--new-window ");
                        foreach (var t in targetTabs)
                        {
                            if (!string.IsNullOrWhiteSpace(t.Url)) sb.Append($"\"{t.Url}\" ");
                        }
                        launchArgs = sb.ToString().Trim();
                    }
                }

                bool isProcessRunning = _processService.IsProcessRunning(app.ProcessName, app.ExecutablePath);
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

                // Step 4: Wait for main window readiness with polling & timeout
                IntPtr targetHwnd = IntPtr.Zero;
                var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                var pollStart = Stopwatch.StartNew();

                while (pollStart.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
                {
                    // Check by specific PID if available
                    if (launchedPid > 0)
                    {
                        targetHwnd = _windowService.FindWindowForProcess(launchedPid, app.WindowTitle);
                        if (targetHwnd != IntPtr.Zero && _windowService.IsWindowValid(targetHwnd) && !claimedHwnds.Contains(targetHwnd))
                        {
                            break;
                        }
                    }

                    // Check best unclaimed top-level window matching this process/executable
                    targetHwnd = FindUnclaimedWindow(app.ProcessName, app.ExecutablePath, searchTitle, claimedHwnds);
                    if (targetHwnd != IntPtr.Zero && _windowService.IsWindowValid(targetHwnd))
                    {
                        break;
                    }

                    if (targetHwnd == IntPtr.Zero)
                    {
                        targetHwnd = FindUnclaimedWindow(app.ProcessName, app.ExecutablePath, null, claimedHwnds);
                        if (targetHwnd != IntPtr.Zero && _windowService.IsWindowValid(targetHwnd))
                        {
                            break;
                        }
                    }

                    await Task.Delay(250, cancellationToken);
                }

                // Step 5: Restore window position & state
                if (targetHwnd != IntPtr.Zero)
                {
                    claimedHwnds.Add(targetHwnd);

                    if (app.Placement != null)
                    {
                        var targetMonitor = _monitorService.FindBestMatchingMonitor(app.Placement.MonitorDeviceId, app.Placement.MonitorBounds);
                        bool placed = _windowService.SetPlacement(targetHwnd, app.Placement, targetMonitor);
                        item.Status = placed ? "LaunchedAndPlaced" : "LaunchedWithoutPlacement";
                        successCount++;
                    }
                    else
                    {
                        item.Status = "LaunchedAndPlaced";
                        successCount++;
                    }
                }
                else
                {
                    item.Status = "LaunchedWithoutPlacement";
                    item.ErrorMessage = "Process launched, but window readiness timed out.";
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

    private IntPtr FindUnclaimedWindow(string processName, string? executablePath, string? expectedTitle, HashSet<IntPtr> claimedHwnds)
    {
        var windows = _windowService.EnumerateVisibleWindows();
        var cleanProc = Path.GetFileNameWithoutExtension(processName);

        // 1. Try title match first if expectedTitle is provided
        if (!string.IsNullOrEmpty(expectedTitle))
        {
            foreach (var w in windows)
            {
                if (claimedHwnds.Contains(w.Handle)) continue;

                bool procMatch = string.Equals(w.ProcessName, cleanProc, StringComparison.OrdinalIgnoreCase) ||
                                 (!string.IsNullOrEmpty(executablePath) && string.Equals(w.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
                if (!procMatch) continue;

                if (w.Title.Contains(expectedTitle, StringComparison.OrdinalIgnoreCase) ||
                    expectedTitle.Contains(w.Title, StringComparison.OrdinalIgnoreCase))
                {
                    return w.Handle;
                }
            }
        }

        // 2. If no expectedTitle, return first unclaimed window matching this process/executable
        if (string.IsNullOrEmpty(expectedTitle))
        {
            foreach (var w in windows)
            {
                if (claimedHwnds.Contains(w.Handle)) continue;

                bool procMatch = string.Equals(w.ProcessName, cleanProc, StringComparison.OrdinalIgnoreCase) ||
                                 (!string.IsNullOrEmpty(executablePath) && string.Equals(w.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
                if (procMatch)
                {
                    return w.Handle;
                }
            }
        }

        return IntPtr.Zero;
    }

    private static List<BrowserTabInfo> GetTargetTabs(WorkspaceApplication app)
    {
        var targetTabs = new List<BrowserTabInfo>();
        if (!string.IsNullOrWhiteSpace(app.BrowserTabsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<BrowserTabInfo>>(app.BrowserTabsJson);
                if (parsed != null && parsed.Count > 0)
                {
                    targetTabs.AddRange(parsed);
                }
            }
            catch { }
        }

        if (targetTabs.Count == 0 && !string.IsNullOrWhiteSpace(app.LaunchArguments))
        {
            var urls = ExtractUrlsFromArguments(app.LaunchArguments);
            foreach (var url in urls)
            {
                targetTabs.Add(new BrowserTabInfo { Url = url, Title = url });
            }
        }

        return targetTabs;
    }

    private static List<string> ExtractUrlsFromArguments(string? args)
    {
        var urls = new List<string>();
        if (string.IsNullOrWhiteSpace(args)) return urls;

        var matches = Regex.Matches(args, @"https?://[^\s\""]+");
        foreach (Match m in matches)
        {
            if (Uri.TryCreate(m.Value, UriKind.Absolute, out _))
            {
                urls.Add(m.Value);
            }
        }
        return urls;
    }

    private static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var u = url.Trim().ToLowerInvariant();
        if (u.StartsWith("https://")) u = u.Substring(8);
        else if (u.StartsWith("http://")) u = u.Substring(7);
        return u.TrimEnd('/');
    }

    private static bool IsTabAlreadyOpen(BrowserTabInfo target, HashSet<string> openUrls, HashSet<string> openTitles)
    {
        if (!string.IsNullOrWhiteSpace(target.Url))
        {
            string norm = NormalizeUrl(target.Url);
            if (openUrls.Contains(norm)) return true;
            if (openUrls.Any(ou => ou.Contains(norm) || norm.Contains(ou))) return true;
        }

        if (!string.IsNullOrWhiteSpace(target.Title))
        {
            string titleLower = target.Title.Trim().ToLowerInvariant();
            if (openTitles.Any(ot => ot.Contains(titleLower) || titleLower.Contains(ot))) return true;
        }

        return false;
    }

    private static string? ExtractProfileArg(string? browserProfile, string? launchArguments)
    {
        if (!string.IsNullOrWhiteSpace(browserProfile))
        {
            var match = Regex.Match(browserProfile, @"\(([^)]+)\)");
            string profileDir = match.Success ? match.Groups[1].Value : browserProfile.Trim();
            return $"--profile-directory=\"{profileDir}\"";
        }

        if (!string.IsNullOrWhiteSpace(launchArguments))
        {
            var match = Regex.Match(launchArguments, @"--profile-directory=""?([^""\s]+)""?");
            if (match.Success)
            {
                return match.Value;
            }
        }

        return null;
    }
}
