# Open Monitor Manager

A C# monitor controller for Windows.

## Implemented

- Enumerates logical and physical monitors through `user32.dll` and `dxva2.dll`.
- Shows the EDID product name, logical display name, and Windows PnP device ID.
- Reads and sets DDC/CI brightness, contrast, and independent RGB gain.
- Disables controls that a monitor does not advertise or respond to.
- Saves named profiles as JSON under the current user's
  local application-data directory.
- Runs in the notification area and offers a manual reconnect command.
- Prevents accidental duplicate instances with a per-user mutex.
- Debounces slider operations so dragging does not flood a monitor with DDC
  commands.
- Executes potentially slow DDC/CI driver calls away from the UI thread.
- Runs as the current user; elevation is not requested.

## Automatic recovery

Physical DDC/CI handles are disposable system resources. Some monitor/GPU
combinations invalidate them while a screen saver, display power transition,
dock change, or session transition occurs.

Open Monitor Manager responds by destroying every old physical-monitor handle
and performing a fresh enumeration after:

- a screen saver changes from running to stopped;
- system power resumes;
- the session is unlocked or reconnected;
- Windows reports a display/device configuration change;
- a brightness, contrast, or RGB-gain operation fails.

Recovery events are debounced and followed by a second delayed enumeration,
because some displays do not expose DDC/CI immediately after waking. A failed
user operation is retried once against the newly enumerated handle.

To verify recovery, let the screen saver run long enough
for the displays to power down, wake the session, and move a slider. The status
bar should first report a scheduled recovery and then a refreshed monitor
count. If the first write still encounters a stale handle, it should report
`Reconnected` after the automatic retry. Details are recorded in
`OpenMonitorManager.log`.

## Build

Requirements: Windows and the .NET 10 SDK.

```powershell
dotnet build ".\OpenMonitorManager.csproj" -c Release
```

Run:

```powershell
dotnet run --project ".\OpenMonitorManager.csproj"
```

Profiles and logs are stored in:

```text
%LOCALAPPDATA%\OpenMonitorManager\
```

## Current scope

The application controls monitors that expose brightness, contrast, or RGB gain
through Windows DDC/CI. Laptop panels that only expose WMI brightness are not
yet included. RGB gain here means the monitor's hardware gain controls; it does
not modify the Windows gamma ramp, which can conflict with color-management
software.
