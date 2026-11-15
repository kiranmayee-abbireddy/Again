using Encore.Core.Domain;

namespace Encore.Core.Storage;

public interface IWorkspaceRepository
{
    Task InitializeAsync();
    Task<List<Workspace>> GetAllWorkspacesAsync();
    Task<Workspace?> GetWorkspaceByIdAsync(Guid id);
    Task SaveWorkspaceAsync(Workspace workspace);
    Task DeleteWorkspaceAsync(Guid id);
    Task UpdatePinStatusAsync(Guid id, bool isPinned);
    Task UpdateLastRestoredAtAsync(Guid id, DateTime timestamp);
    Task<Workspace> DuplicateWorkspaceAsync(Guid id, string newName);

    Task SaveRestorationSessionAsync(RestorationSession session);
    Task<List<RestorationSession>> GetRecentRestorationSessionsAsync(int limit = 50);
    Task ClearRestorationHistoryAsync();
}
