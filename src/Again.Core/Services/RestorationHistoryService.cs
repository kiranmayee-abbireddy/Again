using Again.Core.Domain;
using Again.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Again.Core.Services;

public interface IRestorationHistoryService
{
    Task<List<RestorationSession>> GetRecentHistoryAsync(int limit = 50);
    Task ClearHistoryAsync();
    Task<RestorationSession?> RetryFailedItemsAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

public class RestorationHistoryService : IRestorationHistoryService
{
    private readonly IWorkspaceRepository _repository;
    private readonly IWorkspaceRestoreService _restoreService;
    private readonly ILogger<RestorationHistoryService>? _logger;

    public RestorationHistoryService(
        IWorkspaceRepository repository,
        IWorkspaceRestoreService restoreService,
        ILogger<RestorationHistoryService>? logger = null)
    {
        _repository = repository;
        _restoreService = restoreService;
        _logger = logger;
    }

    public Task<List<RestorationSession>> GetRecentHistoryAsync(int limit = 50)
    {
        return _repository.GetRecentRestorationSessionsAsync(limit);
    }

    public Task ClearHistoryAsync()
    {
        return _repository.ClearRestorationHistoryAsync();
    }

    public async Task<RestorationSession?> RetryFailedItemsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var sessions = await _repository.GetRecentRestorationSessionsAsync(100);
        var targetSession = sessions.FirstOrDefault(s => s.Id == sessionId);
        if (targetSession == null) return null;

        var workspace = await _repository.GetWorkspaceByIdAsync(targetSession.WorkspaceId);
        if (workspace == null) return null;

        var failedPaths = targetSession.Items
            .Where(i => i.Status == "Failed")
            .Select(i => i.ApplicationPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var appsToRetry = workspace.Applications
            .Where(a => failedPaths.Contains(a.ExecutablePath))
            .Select(a => a.Id)
            .ToHashSet();

        if (appsToRetry.Count == 0) return null;

        var options = new RestorationOptions
        {
            SelectedApplicationIds = appsToRetry,
            LaunchMissing = true,
            RepositionExisting = true,
            SkipAlreadyRunning = false
        };

        return await _restoreService.RestoreWorkspaceAsync(workspace, options, cancellationToken: cancellationToken);
    }
}
