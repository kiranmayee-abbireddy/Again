using System.IO;
using Encore.Core.Domain;
using Encore.Core.Services;
using Encore.Core.Storage;
using Xunit;

namespace Encore.Tests;

public class DomainModelTests
{
    [Fact]
    public void Workspace_Clone_CreatesDeepCopy()
    {
        var original = new Workspace
        {
            Name = "Dev Env",
            Category = "Development",
            Description = "Code + Tools",
            IsPinned = true,
            Applications = new List<WorkspaceApplication>
            {
                new()
                {
                    ExecutablePath = @"C:\tools\app.exe",
                    ProcessName = "app",
                    WindowTitle = "My Editor",
                    LaunchOrder = 0,
                    Placement = new WindowPlacement
                    {
                        X = 100,
                        Y = 200,
                        Width = 800,
                        Height = 600,
                        WindowState = "Normal",
                        MonitorDeviceId = "DISPLAY1"
                    }
                }
            }
        };

        var clone = original.Clone("Dev Env Copy");

        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equal("Dev Env Copy", clone.Name);
        Assert.Equal(original.Category, clone.Category);
        Assert.Equal(original.Description, clone.Description);
        Assert.False(clone.IsPinned);
        Assert.Single(clone.Applications);

        var clonedApp = clone.Applications[0];
        Assert.NotEqual(original.Applications[0].Id, clonedApp.Id);
        Assert.Equal(clone.Id, clonedApp.WorkspaceId);
        Assert.Equal(@"C:\tools\app.exe", clonedApp.ExecutablePath);
        Assert.NotNull(clonedApp.Placement);
        Assert.NotEqual(original.Applications[0].Placement!.Id, clonedApp.Placement.Id);
        Assert.Equal(100, clonedApp.Placement.X);
    }
}

