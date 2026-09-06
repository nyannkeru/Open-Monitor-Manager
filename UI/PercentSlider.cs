using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace OpenMonitorManager.UI;

internal sealed class PercentSlider : Control
{
    private const int ThumbRadius = 7;
    private int _value;

    internal PercentSlider()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Height = 28;
        MinimumSize = new Size(120, 28);
        AccessibleRole = AccessibleRole.Slider;
    }

    internal event EventHandler? ValueChangedByUser;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Value
    {
        get => _value;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (_value == normalized)
            {
                return;
            }

            _value = normalized;
            AccessibleName = $"{_value}%";
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var left = ThumbRadius + 1;
        var right = Math.Max(left + 1, ClientSize.Width - ThumbRadius - 1);
        var centerY = ClientSize.Height / 2;
        var track = new Rectangle(left, centerY - 5, right - left, 10);
        var trackColor = Enabled ? Color.FromArgb(218, 222, 228) : Color.FromArgb(232, 232, 232);
        var fillColor = Enabled ? Color.FromArgb(38, 119, 208) : Color.FromArgb(174, 184, 194);

        using var trackPath = RoundedRectangle(track, 5);
        using var trackBrush = new SolidBrush(trackColor);
        graphics.FillPath(trackBrush, trackPath);

        var thumbX = left + (int)Math.Round(track.Width * (_value / 100d));
        if (thumbX > left)
        {
            var fill = new Rectangle(left, track.Top, thumbX - left, track.Height);
            graphics.SetClip(trackPath);
            using var fillBrush = new SolidBrush(fillColor);
            graphics.FillRectangle(fillBrush, fill);
            graphics.ResetClip();
        }

        using var dividerPen = new Pen(Color.FromArgb(95, Color.White), 1);
        for (var step = 1; step < 10; step++)
        {
            var x = left + (int)Math.Round(track.Width * (step / 10d));
            graphics.DrawLine(dividerPen, x, track.Top + 2, x, track.Bottom - 2);
        }

        var thumb = new Rectangle(
            thumbX - ThumbRadius,
            centerY - ThumbRadius,
            ThumbRadius * 2,
            ThumbRadius * 2);
        using var thumbBrush = new SolidBrush(Enabled ? Color.White : Color.FromArgb(242, 242, 242));
        using var thumbPen = new Pen(Enabled ? fillColor : Color.FromArgb(174, 184, 194), 2);
        graphics.FillEllipse(thumbBrush, thumb);
        graphics.DrawEllipse(thumbPen, thumb);

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(graphics, ClientRectangle);
        }
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        if (Enabled && eventArgs.Button == MouseButtons.Left)
        {
            Focus();
            Capture = true;
            SetFromMouse(eventArgs.X);
        }
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        if (Enabled && Capture && (eventArgs.Button & MouseButtons.Left) != 0)
        {
            SetFromMouse(eventArgs.X);
        }
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        base.OnMouseUp(eventArgs);
        if (eventArgs.Button == MouseButtons.Left)
        {
            Capture = false;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs eventArgs)
    {
        if (Enabled && eventArgs.Delta != 0)
        {
            SetFromUser(Value + Math.Sign(eventArgs.Delta));
        }
        // Do not call the base implementation. Its step depends on the system
        // wheel setting and would apply an additional value change.
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        switch (eventArgs.KeyCode)
        {
            case Keys.Left:
            case Keys.Down:
                SetFromUser(Value - 1);
                eventArgs.Handled = true;
                break;
            case Keys.Right:
            case Keys.Up:
                SetFromUser(Value + 1);
                eventArgs.Handled = true;
                break;
            case Keys.Home:
                SetFromUser(0);
                eventArgs.Handled = true;
                break;
            case Keys.End:
                SetFromUser(100);
                eventArgs.Handled = true;
                break;
            default:
                base.OnKeyDown(eventArgs);
                break;
        }
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Invalidate();
    }

    private void SetFromMouse(int x)
    {
        var left = ThumbRadius + 1;
        var width = Math.Max(1, ClientSize.Width - (ThumbRadius + 1) * 2);
        var percent = (int)Math.Round((x - left) * 100d / width);
        SetFromUser(percent);
    }

    private void SetFromUser(int value)
    {
        var previous = Value;
        Value = value;
        if (Value != previous)
        {
            ValueChangedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
