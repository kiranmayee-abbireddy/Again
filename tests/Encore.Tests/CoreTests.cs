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
            Assert.Equal("Dark", loadedDefaults.Theme);

            loadedDefaults.Theme = "Light";
            loadedDefaults.MinimizeToTrayOnClose = false;
            await configRepo.SaveSettingsAsync(loadedDefaults);

            var reloaded = await configRepo.LoadSettingsAsync();
            Assert.Equal("Light", reloaded.Theme);
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
}
