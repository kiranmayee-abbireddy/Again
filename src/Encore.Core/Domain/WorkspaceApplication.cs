namespace Encore.Core.Domain;

public class WorkspaceApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string? WindowTitle { get; set; }
    public string? LaunchArguments { get; set; }
    public int LaunchOrder { get; set; }
    public int LaunchDelayMs { get; set; }
    public bool IsEnabled { get; set; } = true;
    
    public WindowPlacement? Placement { get; set; }

    public WorkspaceApplication Clone(Guid newWorkspaceId)
    {
        return new WorkspaceApplication
        {
            Id = Guid.NewGuid(),
            WorkspaceId = newWorkspaceId,
            ExecutablePath = ExecutablePath,
            ProcessName = ProcessName,
            WindowTitle = WindowTitle,
            LaunchArguments = LaunchArguments,
            LaunchOrder = LaunchOrder,
            LaunchDelayMs = LaunchDelayMs,
            IsEnabled = IsEnabled,
            Placement = Placement?.Clone()
        };
    }
}
