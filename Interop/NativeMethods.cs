using System.Runtime.InteropServices;

namespace OpenMonitorManager.Interop;

internal static class NativeMethods
{
    internal const uint McCapsBrightness = 0x00000002;
    internal const uint McCapsContrast = 0x00000004;
    internal const uint SpiGetScreenSaverRunning = 0x0072;

    internal const int WmDisplayChange = 0x007E;
    internal const int WmPowerBroadcast = 0x0218;
    internal const int WmDeviceChange = 0x0219;
    internal const int PbtApmResumeAutomatic = 0x0012;
    internal const int PbtApmResumeSuspend = 0x0007;

    internal delegate bool MonitorEnumProc(
        nint monitor,
        nint monitorDc,
        ref Rect monitorRect,
        nint userData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        internal uint Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PhysicalMonitor
    {
        internal nint Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        internal uint Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceString;

        internal uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceKey;
    }

    internal enum McGainType
    {
        Red = 0,
        Green = 1,
        Blue = 2
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        nint hdc,
        nint clipRect,
        MonitorEnumProc callback,
        nint userData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(
        string? deviceName,
        uint deviceNumber,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value,
        uint updateFlags);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
        nint monitor,
        out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPhysicalMonitorsFromHMONITOR(
        nint monitor,
        uint count,
        [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyPhysicalMonitor(nint monitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorCapabilities(
        nint monitor,
        out uint capabilities,
        out uint colorTemperatures);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorBrightness(
        nint monitor,
        out uint minimum,
        out uint current,
        out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMonitorBrightness(nint monitor, uint value);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorContrast(
        nint monitor,
        out uint minimum,
        out uint current,
        out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMonitorContrast(nint monitor, uint value);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorRedGreenOrBlueGain(
        nint monitor,
        McGainType gainType,
        out uint minimum,
        out uint current,
        out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMonitorRedGreenOrBlueGain(
        nint monitor,
        McGainType gainType,
        uint value);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetVCPFeature(
        nint monitor,
        byte vcpCode,
        uint newValue);
}
