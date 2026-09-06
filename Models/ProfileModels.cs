namespace OpenMonitorManager.Models;

public sealed class MonitorProfile
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
    public List<MonitorProfileEntry> Monitors { get; set; } = [];

    public override string ToString() => Name;
}

public sealed class MonitorProfileEntry
{
    public string MonitorId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Brightness { get; set; }
    public int? Contrast { get; set; }
    public int? Red { get; set; }
    public int? Green { get; set; }
    public int? Blue { get; set; }
}
