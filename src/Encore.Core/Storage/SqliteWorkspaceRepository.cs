using Microsoft.Data.Sqlite;
using Encore.Core.Domain;
using Microsoft.Extensions.Logging;

namespace Encore.Core.Storage;

public class SqliteWorkspaceRepository : IWorkspaceRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteWorkspaceRepository>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _initialized;

    public SqliteWorkspaceRepository(string dbPath, ILogger<SqliteWorkspaceRepository>? logger = null)
    {
        _logger = logger;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await _semaphore.WaitAsync();
        try
        {
            if (_initialized) return;

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS workspaces (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    description TEXT,
                    category TEXT NOT NULL,
                    icon TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    last_restored_at TEXT,
                    is_pinned INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS workspace_applications (
                    id TEXT PRIMARY KEY,
                    workspace_id TEXT NOT NULL,
                    executable_path TEXT NOT NULL,
                    process_name TEXT NOT NULL,
                    window_title TEXT,
                    launch_arguments TEXT,
                    launch_order INTEGER NOT NULL DEFAULT 0,
                    launch_delay_ms INTEGER NOT NULL DEFAULT 0,
                    is_enabled INTEGER NOT NULL DEFAULT 1,
                    browser_profile TEXT,
                    browser_tabs_json TEXT,
                    folder_view_path TEXT,
                    FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS window_placements (
                    id TEXT PRIMARY KEY,
                    workspace_application_id TEXT NOT NULL,
                    x INTEGER NOT NULL,
                    y INTEGER NOT NULL,
                    width INTEGER NOT NULL,
                    height INTEGER NOT NULL,
                    window_state TEXT NOT NULL DEFAULT 'Normal',
                    monitor_device_id TEXT,
                    monitor_bounds TEXT,
                    FOREIGN KEY (workspace_application_id) REFERENCES workspace_applications(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS restoration_sessions (
                    id TEXT PRIMARY KEY,
                    workspace_id TEXT NOT NULL,
                    workspace_name TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT,
                    status TEXT NOT NULL,
                    duration_ms INTEGER
                );

                CREATE TABLE IF NOT EXISTS restoration_items (
                    id TEXT PRIMARY KEY,
                    session_id TEXT NOT NULL,
                    application_path TEXT NOT NULL,
                    process_name TEXT NOT NULL,
                    window_title TEXT,
                    status TEXT NOT NULL,
                    error_message TEXT,
                    completed_at TEXT,
                    FOREIGN KEY (session_id) REFERENCES restoration_sessions(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_workspaces_pinned ON workspaces(is_pinned);
                CREATE INDEX IF NOT EXISTS idx_workspace_apps ON workspace_applications(workspace_id);
                CREATE INDEX IF NOT EXISTS idx_window_placements ON window_placements(workspace_application_id);
                CREATE INDEX IF NOT EXISTS idx_restoration_sessions ON restoration_sessions(started_at DESC);
                CREATE INDEX IF NOT EXISTS idx_restoration_items ON restoration_items(session_id);
            ";

            await cmd.ExecuteNonQueryAsync();

            // Automatic migrations for existing databases
            try
            {
                using var mCmd = connection.CreateCommand();
                mCmd.CommandText = "ALTER TABLE workspace_applications ADD COLUMN browser_profile TEXT;";
                await mCmd.ExecuteNonQueryAsync();
            }
            catch { }

            try
            {
                using var mCmd = connection.CreateCommand();
                mCmd.CommandText = "ALTER TABLE workspace_applications ADD COLUMN browser_tabs_json TEXT;";
                await mCmd.ExecuteNonQueryAsync();
            }
            catch { }

            try
            {
                using var mCmd = connection.CreateCommand();
                mCmd.CommandText = "ALTER TABLE workspace_applications ADD COLUMN folder_view_path TEXT;";
                await mCmd.ExecuteNonQueryAsync();
            }
            catch { }

            _initialized = true;
            _logger?.LogInformation("SQLite database initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize SQLite database.");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<Workspace>> GetAllWorkspacesAsync()
    {
        await InitializeAsync();
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var workspaces = new List<Workspace>();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, name, description, category, icon, created_at, updated_at, last_restored_at, is_pinned FROM workspaces ORDER BY is_pinned DESC, updated_at DESC";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    workspaces.Add(new Workspace
                    {
                        Id = Guid.Parse(reader.GetString(0)),
                        Name = reader.GetString(1),
                        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Category = reader.GetString(3),
                        Icon = reader.IsDBNull(4) ? null : reader.GetString(4),
                        CreatedAt = DateTime.Parse(reader.GetString(5)),
                        UpdatedAt = DateTime.Parse(reader.GetString(6)),
                        LastRestoredAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
                        IsPinned = reader.GetInt32(8) == 1
                    });
                }
            }

            foreach (var ws in workspaces)
            {
                ws.Applications = await GetApplicationsForWorkspaceAsync(connection, ws.Id);
            }

            return workspaces;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Workspace?> GetWorkspaceByIdAsync(Guid id)
    {
        await InitializeAsync();
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            Workspace? ws = null;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, name, description, category, icon, created_at, updated_at, last_restored_at, is_pinned FROM workspaces WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id.ToString());
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    ws = new Workspace
                    {
                        Id = Guid.Parse(reader.GetString(0)),
                        Name = reader.GetString(1),
                        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Category = reader.GetString(3),
                        Icon = reader.IsDBNull(4) ? null : reader.GetString(4),
                        CreatedAt = DateTime.Parse(reader.GetString(5)),
                        UpdatedAt = DateTime.Parse(reader.GetString(6)),
                        LastRestoredAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
                        IsPinned = reader.GetInt32(8) == 1
                    };
                }
            }

            if (ws != null)
            {
                ws.Applications = await GetApplicationsForWorkspaceAsync(connection, ws.Id);
            }

            return ws;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<List<WorkspaceApplication>> GetApplicationsForWorkspaceAsync(SqliteConnection connection, Guid workspaceId)
    {
        var apps = new List<WorkspaceApplication>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT a.id, a.workspace_id, a.executable_path, a.process_name, a.window_title, 
                       a.launch_arguments, a.launch_order, a.launch_delay_ms, a.is_enabled,
                       a.browser_profile, a.browser_tabs_json, a.folder_view_path,
                       p.id, p.x, p.y, p.width, p.height, p.window_state, p.monitor_device_id, p.monitor_bounds
                FROM workspace_applications a
                LEFT JOIN window_placements p ON p.workspace_application_id = a.id
                WHERE a.workspace_id = @workspace_id
                ORDER BY a.launch_order ASC";
            cmd.Parameters.AddWithValue("@workspace_id", workspaceId.ToString());

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var app = new WorkspaceApplication
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    WorkspaceId = Guid.Parse(reader.GetString(1)),
                    ExecutablePath = reader.GetString(2),
                    ProcessName = reader.GetString(3),
                    WindowTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                    LaunchArguments = reader.IsDBNull(5) ? null : reader.GetString(5),
                    LaunchOrder = reader.GetInt32(6),
                    LaunchDelayMs = reader.GetInt32(7),
                    IsEnabled = reader.GetInt32(8) == 1,
                    BrowserProfile = reader.IsDBNull(9) ? null : reader.GetString(9),
                    BrowserTabsJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                    FolderViewPath = reader.IsDBNull(11) ? null : reader.GetString(11)
                };

                if (!reader.IsDBNull(12))
                {
                    app.Placement = new WindowPlacement
                    {
                        Id = Guid.Parse(reader.GetString(12)),
                        WorkspaceApplicationId = app.Id,
                        X = reader.GetInt32(13),
                        Y = reader.GetInt32(14),
                        Width = reader.GetInt32(15),
                        Height = reader.GetInt32(16),
                        WindowState = reader.GetString(17),
                        MonitorDeviceId = reader.IsDBNull(18) ? null : reader.GetString(18),
                        MonitorBounds = reader.IsDBNull(19) ? null : reader.GetString(19)
                    };
                }

                apps.Add(app);
            }
        }
        return apps;
    }

    public async Task SaveWorkspaceAsync(Workspace workspace)
    {
        await InitializeAsync();
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // 1. Upsert Workspace
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO workspaces (id, name, description, category, icon, created_at, updated_at, last_restored_at, is_pinned)
                        VALUES (@id, @name, @description, @category, @icon, @created_at, @updated_at, @last_restored_at, @is_pinned)
                        ON CONFLICT(id) DO UPDATE SET
                            name = @name,
                            description = @description,
                            category = @category,
                            icon = @icon,
                            updated_at = @updated_at,
                            last_restored_at = @last_restored_at,
                            is_pinned = @is_pinned;
                    ";
                    cmd.Parameters.AddWithValue("@id", workspace.Id.ToString());
                    cmd.Parameters.AddWithValue("@name", workspace.Name);
                    cmd.Parameters.AddWithValue("@description", (object?)workspace.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@category", workspace.Category);
                    cmd.Parameters.AddWithValue("@icon", (object?)workspace.Icon ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@created_at", workspace.CreatedAt.ToString("o"));
                    cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@last_restored_at", workspace.LastRestoredAt.HasValue ? workspace.LastRestoredAt.Value.ToString("o") : DBNull.Value);
                    cmd.Parameters.AddWithValue("@is_pinned", workspace.IsPinned ? 1 : 0);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 2. Clear existing apps and placements for this workspace to cleanly replace
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM workspace_applications WHERE workspace_id = @workspace_id";
                    cmd.Parameters.AddWithValue("@workspace_id", workspace.Id.ToString());
                    await cmd.ExecuteNonQueryAsync();
                }

                // 3. Insert applications and placements
                for (int i = 0; i < workspace.Applications.Count; i++)
                {
                    var app = workspace.Applications[i];
                    app.LaunchOrder = i;
                    app.WorkspaceId = workspace.Id;

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"
                            INSERT INTO workspace_applications 
                            (id, workspace_id, executable_path, process_name, window_title, launch_arguments, launch_order, launch_delay_ms, is_enabled, browser_profile, browser_tabs_json, folder_view_path)
                            VALUES (@id, @workspace_id, @executable_path, @process_name, @window_title, @launch_arguments, @launch_order, @launch_delay_ms, @is_enabled, @browser_profile, @browser_tabs_json, @folder_view_path);
                        ";
                        cmd.Parameters.AddWithValue("@id", app.Id.ToString());
                        cmd.Parameters.AddWithValue("@workspace_id", workspace.Id.ToString());
                        cmd.Parameters.AddWithValue("@executable_path", app.ExecutablePath);
                        cmd.Parameters.AddWithValue("@process_name", app.ProcessName);
                        cmd.Parameters.AddWithValue("@window_title", (object?)app.WindowTitle ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@launch_arguments", (object?)app.LaunchArguments ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@launch_order", app.LaunchOrder);
                        cmd.Parameters.AddWithValue("@launch_delay_ms", app.LaunchDelayMs);
                        cmd.Parameters.AddWithValue("@is_enabled", app.IsEnabled ? 1 : 0);
                        cmd.Parameters.AddWithValue("@browser_profile", (object?)app.BrowserProfile ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@browser_tabs_json", (object?)app.BrowserTabsJson ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@folder_view_path", (object?)app.FolderViewPath ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    if (app.Placement != null)
                    {
                        var p = app.Placement;
                        p.WorkspaceApplicationId = app.Id;

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = @"
                                INSERT INTO window_placements 
                                (id, workspace_application_id, x, y, width, height, window_state, monitor_device_id, monitor_bounds)
                                VALUES (@id, @app_id, @x, @y, @width, @height, @window_state, @monitor_device_id, @monitor_bounds);
                            ";
                            cmd.Parameters.AddWithValue("@id", p.Id.ToString());
                            cmd.Parameters.AddWithValue("@app_id", app.Id.ToString());
                            cmd.Parameters.AddWithValue("@x", p.X);
                            cmd.Parameters.AddWithValue("@y", p.Y);
                            cmd.Parameters.AddWithValue("@width", p.Width);
                            cmd.Parameters.AddWithValue("@height", p.Height);
                            cmd.Parameters.AddWithValue("@window_state", p.WindowState);
                            cmd.Parameters.AddWithValue("@monitor_device_id", (object?)p.MonitorDeviceId ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@monitor_bounds", (object?)p.MonitorBounds ?? DBNull.Value);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }

                transaction.Commit();
                _logger?.LogInformation("Saved workspace {WorkspaceId} ({Name}) successfully.", workspace.Id, workspace.Name);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DeleteWorkspaceAsync(Guid id)
    {
        await InitializeAsync();
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM workspaces WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id.ToString());
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UpdatePinStatusAsync(Guid id, bool isPinned)
    {
        await InitializeAsync();
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE workspaces SET is_pinned = @is_pinned, updated_at = @updated_at WHERE id = @id";
            cmd.Parameters.AddWithValue("@is_pinned", isPinned ? 1 : 0);
            cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@id", id.ToString());
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UpdateLastRestoredAtAsync(Guid id, DateTime timestamp)
    {
        await InitializeAsync();
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE workspaces SET last_restored_at = @last_restored_at WHERE id = @id";
            cmd.Parameters.AddWithValue("@last_restored_at", timestamp.ToString("o"));
            cmd.Parameters.AddWithValue("@id", id.ToString());
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Workspace> DuplicateWorkspaceAsync(Guid id, string newName)
    {
        var existing = await GetWorkspaceByIdAsync(id);
        if (existing == null)
            throw new KeyNotFoundException($"Workspace {id} not found.");

        var copy = existing.Clone(newName);
        await SaveWorkspaceAsync(copy);
        return copy;
    }

    public async Task SaveRestorationSessionAsync(RestorationSession session)
    {
        await InitializeAsync();
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO restoration_sessions (id, workspace_id, workspace_name, started_at, completed_at, status, duration_ms)
                        VALUES (@id, @workspace_id, @workspace_name, @started_at, @completed_at, @status, @duration_ms)
                        ON CONFLICT(id) DO UPDATE SET
                            completed_at = @completed_at,
                            status = @status,
                            duration_ms = @duration_ms;
                    ";
                    cmd.Parameters.AddWithValue("@id", session.Id.ToString());
                    cmd.Parameters.AddWithValue("@workspace_id", session.WorkspaceId.ToString());
                    cmd.Parameters.AddWithValue("@workspace_name", session.WorkspaceName);
                    cmd.Parameters.AddWithValue("@started_at", session.StartedAt.ToString("o"));
                    cmd.Parameters.AddWithValue("@completed_at", session.CompletedAt.HasValue ? session.CompletedAt.Value.ToString("o") : DBNull.Value);
                    cmd.Parameters.AddWithValue("@status", session.Status);
                    cmd.Parameters.AddWithValue("@duration_ms", (object?)session.DurationMs ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }

                foreach (var item in session.Items)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO restoration_items (id, session_id, application_path, process_name, window_title, status, error_message, completed_at)
                        VALUES (@id, @session_id, @app_path, @proc_name, @title, @status, @error, @completed_at)
                        ON CONFLICT(id) DO UPDATE SET
                            status = @status,
                            error_message = @error,
                            completed_at = @completed_at;
                    ";
                    cmd.Parameters.AddWithValue("@id", item.Id.ToString());
                    cmd.Parameters.AddWithValue("@session_id", session.Id.ToString());
                    cmd.Parameters.AddWithValue("@app_path", item.ApplicationPath);
                    cmd.Parameters.AddWithValue("@proc_name", item.ProcessName);
                    cmd.Parameters.AddWithValue("@title", (object?)item.WindowTitle ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@status", item.Status);
                    cmd.Parameters.AddWithValue("@error", (object?)item.ErrorMessage ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@completed_at", item.CompletedAt.HasValue ? item.CompletedAt.Value.ToString("o") : DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<RestorationSession>> GetRecentRestorationSessionsAsync(int limit = 50)
    {
        await InitializeAsync();
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sessions = new List<RestorationSession>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT id, workspace_id, workspace_name, started_at, completed_at, status, duration_ms
                    FROM restoration_sessions
                    ORDER BY started_at DESC
                    LIMIT @limit";
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    sessions.Add(new RestorationSession
                    {
                        Id = Guid.Parse(reader.GetString(0)),
                        WorkspaceId = Guid.Parse(reader.GetString(1)),
                        WorkspaceName = reader.GetString(2),
                        StartedAt = DateTime.Parse(reader.GetString(3)),
                        CompletedAt = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                        Status = reader.GetString(5),
                        DurationMs = reader.IsDBNull(6) ? null : reader.GetInt64(6)
                    });
                }
            }

            foreach (var session in sessions)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, session_id, application_path, process_name, window_title, status, error_message, completed_at
                    FROM restoration_items
                    WHERE session_id = @session_id";
                cmd.Parameters.AddWithValue("@session_id", session.Id.ToString());

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    session.Items.Add(new RestorationItem
                    {
                        Id = Guid.Parse(reader.GetString(0)),
                        SessionId = Guid.Parse(reader.GetString(1)),
                        ApplicationPath = reader.GetString(2),
                        ProcessName = reader.GetString(3),
                        WindowTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Status = reader.GetString(5),
                        ErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
                        CompletedAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7))
                    });
                }
            }

            return sessions;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ClearRestorationHistoryAsync()
    {
        await InitializeAsync();
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM restoration_sessions";
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
