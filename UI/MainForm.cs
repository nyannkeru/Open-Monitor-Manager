using OpenMonitorManager.Interop;
using OpenMonitorManager.Models;
using OpenMonitorManager.Services;

namespace OpenMonitorManager.UI;

public sealed class MainForm : Form
{
    private readonly MonitorService _monitorService;
    private readonly ProfileStore _profileStore;
    private readonly RecoveryCoordinator _recovery;
    private readonly Icon _applicationIcon;
    private readonly ComboBox _monitorCombo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly PercentSlider _brightness = CreateSlider();
    private readonly PercentSlider _contrast = CreateSlider();
    private readonly PercentSlider _red = CreateSlider();
    private readonly PercentSlider _green = CreateSlider();
    private readonly PercentSlider _blue = CreateSlider();
    private readonly Label _brightnessValue = CreateValueLabel();
    private readonly Label _contrastValue = CreateValueLabel();
    private readonly Label _redValue = CreateValueLabel();
    private readonly Label _greenValue = CreateValueLabel();
    private readonly Label _blueValue = CreateValueLabel();
    private readonly ComboBox _profileCombo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NotifyIcon _trayIcon;
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _brightnessDebounce = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer _contrastDebounce = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer _redDebounce = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer _greenDebounce = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer _blueDebounce = new() { Interval = 180 };
    private readonly CancellationTokenSource _lifetime = new();
    private List<MonitorProfile> _profiles = [];
    private bool _suppressSliderEvents;
    private bool _allowExit;
    private int _brightnessRevision;
    private int _contrastRevision;
    private int _redRevision;
    private int _greenRevision;
    private int _blueRevision;

    public MainForm(
        MonitorService monitorService,
        ProfileStore profileStore,
        RecoveryCoordinator recovery)
    {
        _monitorService = monitorService;
        _profileStore = profileStore;
        _recovery = recovery;

        Text = "Open Monitor Manager";
        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? (Icon)SystemIcons.Application.Clone();
        Icon = _applicationIcon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 510);
        ClientSize = new Size(760, 550);

        var refreshButton = new Button { Text = "Refresh / Reconnect", AutoSize = true };
        var saveProfileButton = CreateProfileButton(ProfileButtonIcon.Save, "Save profile");
        var applyProfileButton = CreateProfileButton(ProfileButtonIcon.Apply, "Apply profile");
        var deleteProfileButton = CreateProfileButton(ProfileButtonIcon.Delete, "Delete profile");

        refreshButton.Click += (_, _) => _recovery.RequestManualRecovery();
        saveProfileButton.Click += SaveProfileButton_Click;
        applyProfileButton.Click += ApplyProfileButton_Click;
        deleteProfileButton.Click += DeleteProfileButton_Click;
        _monitorCombo.SelectedIndexChanged += (_, _) => BindSelectedMonitor();
        _brightness.ValueChangedByUser += (_, _) => QueueBrightnessChange();
        _contrast.ValueChangedByUser += (_, _) => QueueContrastChange();
        _red.ValueChangedByUser += (_, _) =>
        {
            _redRevision++;
            QueueGainChange(_red, _redValue, _redDebounce);
        };
        _green.ValueChangedByUser += (_, _) =>
        {
            _greenRevision++;
            QueueGainChange(_green, _greenValue, _greenDebounce);
        };
        _blue.ValueChangedByUser += (_, _) =>
        {
            _blueRevision++;
            QueueGainChange(_blue, _blueValue, _blueDebounce);
        };
        _brightnessDebounce.Tick += BrightnessDebounce_Tick;
        _contrastDebounce.Tick += ContrastDebounce_Tick;
        _redDebounce.Tick += RedDebounce_Tick;
        _greenDebounce.Tick += GreenDebounce_Tick;
        _blueDebounce.Tick += BlueDebounce_Tick;

        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttonRow.Controls.Add(refreshButton);

        var profileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        profileButtons.FlowDirection = FlowDirection.LeftToRight;
        profileButtons.WrapContents = false;
        profileButtons.Margin = Padding.Empty;
        profileButtons.Controls.Add(saveProfileButton);
        profileButtons.Controls.Add(applyProfileButton);
        profileButtons.Controls.Add(deleteProfileButton);

        _toolTip.SetToolTip(saveProfileButton, "Save profile");
        _toolTip.SetToolTip(applyProfileButton, "Apply profile");
        _toolTip.SetToolTip(deleteProfileButton, "Delete profile");

