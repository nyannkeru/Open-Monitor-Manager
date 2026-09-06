namespace OpenMonitorManager.Models;

public sealed record MonitorSnapshot(
    string Id,
    string LogicalDevice,
    string Description,
    string DeviceId,
    int PhysicalIndex,
    bool SupportsBrightness,
    int Brightness,
    bool SupportsContrast,
    int Contrast,
    bool SupportsRgbGain,
    int Red,
    int Green,
    int Blue)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Description)
        ? $"{LogicalDevice} #{PhysicalIndex + 1}"
        : $"{Description} — {LogicalDevice}";

    public string Details => string.IsNullOrWhiteSpace(DeviceId) ? LogicalDevice : DeviceId;

    public override string ToString() => DisplayName;
}

public sealed record MonitorOperationResult(bool Success, string Message)
{
    public static MonitorOperationResult Ok(string message) => new(true, message);
    public static MonitorOperationResult Fail(string message) => new(false, message);
}
