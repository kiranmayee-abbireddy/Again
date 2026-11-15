namespace Encore.Core.Domain;

public class RestorationSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "Running"; // Running, Success, Partial, Failed, Cancelled
    public long? DurationMs { get; set; }
    
    public List<RestorationItem> Items { get; set; } = new();
}

public class RestorationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public string ApplicationPath { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string? WindowTitle { get; set; }
    public string Status { get; set; } = "Pending"; // LaunchedAndPlaced, RepositionedOnly, AlreadyRunning, Skipped, Failed
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
}