        var displaySettings = CreateSettingsGroup("Display");
        AddRow(displaySettings, 0, "Brightness", _brightness, _brightnessValue);
        AddRow(displaySettings, 1, "Contrast", _contrast, _contrastValue);

        var colorSettings = CreateSettingsGroup("Color");
        AddRow(colorSettings, 0, "Red", _red, _redValue);
        AddRow(colorSettings, 1, "Green", _green, _greenValue);
        AddRow(colorSettings, 2, "Blue", _blue, _blueValue);
        var displayGroup = WrapInGroupBox("Display controls", displaySettings);
        var colorGroup = WrapInGroupBox("Color controls", colorSettings);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 3,
            RowCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(layout, 0, "Monitor", _monitorCombo, buttonRow);
        layout.Controls.Add(displayGroup, 0, 1);
        layout.SetColumnSpan(displayGroup, 3);
        layout.Controls.Add(colorGroup, 0, 2);
        layout.SetColumnSpan(colorGroup, 3);
        AddRow(layout, 3, "Profile", _profileCombo, profileButtons);

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            Text = "Monitor handles are automatically rebuilt after screen saver, power, session, " +
                   "and display-change events. A failed DDC/CI operation also reconnects and retries once.",
        };
        layout.Controls.Add(explanation, 0, 4);
        layout.SetColumnSpan(explanation, 3);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        Controls.Add(layout);
        Controls.Add(statusStrip);

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show", null, (_, _) => ShowFromTray());
        trayMenu.Items.Add("Refresh / Reconnect", null, (_, _) => _recovery.RequestManualRecovery());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _trayIcon = new NotifyIcon
        {
            Text = "Open Monitor Manager",
            Icon = (Icon)_applicationIcon.Clone(),
            ContextMenuStrip = trayMenu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        _monitorService.MonitorsChanged += MonitorService_MonitorsChanged;
        _recovery.RecoveryTriggered += Recovery_RecoveryTriggered;
        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
    }

    protected override void WndProc(ref Message message)
    {
        _recovery.NotifyWindowMessage(message.Msg, message.WParam);
        base.WndProc(ref message);
    }

    private async void MainForm_Shown(object? sender, EventArgs eventArgs)
    {
        _profiles = [.. await _profileStore.LoadAsync(_lifetime.Token)];
        BindProfiles();
        await RefreshMonitorsAsync("application started");
    }

    private async Task RefreshMonitorsAsync(string reason)
    {
        SetStatus($"Refreshing: {reason}…");
        try
        {
            await _monitorService.RefreshAsync(reason, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Write("Monitor refresh failed.", exception);
            SetStatus($"Refresh failed: {exception.Message}");
        }
    }

    private void MonitorService_MonitorsChanged(object? sender, MonitorsChangedEventArgs eventArgs)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            var selectedId = (_monitorCombo.SelectedItem as MonitorSnapshot)?.Id;
            _monitorCombo.BeginUpdate();
            try
            {
                _monitorCombo.Items.Clear();
                _monitorCombo.Items.AddRange(eventArgs.Monitors.Cast<object>().ToArray());
                if (eventArgs.Monitors.Count > 0)
                {
                    var selectedIndex = eventArgs.Monitors
                        .Select((monitor, index) => (monitor, index))
                        .FirstOrDefault(item => item.monitor.Id == selectedId).index;
                    _monitorCombo.SelectedIndex = Math.Clamp(selectedIndex, 0, eventArgs.Monitors.Count - 1);
                }
            }
            finally
            {
                _monitorCombo.EndUpdate();
            }

            SetStatus($"{eventArgs.Reason}: {eventArgs.Monitors.Count} monitor(s)");
        });
    }

    private void Recovery_RecoveryTriggered(object? sender, string reason)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(() => SetStatus($"Recovery scheduled: {reason}"));
        }
    }

    private void BindSelectedMonitor()
    {
        _suppressSliderEvents = true;
        try
        {
            if (_monitorCombo.SelectedItem is not MonitorSnapshot monitor)
            {
                _brightness.Enabled = false;
                _contrast.Enabled = false;
                _red.Enabled = false;
                _green.Enabled = false;
                _blue.Enabled = false;
                _brightnessValue.Text = "—";
                _contrastValue.Text = "—";
                _redValue.Text = "—";
                _greenValue.Text = "—";
                _blueValue.Text = "—";
                return;
            }

            _brightness.Enabled = monitor.SupportsBrightness;
            _brightness.Value = Math.Clamp(monitor.Brightness, 0, 100);
            _brightnessValue.Text = monitor.SupportsBrightness ? $"{_brightness.Value}%" : "Unsupported";
            _contrast.Enabled = monitor.SupportsContrast;
            _contrast.Value = Math.Clamp(monitor.Contrast, 0, 100);
            _contrastValue.Text = monitor.SupportsContrast ? $"{_contrast.Value}%" : "Unsupported";
            BindGain(_red, _redValue, monitor.Red, monitor.SupportsRgbGain);
            BindGain(_green, _greenValue, monitor.Green, monitor.SupportsRgbGain);
            BindGain(_blue, _blueValue, monitor.Blue, monitor.SupportsRgbGain);
        }
        finally
        {
            _suppressSliderEvents = false;
        }
    }

    private static void BindGain(PercentSlider slider, Label valueLabel, int value, bool supported)
    {
        slider.Enabled = supported;
        slider.Value = Math.Clamp(value, 0, 100);
        valueLabel.Text = supported ? $"{slider.Value}%" : "Unsupported";
    }

    private void QueueGainChange(
        PercentSlider slider,
        Label valueLabel,
        System.Windows.Forms.Timer timer)
    {
        valueLabel.Text = $"{slider.Value}%";
        if (_suppressSliderEvents)
        {
            return;
        }

        timer.Stop();
        timer.Start();
    }

    private void QueueBrightnessChange()
    {
        _brightnessValue.Text = $"{_brightness.Value}%";
        if (_suppressSliderEvents)
        {
            return;
        }

        _brightnessRevision++;
        _brightnessDebounce.Stop();
        _brightnessDebounce.Start();
    }

    private void QueueContrastChange()
    {
        _contrastValue.Text = $"{_contrast.Value}%";
        if (_suppressSliderEvents)
        {
            return;
        }

        _contrastRevision++;
        _contrastDebounce.Stop();
        _contrastDebounce.Start();
    }

    private async void BrightnessDebounce_Tick(object? sender, EventArgs eventArgs)
    {
        _brightnessDebounce.Stop();
        if (_monitorCombo.SelectedItem is not MonitorSnapshot monitor)
        {
            return;
        }

        var requested = _brightness.Value;
        var revision = _brightnessRevision;
        var result = await _monitorService.SetBrightnessAsync(
            monitor.Id, requested, _lifetime.Token);
        RestoreRequestedValue(
            _brightness, _brightnessValue, monitor.Id, requested,
            result.Success && revision == _brightnessRevision);
        SetStatus(result.Message);
    }

    private async void ContrastDebounce_Tick(object? sender, EventArgs eventArgs)
    {
        _contrastDebounce.Stop();
        if (_monitorCombo.SelectedItem is not MonitorSnapshot monitor)
        {
            return;
        }

        var requested = _contrast.Value;
        var revision = _contrastRevision;
        var result = await _monitorService.SetContrastAsync(
            monitor.Id, requested, _lifetime.Token);
        RestoreRequestedValue(
            _contrast, _contrastValue, monitor.Id, requested,
            result.Success && revision == _contrastRevision);
        SetStatus(result.Message);
    }

    private async void RedDebounce_Tick(object? sender, EventArgs eventArgs)
    {
        _redDebounce.Stop();
        if (_monitorCombo.SelectedItem is MonitorSnapshot monitor)
        {
            var requested = _red.Value;
            var revision = _redRevision;
            var result = await _monitorService.SetRedAsync(
                monitor.Id, requested, _lifetime.Token);
            RestoreRequestedValue(
                _red, _redValue, monitor.Id, requested,
                result.Success && revision == _redRevision);
            SetStatus(result.Message);
        }
    }

    private async void GreenDebounce_Tick(object? sender, EventArgs eventArgs)
    {
        _greenDebounce.Stop();
        if (_monitorCombo.SelectedItem is MonitorSnapshot monitor)
        {
            var requested = _green.Value;
            var revision = _greenRevision;
            var result = await _monitorService.SetGreenAsync(
                monitor.Id, requested, _lifetime.Token);
            RestoreRequestedValue(
                _green, _greenValue, monitor.Id, requested,
                result.Success && revision == _greenRevision);
            SetStatus(result.Message);
        }
    }

    private async void BlueDebounce_Tick(object? sender, EventArgs eventArgs)
    {
        _blueDebounce.Stop();
        if (_monitorCombo.SelectedItem is MonitorSnapshot monitor)
        {
            var requested = _blue.Value;
            var revision = _blueRevision;
            var result = await _monitorService.SetBlueAsync(
                monitor.Id, requested, _lifetime.Token);
            RestoreRequestedValue(
                _blue, _blueValue, monitor.Id, requested,
                result.Success && revision == _blueRevision);
            SetStatus(result.Message);
        }
    }

    private void RestoreRequestedValue(
        PercentSlider slider,
        Label valueLabel,
        string monitorId,
        int requested,
        bool success)
    {
        if (!success || (_monitorCombo.SelectedItem as MonitorSnapshot)?.Id != monitorId)
        {
            return;
        }

        _suppressSliderEvents = true;
        try
        {
            slider.Value = requested;
            valueLabel.Text = $"{requested}%";
        }
        finally
        {
            _suppressSliderEvents = false;
        }
    }

    private async void SaveProfileButton_Click(object? sender, EventArgs eventArgs)
    {
        var name = PromptDialog.Show(this, "Save profile", "Profile name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var profile = new MonitorProfile
        {
            Name = name,
            SavedAt = DateTimeOffset.Now,
            Monitors = _monitorService.GetSnapshots().Select(static monitor => new MonitorProfileEntry
            {
                MonitorId = monitor.Id,
                Description = monitor.DisplayName,
                Brightness = monitor.SupportsBrightness ? monitor.Brightness : null,
                Contrast = monitor.SupportsContrast ? monitor.Contrast : null,
                Red = monitor.SupportsRgbGain ? monitor.Red : null,
                Green = monitor.SupportsRgbGain ? monitor.Green : null,
                Blue = monitor.SupportsRgbGain ? monitor.Blue : null,
            }).ToList(),
        };

        var existing = _profiles.FindIndex(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            _profiles[existing] = profile;
        }
        else
        {
            _profiles.Add(profile);
        }

        await _profileStore.SaveAsync(_profiles, _lifetime.Token);
        BindProfiles(name);
        SetStatus($"Profile saved: {name}");
    }

    private async void ApplyProfileButton_Click(object? sender, EventArgs eventArgs)
    {
        if (_profileCombo.SelectedItem is not MonitorProfile profile)
        {
            return;
        }

        SetStatus($"Applying profile: {profile.Name}…");
        var failures = new List<string>();
        foreach (var entry in profile.Monitors)
        {
            if (entry.Brightness is int brightness)
            {
                var result = await _monitorService.SetBrightnessAsync(
                    entry.MonitorId, brightness, _lifetime.Token);
                if (!result.Success)
                {
                    failures.Add($"{entry.Description} brightness: {result.Message}");
                }
            }

            if (entry.Contrast is int contrast)
            {
                var result = await _monitorService.SetContrastAsync(
                    entry.MonitorId, contrast, _lifetime.Token);
                if (!result.Success)
                {
                    failures.Add($"{entry.Description} contrast: {result.Message}");
                }
            }

            if (entry.Red is int red)
            {
                AddFailureIfNeeded(failures, entry, "red gain",
                    await _monitorService.SetRedAsync(entry.MonitorId, red, _lifetime.Token));
            }
            if (entry.Green is int green)
            {
                AddFailureIfNeeded(failures, entry, "green gain",
                    await _monitorService.SetGreenAsync(entry.MonitorId, green, _lifetime.Token));
            }
            if (entry.Blue is int blue)
            {
                AddFailureIfNeeded(failures, entry, "blue gain",
                    await _monitorService.SetBlueAsync(entry.MonitorId, blue, _lifetime.Token));
            }
        }

        SetStatus(failures.Count == 0
            ? $"Profile applied: {profile.Name}"
            : $"Profile completed with {failures.Count} failure(s). See log.");
        foreach (var failure in failures)
        {
            AppLog.Write(failure);
        }
        _monitorService.PublishSnapshots($"Profile applied: {profile.Name}");
    }

    private static void AddFailureIfNeeded(
        List<string> failures,
        MonitorProfileEntry entry,
        string setting,
        MonitorOperationResult result)
    {
        if (!result.Success)
        {
            failures.Add($"{entry.Description} {setting}: {result.Message}");
        }
    }

    private async void DeleteProfileButton_Click(object? sender, EventArgs eventArgs)
    {
        if (_profileCombo.SelectedItem is not MonitorProfile profile
            || MessageBox.Show(
                this,
                $"Delete profile '{profile.Name}'?",
                "Open Monitor Manager",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _profiles.Remove(profile);
        await _profileStore.SaveAsync(_profiles, _lifetime.Token);
        BindProfiles();
        SetStatus($"Profile deleted: {profile.Name}");
    }

    private void BindProfiles(string? selectName = null)
    {
        _profileCombo.BeginUpdate();
        try
        {
            _profileCombo.Items.Clear();
            _profileCombo.Items.AddRange(_profiles.OrderBy(profile => profile.Name).Cast<object>().ToArray());
            if (_profileCombo.Items.Count > 0)
            {
                var index = 0;
                if (selectName is not null)
                {
                    for (var i = 0; i < _profileCombo.Items.Count; i++)
                    {
                        if (_profileCombo.Items[i] is MonitorProfile profile
                            && string.Equals(profile.Name, selectName, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }
                _profileCombo.SelectedIndex = index;
            }
        }
        finally
        {
            _profileCombo.EndUpdate();
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        Close();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_allowExit && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
            _trayIcon.ShowBalloonTip(
                1_500,
                "Open Monitor Manager",
                "Still running in the notification area.",
                ToolTipIcon.Info);
            return;
        }

        _lifetime.Cancel();
    }

    private void SetStatus(string text) => _status.Text = text;

    private static PercentSlider CreateSlider() => new()
    {
        Dock = DockStyle.Fill,
        Enabled = false,
    };

    private static Label CreateValueLabel() => new()
    {
        AutoSize = true,
        Text = "—",
        Anchor = AnchorStyles.Left,
    };

    private static TableLayoutPanel CreateSettingsGroup(string accessibleName)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            AccessibleName = accessibleName,
            Padding = new Padding(6, 3, 6, 5),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return panel;
    }

    private static GroupBox WrapInGroupBox(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(8),
            Margin = new Padding(3, 7, 3, 3),
        };
        group.Controls.Add(content);
        return group;
    }

    private static Button CreateProfileButton(ProfileButtonIcon icon, string accessibleName)
    {
        var image = new Bitmap(18, 18);
        using (var graphics = Graphics.FromImage(image))
        using (var pen = new Pen(Color.FromArgb(55, 62, 70), 1.7f))
        using (var brush = new SolidBrush(Color.FromArgb(55, 62, 70)))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            switch (icon)
            {
                case ProfileButtonIcon.Save:
                    graphics.DrawRectangle(pen, 2, 2, 14, 14);
                    graphics.DrawRectangle(pen, 5, 2, 7, 5);
                    graphics.DrawRectangle(pen, 5, 11, 8, 5);
                    break;
                case ProfileButtonIcon.Apply:
                    graphics.FillPolygon(brush, [new Point(5, 3), new Point(15, 9), new Point(5, 15)]);
                    break;
                case ProfileButtonIcon.Delete:
                    graphics.DrawRectangle(pen, 5, 6, 8, 10);
                    graphics.DrawLine(pen, 3, 5, 15, 5);
                    graphics.DrawLine(pen, 7, 2, 11, 2);
                    graphics.DrawLine(pen, 8, 8, 8, 14);
                    graphics.DrawLine(pen, 11, 8, 11, 14);
                    break;
            }
        }

        var button = new Button
        {
            Image = image,
            Size = new Size(34, 30),
            Margin = new Padding(2, 0, 2, 0),
            AccessibleName = accessibleName,
            TabStop = true,
        };
        button.Disposed += (_, _) => image.Dispose();
        return button;
    }

    private static void AddRow(
        TableLayoutPanel layout,
        int row,
        string caption,
        Control mainControl,
        Control trailingControl)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainControl.Margin = new Padding(3, 2, 3, 2);
        trailingControl.Anchor = AnchorStyles.Left;
        layout.Controls.Add(new Label
        {
            Text = caption,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 10, 12, 3),
        }, 0, row);
        layout.Controls.Add(mainControl, 1, row);
        layout.Controls.Add(trailingControl, 2, row);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitorService.MonitorsChanged -= MonitorService_MonitorsChanged;
            _recovery.RecoveryTriggered -= Recovery_RecoveryTriggered;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _brightnessDebounce.Dispose();
            _contrastDebounce.Dispose();
            _redDebounce.Dispose();
            _greenDebounce.Dispose();
            _blueDebounce.Dispose();
            _toolTip.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
            Icon = null;
            _applicationIcon.Dispose();
            _monitorService.Dispose();
        }
        base.Dispose(disposing);
    }

    private enum ProfileButtonIcon
    {
        Save,
        Apply,
        Delete,
    }
}
