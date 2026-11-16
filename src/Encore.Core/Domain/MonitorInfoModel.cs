namespace Encore.Core.Domain;

public class MonitorInfoModel
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public int WorkLeft { get; set; }
    public int WorkTop { get; set; }
    public int WorkRight { get; set; }
    public int WorkBottom { get; set; }
    
    public int WorkWidth => WorkRight - WorkLeft;
    public int WorkHeight => WorkBottom - WorkTop;

    public bool IsPrimary { get; set; }
    public uint DpiX { get; set; } = 96;
    public uint DpiY { get; set; } = 96;
    public double ScaleFactor => DpiX > 0 ? DpiX / 96.0 : 1.0;

    public string BoundsString => $"{Left},{Top},{Width},{Height}";

    public string FriendlyName => FormatFriendlyName(DeviceName, IsPrimary);

    public static string FormatFriendlyName(string? rawName, bool isPrimary = false)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return isPrimary ? "Primary Display" : "Display 1";
        }

        var s = rawName.Trim();
        // Remove Win32 device namespace prefix \\.\
        if (s.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(4);
        }

        // Convert DISPLAY1 -> Display 1, DISPLAY2 -> Display 2
        if (s.StartsWith("DISPLAY", StringComparison.OrdinalIgnoreCase) && 
            int.TryParse(s.Substring(7), out int num))
        {
            return $"Display {num}";
        }

        if (s.StartsWith("DISPLAY", StringComparison.OrdinalIgnoreCase))
        {
            return "Display " + s.Substring(7);
        }

        return s;
    }
}