public class StorageTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteWorkspaceRepository _repo;

    public StorageTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"encore_test_{Guid.NewGuid():N}.db");
        _repo = new SqliteWorkspaceRepository(_tempDbPath);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }
        catch { }
    }

    [Fact]
    public async Task Sqlite_SaveAndRetrieveWorkspace_PreservesAllData()
    {
        var ws = new Workspace
        {
            Name = "Meeting Workspace",
            Category = "Meetings",
            Description = "Browser and Teams",
            IsPinned = true,
            Applications = new List<WorkspaceApplication>
            {
                new()
                {
                    ExecutablePath = @"C:\Program Files\App\app.exe",
                    ProcessName = "app",
                    WindowTitle = "Team Meeting",
                    LaunchArguments = "--meeting-id 123",
                    LaunchOrder = 0,
                    LaunchDelayMs = 500,
                    IsEnabled = true,
                    Placement = new WindowPlacement
                    {
                        X = 50,
                        Y = 50,
                        Width = 1200,
                        Height = 800,
                        WindowState = "Normal",
                        MonitorDeviceId = @"\\.\DISPLAY1",
                        MonitorBounds = "0,0,1920,1080"
                    }
                }
            }
        };

        await _repo.SaveWorkspaceAsync(ws);

        var retrieved = await _repo.GetWorkspaceByIdAsync(ws.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Meeting Workspace", retrieved.Name);
        Assert.Equal("Meetings", retrieved.Category);
        Assert.True(retrieved.IsPinned);
        Assert.Single(retrieved.Applications);

        var app = retrieved.Applications[0];
        Assert.Equal(@"C:\Program Files\App\app.exe", app.ExecutablePath);
        Assert.Equal("--meeting-id 123", app.LaunchArguments);
        Assert.NotNull(app.Placement);
        Assert.Equal(1200, app.Placement.Width);
        Assert.Equal(@"\\.\DISPLAY1", app.Placement.MonitorDeviceId);

        // Update pin status
        await _repo.UpdatePinStatusAsync(ws.Id, false);
        var updated = await _repo.GetWorkspaceByIdAsync(ws.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsPinned);

        // Duplicate
        var duplicated = await _repo.DuplicateWorkspaceAsync(ws.Id, "Meeting Workspace Duplicated");
        Assert.NotNull(duplicated);
        Assert.Equal("Meeting Workspace Duplicated", duplicated.Name);
        Assert.Single(duplicated.Applications);

        // Delete
        await _repo.DeleteWorkspaceAsync(ws.Id);
        var deleted = await _repo.GetWorkspaceByIdAsync(ws.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task Sqlite_RestorationSessions_RecordAndRetrieve()
    {
        var session = new RestorationSession
        {
            WorkspaceId = Guid.NewGuid(),
            WorkspaceName = "Design Setup",
            StartedAt = DateTime.UtcNow.AddSeconds(-5),
            CompletedAt = DateTime.UtcNow,
            Status = "Success",
            DurationMs = 1500,
            Items = new List<RestorationItem>
            {
                new()
                {
                    ApplicationPath = @"C:\Apps\design.exe",
                    ProcessName = "design",
                    Status = "LaunchedAndPlaced"
                },
                new()
                {
                    ApplicationPath = @"C:\Apps\browser.exe",
                    ProcessName = "browser",
                    Status = "RepositionedOnly"
                }
            }
        };

        await _repo.SaveRestorationSessionAsync(session);

        var history = await _repo.GetRecentRestorationSessionsAsync(10);
        Assert.NotEmpty(history);
        var retrieved = history.First(h => h.Id == session.Id);
        Assert.Equal("Design Setup", retrieved.WorkspaceName);
        Assert.Equal("Success", retrieved.Status);
        Assert.Equal(2, retrieved.Items.Count);
    }

    [Fact]
    public async Task ConfigurationRepository_LoadAndSave_WorksCorrectly()
    {
        var tempSettingsPath = Path.Combine(Path.GetTempPath(), $"encore_settings_{Guid.NewGuid():N}.json");
        try
        {
            var configRepo = new ConfigurationRepository(tempSettingsPath);
            var loadedDefaults = await configRepo.LoadSettingsAsync();
            Assert.NotNull(loadedDefaults);
            Assert.True(loadedDefaults.MinimizeToTrayOnClose);
            Assert.Equal("Emerald (Dark)", loadedDefaults.Theme);

            loadedDefaults.Theme = "Nordic Teal (Dark)";
            loadedDefaults.MinimizeToTrayOnClose = false;
            await configRepo.SaveSettingsAsync(loadedDefaults);

            var reloaded = await configRepo.LoadSettingsAsync();
            Assert.Equal("Nordic Teal (Dark)", reloaded.Theme);
            Assert.False(reloaded.MinimizeToTrayOnClose);
        }
        finally
        {
            if (File.Exists(tempSettingsPath))
                File.Delete(tempSettingsPath);
        }
    }

    [Fact]
    public async Task WorkspaceRestoreService_HandlesMissingExecutableGracefully()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"encore_test_{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteWorkspaceRepository(tempDbPath);
            var monitorService = new WindowsMonitorService();
            var procService = new WindowsProcessService();
            var windowService = new WindowsWindowService(procService, monitorService);
            var restoreService = new WorkspaceRestoreService(procService, windowService, monitorService, repo);

            var ws = new Workspace
            {
                Name = "Missing App Test",
                Applications = new List<WorkspaceApplication>
                {
                    new()
                    {
                        ExecutablePath = @"C:\NonExistentDirectory\FakeApp12345.exe",
                        ProcessName = "FakeApp12345",
                        IsEnabled = true
                    }
                }
            };
            await repo.SaveWorkspaceAsync(ws);

            var session = await restoreService.RestoreWorkspaceAsync(ws);

            Assert.Equal("Failed", session.Status);
            Assert.Single(session.Items);
            Assert.Equal("Failed", session.Items[0].Status);
            Assert.Contains("not found", session.Items[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbPath))
                File.Delete(tempDbPath);
        }
    }
}

