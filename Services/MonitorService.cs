using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using OpenMonitorManager.Interop;
using OpenMonitorManager.Models;

namespace OpenMonitorManager.Services;

public sealed class MonitorService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _snapshotGate = new();
    private List<MonitorDevice> _devices = [];
    private bool _disposed;

    public event EventHandler<MonitorsChangedEventArgs>? MonitorsChanged;

    public IReadOnlyList<MonitorSnapshot> GetSnapshots()
    {
        lock (_snapshotGate)
        {
            return _devices.Select(static device => device.ToSnapshot()).ToArray();
        }
    }

    public void PublishSnapshots(string reason) => RaiseChanged(reason);

    public Task RefreshAsync(string reason, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            _gate.Wait(cancellationToken);
            try
            {
                ThrowIfDisposed();
                RefreshCore(reason);
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken);

    public Task<MonitorOperationResult> SetBrightnessAsync(
        string monitorId,
        int percent,
        CancellationToken cancellationToken = default) =>
        SetValueWithRecoveryAsync(monitorId, percent, MonitorValue.Brightness, cancellationToken);

    public Task<MonitorOperationResult> SetContrastAsync(
        string monitorId,
        int percent,
        CancellationToken cancellationToken = default) =>
        SetValueWithRecoveryAsync(monitorId, percent, MonitorValue.Contrast, cancellationToken);

    public Task<MonitorOperationResult> SetRedAsync(
        string monitorId, int percent, CancellationToken cancellationToken = default) =>
        SetValueWithRecoveryAsync(monitorId, percent, MonitorValue.Red, cancellationToken);

    public Task<MonitorOperationResult> SetGreenAsync(
        string monitorId, int percent, CancellationToken cancellationToken = default) =>
        SetValueWithRecoveryAsync(monitorId, percent, MonitorValue.Green, cancellationToken);

    public Task<MonitorOperationResult> SetBlueAsync(
        string monitorId, int percent, CancellationToken cancellationToken = default) =>
        SetValueWithRecoveryAsync(monitorId, percent, MonitorValue.Blue, cancellationToken);

    private Task<MonitorOperationResult> SetValueWithRecoveryAsync(
        string monitorId,
        int percent,
        MonitorValue value,
        CancellationToken cancellationToken)
    {
        var normalizedPercent = Math.Clamp(percent, 0, 100);
        // DDC/CI calls can be noticeably slow on a waking display. Run the
        // complete operation away from the caller's UI context.
        return Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var device = FindDevice(monitorId);
                if (device is null)
                {
                    RefreshCore("monitor was not present before an operation");
                    device = FindDevice(monitorId);
                }

                if (device is null)
                {
                    return MonitorOperationResult.Fail("The selected monitor is no longer available.");
                }

                if (TrySet(device, value, normalizedPercent))
                {
                    return MonitorOperationResult.Ok($"{value}: {normalizedPercent}%");
                }

                var firstError = Marshal.GetLastWin32Error();
                AppLog.Write($"{value} failed for {monitorId}; Win32={firstError}. Re-enumerating.");

                // DDC/CI handles may become stale after display power-off or a
                // screen saver transition. Destroy every old handle, enumerate new
                // ones, match by stable ID, and retry exactly once.
                RefreshCore($"automatic recovery after {value} failure");
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                device = FindDevice(monitorId);
                if (device is not null && TrySet(device, value, normalizedPercent))
                {
                    return MonitorOperationResult.Ok(
                        $"Reconnected; {value}: {normalizedPercent}%");
                }

                var retryError = Marshal.GetLastWin32Error();
                if (value is MonitorValue.Red or MonitorValue.Green or MonitorValue.Blue)
                {
                    return MonitorOperationResult.Fail(
                        "The monitor did not accept the RGB gain value. " +
                        "Some models require a Custom/User picture or color-temperature mode.");
                }
                return MonitorOperationResult.Fail(
                    $"The monitor did not respond after reconnecting (Win32 {retryError}).");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AppLog.Write($"Unexpected {value} operation failure.", exception);
                return MonitorOperationResult.Fail(exception.Message);
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken);
    }

    private void RefreshCore(string reason)
    {
        var replacement = EnumerateDevices();
        List<MonitorDevice> previous;
        lock (_snapshotGate)
        {
            previous = _devices;
            _devices = replacement;
        }

        foreach (var device in previous)
        {
            device.Dispose();
        }

        AppLog.Write($"Monitor refresh: {reason}; found {replacement.Count} DDC/CI monitor(s).");
        RaiseChanged(reason);
    }

    private static List<MonitorDevice> EnumerateDevices()
    {
        var result = new List<MonitorDevice>();
        NativeMethods.MonitorEnumProc callback = (
            nint logicalHandle,
            nint monitorDc,
            ref NativeMethods.Rect monitorRect,
            nint userData) =>
        {
            EnumerateLogicalMonitor(logicalHandle, result);
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors failed.");
        }

        return result;
    }

    private static void EnumerateLogicalMonitor(nint logicalHandle, List<MonitorDevice> result)
    {
        var info = new NativeMethods.MonitorInfoEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
            DeviceName = string.Empty,
        };
        _ = NativeMethods.GetMonitorInfo(logicalHandle, ref info);

        if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(logicalHandle, out var count)
            || count == 0)
        {
            return;
        }

        var physicalMonitors = new NativeMethods.PhysicalMonitor[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(logicalHandle, count, physicalMonitors))
        {
            return;
        }

        for (var index = 0; index < physicalMonitors.Length; index++)
        {
            var physical = physicalMonitors[index];
            try
            {
                var identity = GetDisplayIdentity(
                    info.DeviceName ?? string.Empty,
                    index,
                    physical.Description ?? string.Empty);
                result.Add(MonitorDevice.Create(
                    info.DeviceName ?? string.Empty,
                    identity.Description,
                    identity.DeviceId,
                    index,
                    physical.Handle));
                physicalMonitors[index].Handle = 0;
            }
            finally
            {
                if (physicalMonitors[index].Handle != 0)
                {
                    _ = NativeMethods.DestroyPhysicalMonitor(physicalMonitors[index].Handle);
                }
            }
        }
    }

    private static (string Description, string DeviceId) GetDisplayIdentity(
        string logicalDevice,
        int physicalIndex,
        string physicalDescription)
    {
        var displayDevice = new NativeMethods.DisplayDevice
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.DisplayDevice>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceId = string.Empty,
            DeviceKey = string.Empty,
        };

        if (!NativeMethods.EnumDisplayDevices(
                logicalDevice,
                (uint)physicalIndex,
                ref displayDevice,
                0))
        {
            return (physicalDescription.Trim(), string.Empty);
        }

        var modelName = (displayDevice.DeviceString ?? string.Empty).Trim();
        var deviceId = (displayDevice.DeviceId ?? string.Empty).Trim();
        var edidName = TryGetEdidMonitorName(deviceId);
        var description = !string.IsNullOrWhiteSpace(edidName)
            ? edidName
            : string.IsNullOrWhiteSpace(modelName) ? physicalDescription.Trim() : modelName;
        return (description, deviceId);
    }

    private static string? TryGetEdidMonitorName(string deviceId)
    {
        try
        {
            var idParts = deviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (idParts.Length < 2 || !idParts[0].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var modelKey = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{idParts[1]}");
            if (modelKey is null)
            {
                return null;
            }

            foreach (var instanceName in modelKey.GetSubKeyNames())
            {
                using var parameters = modelKey.OpenSubKey($@"{instanceName}\Device Parameters");
                if (parameters?.GetValue("EDID") is byte[] edid)
                {
                    var name = ReadEdidDisplayName(edid);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
        {
            AppLog.Write("Could not read the monitor EDID name from the registry.", exception);
        }

        return null;
    }

    private static string? ReadEdidDisplayName(byte[] edid)
    {
        // EDID detailed descriptor blocks begin at byte 54. Descriptor tag FC
        // contains the display product name as 13 ASCII bytes.
        for (var offset = 54; offset + 18 <= edid.Length && offset < 126; offset += 18)
        {
            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xFC)
            {
                return Encoding.ASCII.GetString(edid, offset + 5, 13)
                    .Trim('\0', '\r', '\n', ' ');
            }
        }
        return null;
    }

    private MonitorDevice? FindDevice(string id)
    {
        lock (_snapshotGate)
        {
            return _devices.FirstOrDefault(device => device.Id == id);
        }
    }

    private static bool TrySet(MonitorDevice device, MonitorValue value, int percent)
    {
        return value switch
        {
            MonitorValue.Brightness when device.SupportsBrightness => device.SetBrightness(percent),
            MonitorValue.Contrast when device.SupportsContrast => device.SetContrast(percent),
            MonitorValue.Red when device.SupportsRgbGain => device.SetGain(NativeMethods.McGainType.Red, percent),
            MonitorValue.Green when device.SupportsRgbGain => device.SetGain(NativeMethods.McGainType.Green, percent),
            MonitorValue.Blue when device.SupportsRgbGain => device.SetGain(NativeMethods.McGainType.Blue, percent),
            _ => false,
        };
    }

    private void RaiseChanged(string reason) =>
        MonitorsChanged?.Invoke(this, new MonitorsChangedEventArgs(reason, GetSnapshots()));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Wait();
        try
        {
            lock (_snapshotGate)
            {
                foreach (var device in _devices)
                {
                    device.Dispose();
                }
                _devices = [];
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private enum MonitorValue
    {
        Brightness,
        Contrast,
        Red,
        Green,
        Blue,
    }

    private sealed class MonitorDevice : IDisposable
    {
        private bool _disposed;

        private MonitorDevice(
            string id,
            string logicalDevice,
            string description,
            string deviceId,
            int physicalIndex,
            nint handle)
        {
            Id = id;
            LogicalDevice = logicalDevice;
            Description = description;
            DeviceId = deviceId;
            PhysicalIndex = physicalIndex;
            Handle = handle;
        }

        internal string Id { get; }
        internal string LogicalDevice { get; }
        internal string Description { get; }
        internal string DeviceId { get; }
        internal int PhysicalIndex { get; }
        internal nint Handle { get; private set; }
        internal bool SupportsBrightness { get; private set; }
        internal uint MinimumBrightness { get; private set; }
        internal uint CurrentBrightness { get; private set; }
        internal uint MaximumBrightness { get; private set; }
        internal bool SupportsContrast { get; private set; }
        internal uint MinimumContrast { get; private set; }
        internal uint CurrentContrast { get; private set; }
        internal uint MaximumContrast { get; private set; }
        internal bool SupportsRgbGain { get; private set; }
        internal uint MinimumRed { get; private set; }
        internal uint CurrentRed { get; private set; }
        internal uint MaximumRed { get; private set; }
        internal uint MinimumGreen { get; private set; }
        internal uint CurrentGreen { get; private set; }
        internal uint MaximumGreen { get; private set; }
        internal uint MinimumBlue { get; private set; }
        internal uint CurrentBlue { get; private set; }
        internal uint MaximumBlue { get; private set; }

        internal static MonitorDevice Create(
            string logicalDevice,
            string description,
            string deviceId,
            int physicalIndex,
            nint handle)
        {
            var idSource = string.IsNullOrWhiteSpace(deviceId)
                ? $"{logicalDevice}\n{description}\n{physicalIndex}"
                : $"{deviceId}\n{physicalIndex}";
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idSource)))[..16];
            var device = new MonitorDevice(
                id, logicalDevice, description, deviceId, physicalIndex, handle);
            device.ReadCapabilitiesAndValues();
            return device;
        }

        private void ReadCapabilitiesAndValues()
        {
            var capabilityKnown = NativeMethods.GetMonitorCapabilities(
                Handle, out var capabilities, out _);

            SupportsBrightness = NativeMethods.GetMonitorBrightness(
                Handle,
                out var minBrightness,
                out var currentBrightness,
                out var maxBrightness);
            MinimumBrightness = minBrightness;
            CurrentBrightness = currentBrightness;
            MaximumBrightness = maxBrightness;

            SupportsContrast = NativeMethods.GetMonitorContrast(
                Handle,
                out var minContrast,
                out var currentContrast,
                out var maxContrast);
            MinimumContrast = minContrast;
            CurrentContrast = currentContrast;
            MaximumContrast = maxContrast;

            var redAvailable = NativeMethods.GetMonitorRedGreenOrBlueGain(
                Handle, NativeMethods.McGainType.Red,
                out var minRed, out var currentRed, out var maxRed);
            MinimumRed = minRed;
            CurrentRed = currentRed;
            MaximumRed = maxRed;

            var greenAvailable = NativeMethods.GetMonitorRedGreenOrBlueGain(
                Handle, NativeMethods.McGainType.Green,
                out var minGreen, out var currentGreen, out var maxGreen);
            MinimumGreen = minGreen;
            CurrentGreen = currentGreen;
            MaximumGreen = maxGreen;

            var blueAvailable = NativeMethods.GetMonitorRedGreenOrBlueGain(
                Handle, NativeMethods.McGainType.Blue,
                out var minBlue, out var currentBlue, out var maxBlue);
            MinimumBlue = minBlue;
            CurrentBlue = currentBlue;
            MaximumBlue = maxBlue;

            SupportsRgbGain = redAvailable && greenAvailable && blueAvailable;

            if (capabilityKnown)
            {
                SupportsBrightness &= (capabilities & NativeMethods.McCapsBrightness) != 0;
                SupportsContrast &= (capabilities & NativeMethods.McCapsContrast) != 0;
            }
        }

        internal bool SetBrightness(int percent)
        {
            var value = ScalePercent(percent, MinimumBrightness, MaximumBrightness);
            if (!NativeMethods.SetMonitorBrightness(Handle, value))
            {
                return false;
            }

            CurrentBrightness = value;
            return true;
        }

        internal bool SetContrast(int percent)
        {
            var value = ScalePercent(percent, MinimumContrast, MaximumContrast);
            if (!NativeMethods.SetMonitorContrast(Handle, value))
            {
                return false;
            }

            CurrentContrast = value;
            return true;
        }

        internal bool SetGain(NativeMethods.McGainType gainType, int percent)
        {
            var (minimum, maximum) = gainType switch
            {
                NativeMethods.McGainType.Red => (MinimumRed, MaximumRed),
                NativeMethods.McGainType.Green => (MinimumGreen, MaximumGreen),
                _ => (MinimumBlue, MaximumBlue),
            };
            var value = ScalePercent(percent, minimum, maximum);
            var written = NativeMethods.SetMonitorRedGreenOrBlueGain(Handle, gainType, value);
            if (written && ReadBackGain(gainType, value))
            {
                return true;
            }

            // A few monitor firmwares report RGB gain support but reject the
            // high-level MCCS helper. Retry through the equivalent VCP feature.
            var vcpCode = gainType switch
            {
                NativeMethods.McGainType.Red => (byte)0x16,
                NativeMethods.McGainType.Green => (byte)0x18,
                _ => (byte)0x1A,
            };
            return NativeMethods.SetVCPFeature(Handle, vcpCode, value)
                && ReadBackGain(gainType, value);
        }

        private bool ReadBackGain(NativeMethods.McGainType gainType, uint requestedValue)
        {
            Thread.Sleep(60);
            if (!NativeMethods.GetMonitorRedGreenOrBlueGain(
                    Handle, gainType, out var minimum, out var current, out var maximum))
            {
                return false;
            }

            switch (gainType)
            {
                case NativeMethods.McGainType.Red:
                    MinimumRed = minimum;
                    CurrentRed = current;
                    MaximumRed = maximum;
                    break;
                case NativeMethods.McGainType.Green:
                    MinimumGreen = minimum;
                    CurrentGreen = current;
                    MaximumGreen = maximum;
                    break;
                case NativeMethods.McGainType.Blue:
                    MinimumBlue = minimum;
                    CurrentBlue = current;
                    MaximumBlue = maximum;
                    break;
            }

            var range = maximum > minimum ? maximum - minimum : 0u;
            var tolerance = Math.Max(1u, range / 100u);
            return current >= requestedValue
                ? current - requestedValue <= tolerance
                : requestedValue - current <= tolerance;
        }

        internal MonitorSnapshot ToSnapshot() => new(
            Id,
            LogicalDevice,
            Description,
            DeviceId,
            PhysicalIndex,
            SupportsBrightness,
            ToPercent(CurrentBrightness, MinimumBrightness, MaximumBrightness),
            SupportsContrast,
            ToPercent(CurrentContrast, MinimumContrast, MaximumContrast),
            SupportsRgbGain,
            ToPercent(CurrentRed, MinimumRed, MaximumRed),
            ToPercent(CurrentGreen, MinimumGreen, MaximumGreen),
            ToPercent(CurrentBlue, MinimumBlue, MaximumBlue));

        private static uint ScalePercent(int percent, uint minimum, uint maximum)
        {
            if (maximum <= minimum)
            {
                return minimum;
            }

            return minimum + (uint)(((ulong)(maximum - minimum) * (uint)percent + 50UL) / 100UL);
        }

        private static int ToPercent(uint value, uint minimum, uint maximum)
        {
            if (maximum <= minimum)
            {
                return 0;
            }

            var clamped = Math.Clamp(value, minimum, maximum);
            return (int)(((ulong)(clamped - minimum) * 100UL + (maximum - minimum) / 2UL)
                / (maximum - minimum));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var handle = Handle;
            Handle = 0;
            if (handle != 0)
            {
                _ = NativeMethods.DestroyPhysicalMonitor(handle);
            }
        }
    }
}

public sealed class MonitorsChangedEventArgs(
    string reason,
    IReadOnlyList<MonitorSnapshot> monitors) : EventArgs
{
    public string Reason { get; } = reason;
    public IReadOnlyList<MonitorSnapshot> Monitors { get; } = monitors;
}
