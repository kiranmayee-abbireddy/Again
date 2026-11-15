namespace Encore.Core.Domain;

public class WindowPlacement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceApplicationId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string WindowState { get; set; } = "Normal"; // Normal, Minimized, Maximized
    public string? MonitorDeviceId { get; set; }
    public string? MonitorBounds { get; set; } // "left,top,width,height"

    public WindowPlacement Clone()
    {
        return new WindowPlacement
        {
            Id = Guid.NewGuid(),
            WorkspaceApplicationId = WorkspaceApplicationId,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            WindowState = WindowState,
            MonitorDeviceId = MonitorDeviceId,
            MonitorBounds = MonitorBounds
        };
    }
}