public class MonitorAdjustmentTests
{
    [Fact]
    public void CoordinateAdjustment_ClampsToVisibleWorkArea()
    {
        var monitorService = new WindowsMonitorService();

        // Pass coordinates outside the normal screen
        var (x, y, w, h) = monitorService.AdjustCoordinates(-5000, -5000, 1000, 800, null, null);

        Assert.True(x >= 0);
        Assert.True(y >= 0);
        Assert.True(w >= 200);
        Assert.True(h >= 150);
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY1", false, "Display 1")]
    [InlineData(@"\\.\DISPLAY2", false, "Display 2")]
    [InlineData(@"DISPLAY1", false, "Display 1")]
    [InlineData(null, true, "Primary Display")]
    [InlineData("", false, "Display 1")]
    [InlineData("Primary Display", true, "Primary Display")]
    public void FormatFriendlyName_CleansWin32DeviceNamespace(string? input, bool isPrimary, string expected)
    {
        var result = MonitorInfoModel.FormatFriendlyName(input, isPrimary);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MonitorInfoModel_FriendlyName_ResolvesCleanlyFromDeviceId()
    {
        var model = new MonitorInfoModel
        {
            DeviceId = @"\\.\DISPLAY1",
            DeviceName = ""
        };
        Assert.Equal("Display 1", model.FriendlyName);
    }
}

public class ShortcutParsingTests
{
    [Theory]
    [InlineData("Ctrl+Alt+C", true)]
    [InlineData("Ctrl+Shift+R", true)]
    [InlineData("Win+Alt+W", true)]
    [InlineData("Alt+E", true)]
    [InlineData("", false)]
    public void TryParseShortcut_ValidatesCorrectly(string shortcut, bool expectedSuccess)
    {
        bool success = WindowsShortcutService.TryParseShortcut(shortcut, out uint modifiers, out uint vk);
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.True(vk != 0);
        }
    }
}

public class MultiProcessWindowServiceTests
{
    [Fact]
    public void WindowService_FindTopLevelWindow_HandlesNonExistentProcessesCleanly()
    {
        var procService = new WindowsProcessService();
        var monService = new WindowsMonitorService();
        var winService = new WindowsWindowService(procService, monService);

        IntPtr hwnd = winService.FindTopLevelWindow("NonExistentApp999", null, "Some Title");
        Assert.Equal(IntPtr.Zero, hwnd);

        bool hasWin = winService.HasVisibleTopLevelWindow("NonExistentApp999");
        Assert.False(hasWin);
    }

    [Fact]
    public void WindowService_SetPlacement_HandlesZeroHandleGracefully()
    {
        var procService = new WindowsProcessService();
        var monService = new WindowsMonitorService();
        var winService = new WindowsWindowService(procService, monService);

        var placement = new WindowPlacement { X = 100, Y = 100, Width = 800, Height = 600, WindowState = "Normal" };
        bool result = winService.SetPlacement(IntPtr.Zero, placement, null);
        Assert.False(result);
    }

    [Fact]
    public void ChromiumBrowserService_IdentifiesSupportedBrowsers()
    {
        var service = new ChromiumBrowserService();

        Assert.True(service.IsSupportedBrowser(@"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.True(service.IsSupportedBrowser(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"));
        Assert.True(service.IsSupportedBrowser(@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"));
        Assert.False(service.IsSupportedBrowser(@"C:\Windows\notepad.exe"));
        Assert.False(service.IsSupportedBrowser(@"C:\Program Files\Git\bin\bash.exe"));
    }

    [Fact]
    public void ChromiumBrowserService_LoadsProfiles_WhenPresent()
    {
        var service = new ChromiumBrowserService();
        string chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        if (File.Exists(chromePath))
        {
            var profiles = service.GetProfiles(chromePath);
            Assert.NotEmpty(profiles);
            Assert.Contains(profiles, p => !string.IsNullOrEmpty(p.DirectoryName));
        }
    }

    [Fact]
    public async Task Sqlite_SaveAndRetrieve_BrowserProfileAndTabs_Preserved()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"encore_browser_test_{Guid.NewGuid():N}.db");
        var repo = new SqliteWorkspaceRepository(tempDb);

        try
        {
            var ws = new Workspace
            {
                Name = "Chrome Research Setup",
                Category = "Development",
                Applications = new List<WorkspaceApplication>
                {
                    new()
                    {
                        ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                        ProcessName = "chrome",
                        WindowTitle = "AI & ML Papers — Google Chrome",
                        BrowserProfile = "Kiranmayee (Profile 1)",
                        BrowserTabsJson = "[{\"Title\":\"arXiv:2301.00001\",\"Url\":\"https://arxiv.org/abs/2301.00001\"},{\"Title\":\"GitHub\",\"Url\":\"https://github.com\"}]",
                        LaunchArguments = "--profile-directory=\"Profile 1\" --new-window \"https://arxiv.org/abs/2301.00001\" \"https://github.com\"",
                        Placement = new WindowPlacement { X = 0, Y = 0, Width = 1920, Height = 1080, WindowState = "Normal" }
                    }
                }
            };

            await repo.SaveWorkspaceAsync(ws);

            var loaded = await repo.GetWorkspaceByIdAsync(ws.Id);
            Assert.NotNull(loaded);
            Assert.Single(loaded.Applications);

            var app = loaded.Applications[0];
            Assert.Equal("Kiranmayee (Profile 1)", app.BrowserProfile);
            Assert.NotNull(app.BrowserTabsJson);
            Assert.Contains("arXiv", app.BrowserTabsJson);
            Assert.Contains("--profile-directory=\"Profile 1\"", app.LaunchArguments);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) File.Delete(tempDb);
        }
    }

