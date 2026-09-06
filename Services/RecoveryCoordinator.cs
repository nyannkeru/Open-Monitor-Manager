using Microsoft.Win32;
using OpenMonitorManager.Interop;

namespace OpenMonitorManager.Services;

public sealed class RecoveryCoordinator : IDisposable
{
    private readonly MonitorService _monitorService;
    private readonly System.Threading.Timer _screenSaverPollTimer;
    private readonly object _scheduleGate = new();
    private CancellationTokenSource? _scheduledRecovery;
    private bool? _screenSaverWasRunning;
    private bool _disposed;

    public event EventHandler<string>? RecoveryTriggered;

    public RecoveryCoordinator(MonitorService monitorService)
    {
        _monitorService = monitorService;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _screenSaverPollTimer = new System.Threading.Timer(
            PollScreenSaver, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public void NotifyWindowMessage(int message, nint wParam)
    {
        if (message is NativeMethods.WmDisplayChange or NativeMethods.WmDeviceChange)
        {
            ScheduleRecovery("Windows display configuration changed", 500, 2_500);
        }
        else if (message == NativeMethods.WmPowerBroadcast
                 && ((int)wParam == NativeMethods.PbtApmResumeAutomatic
                     || (int)wParam == NativeMethods.PbtApmResumeSuspend))
        {
            ScheduleRecovery("Windows resumed from a power transition", 750, 3_500);
        }
    }

    public void RequestManualRecovery() =>
        ScheduleRecovery("manual reconnect requested", 0);

    private void PollScreenSaver(object? state)
    {
        if (_disposed
            || !NativeMethods.SystemParametersInfo(
                NativeMethods.SpiGetScreenSaverRunning, 0, out var running, 0))
        {
            return;
        }

        var previous = _screenSaverWasRunning;
        _screenSaverWasRunning = running;
        if (previous == true && !running)
        {
            // This transition directly addresses stale DDC/CI handles after a
            // screen saver has powered down or reconfigured a monitor.
            ScheduleRecovery("screen saver ended", 500, 2_500);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs eventArgs) =>
        ScheduleRecovery("display settings changed", 500, 2_500);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == PowerModes.Resume)
        {
            ScheduleRecovery("power resume event", 750, 3_500);
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs eventArgs)
    {
        if (eventArgs.Reason is SessionSwitchReason.SessionUnlock
            or SessionSwitchReason.ConsoleConnect
            or SessionSwitchReason.RemoteConnect)
        {
            ScheduleRecovery($"session event: {eventArgs.Reason}", 500, 2_500);
        }
    }

    private void ScheduleRecovery(string reason, params int[] delaysMilliseconds)
    {
        if (_disposed)
        {
            return;
        }

        CancellationToken token;
        lock (_scheduleGate)
        {
            _scheduledRecovery?.Cancel();
            _scheduledRecovery?.Dispose();
            _scheduledRecovery = new CancellationTokenSource();
            token = _scheduledRecovery.Token;
        }

        RecoveryTriggered?.Invoke(this, reason);
        _ = RunRecoverySequenceAsync(reason, delaysMilliseconds, token);
    }

    private async Task RunRecoverySequenceAsync(
        string reason,
        IEnumerable<int> delaysMilliseconds,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var delay in delaysMilliseconds)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                await _monitorService.RefreshAsync(reason, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer recovery cause superseded this sequence.
        }
        catch (Exception exception)
        {
            AppLog.Write($"Recovery sequence failed: {reason}", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _screenSaverPollTimer.Dispose();
        lock (_scheduleGate)
        {
            _scheduledRecovery?.Cancel();
            _scheduledRecovery?.Dispose();
            _scheduledRecovery = null;
        }
    }
}