    [Fact]
    public async Task WorkspaceCaptureService_CapturesChromeWithProfileAndTabs_IfRunning()
    {
        var procService = new WindowsProcessService();
        var monService = new WindowsMonitorService();
        var winService = new WindowsWindowService(procService, monService);
        var browserService = new ChromiumBrowserService();
        var explorerService = new WindowsExplorerService();
        string tempDb = Path.Combine(Path.GetTempPath(), $"test_capture_{Guid.NewGuid():N}.db");
        var repo = new SqliteWorkspaceRepository(tempDb);

        try
        {
            var captureService = new WorkspaceCaptureService(winService, browserService, explorerService, procService, repo);
            var apps = await captureService.CaptureCurrentWindowsAsync();
            Assert.NotNull(apps);

            var chromeApp = apps.FirstOrDefault(a => a.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase));
            if (chromeApp != null)
            {
                Assert.True(!string.IsNullOrEmpty(chromeApp.BrowserProfile) || !string.IsNullOrEmpty(chromeApp.LaunchArguments));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) File.Delete(tempDb);
        }
    }

    [Fact]
    public void WindowsExplorerService_IsExplorer_IdentifiesProcessAndClass()
    {
        var service = new WindowsExplorerService();
        Assert.True(service.IsExplorer("explorer", "CabinetWClass", @"C:\Windows\explorer.exe"));
        Assert.True(service.IsExplorer("other", "CabinetWClass", ""));
        Assert.True(service.IsExplorer("explorer", "", ""));
        Assert.True(service.IsExplorer("", "", @"C:\Windows\explorer.exe"));
        Assert.False(service.IsExplorer("notepad", "Notepad", @"C:\Windows\notepad.exe"));
    }

    [Fact]
    public void WindowsExplorerService_FormatLaunchArguments_WorksCorrectly()
    {
        var service = new WindowsExplorerService();
        Assert.Equal("\"D:\\projects\\Encore\"", service.FormatLaunchArguments(@"D:\projects\Encore"));
        Assert.Equal(string.Empty, service.FormatLaunchArguments("Home"));
        Assert.Equal(string.Empty, service.FormatLaunchArguments(""));
    }

    [Fact]
    public void WindowsProcessService_ExtractLaunchArguments_ExtractsCorrectArgs()
    {
        var service = new WindowsProcessService();
        string exe = @"C:\Program Files\Microsoft VS Code\Code.exe";
        string cmdLine = "\"C:\\Program Files\\Microsoft VS Code\\Code.exe\" --goto \"D:\\projects\\Encore\\src\\Program.cs:10\"";

        string? args = service.ExtractLaunchArguments(exe, cmdLine);
        Assert.NotNull(args);
        Assert.Contains("--goto", args);
        Assert.Contains("Program.cs:10", args);
    }

    [Fact]
    public async Task Sqlite_SaveAndRetrieve_FolderViewPath_Preserved()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"encore_folder_test_{Guid.NewGuid():N}.db");
        var repo = new SqliteWorkspaceRepository(tempDb);

        try
        {
            var ws = new Workspace
            {
                Name = "File Explorer Work Setup",
                Category = "Work",
                Applications = new List<WorkspaceApplication>
                {
                    new()
                    {
                        ExecutablePath = @"C:\Windows\explorer.exe",
                        ProcessName = "explorer",
                        WindowTitle = "Encore — File Explorer",
                        FolderViewPath = @"D:\projects\Encore",
                        LaunchArguments = "\"D:\\projects\\Encore\"",
                        Placement = new WindowPlacement { X = 50, Y = 50, Width = 1200, Height = 800, WindowState = "Normal" }
                    }
                }
            };

            await repo.SaveWorkspaceAsync(ws);

            var loaded = await repo.GetWorkspaceByIdAsync(ws.Id);
            Assert.NotNull(loaded);
            Assert.Single(loaded.Applications);

            var app = loaded.Applications[0];
            Assert.Equal(@"D:\projects\Encore", app.FolderViewPath);
            Assert.Equal("\"D:\\projects\\Encore\"", app.LaunchArguments);
            Assert.Equal("Encore — File Explorer", app.WindowTitle);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) File.Delete(tempDb);
        }
    }

    [Fact]
    public async Task ApplicationCatalogService_DiscoversInstalledApps()
    {
        var catalogService = new ApplicationCatalogService();
        var apps = await catalogService.GetInstalledApplicationsAsync();

        Assert.NotNull(apps);
        Assert.NotEmpty(apps);

        // At least system apps like notepad or explorer should be present
        var hasNotepadOrExplorer = apps.Any(a => a.ProcessName.Equals("notepad", StringComparison.OrdinalIgnoreCase) ||
                                                 a.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                                                 a.ProcessName.Equals("cmd", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasNotepadOrExplorer);
    }

    [Fact]
    public void ApplicationCatalogService_ResolvesExecutablePath_FindsKnownSystemExe()
    {
        var catalogService = new ApplicationCatalogService();
        var resolved = catalogService.ResolveExecutablePath("notepad.exe");

        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved));
        Assert.EndsWith("notepad.exe", resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplicationCatalogService_GetAppGlyph_ReturnsAppropriateGlyph()
    {
        Assert.Equal("🌐", ApplicationCatalogService.GetAppGlyph("chrome"));
        Assert.Equal("💻", ApplicationCatalogService.GetAppGlyph("Code"));
        Assert.Equal("📁", ApplicationCatalogService.GetAppGlyph("explorer"));
        Assert.Equal("⚡", ApplicationCatalogService.GetAppGlyph("powershell"));
        Assert.Equal("📝", ApplicationCatalogService.GetAppGlyph("notepad"));
    }

    [Fact]
    public async Task WorkspaceRestoreService_DoesNotSpawnDuplicate_WhenWindowAlreadyExists()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"encore_test_{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteWorkspaceRepository(tempDbPath);
            var monitorService = new WindowsMonitorService();
            var procService = new WindowsProcessService();
            var windowService = new WindowsWindowService(procService, monitorService);
            var restoreService = new WorkspaceRestoreService(procService, windowService, monitorService, repo);

            // Find an actual visible window currently running on this test runner (e.g. explorer or desktop process)
            var visibleWindows = windowService.EnumerateVisibleWindows();
            var existing = visibleWindows.FirstOrDefault(w => !string.IsNullOrEmpty(w.ExecutablePath) && File.Exists(w.ExecutablePath));

            if (existing != null)
            {
                var ws = new Workspace
                {
                    Name = "Existing App Re-use Test",
                    Applications = new List<WorkspaceApplication>
                    {
                        new()
                        {
                            ExecutablePath = existing.ExecutablePath,
                            ProcessName = existing.ProcessName,
                            WindowTitle = existing.Title,
                            IsEnabled = true,
                            Placement = new WindowPlacement
                            {
                                X = existing.Bounds.Left,
                                Y = existing.Bounds.Top,
                                Width = existing.Bounds.Width,
                                Height = existing.Bounds.Height,
                                WindowState = existing.WindowState
                            }
                        }
                    }
                };

                await repo.SaveWorkspaceAsync(ws);
                var session = await restoreService.RestoreWorkspaceAsync(ws);

                Assert.Equal("Success", session.Status);
                Assert.Single(session.Items);
                // When an existing window is present, it should be repositioned rather than launched as a duplicate instance
                Assert.True(session.Items[0].Status is "RepositionedOnly" or "AlreadyRunning");
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbPath)) File.Delete(tempDbPath);
        }
    }
}
