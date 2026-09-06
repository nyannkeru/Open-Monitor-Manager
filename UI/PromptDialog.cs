namespace OpenMonitorManager.UI;

internal sealed class PromptDialog : Form
{
    private readonly TextBox _textBox = new() { Dock = DockStyle.Fill };

    private PromptDialog(string title, string prompt, string initialValue)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(390, 125);

        _textBox.Text = initialValue;
        _textBox.SelectAll();

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = prompt, AutoSize = true }, 0, 0);
        layout.Controls.Add(_textBox, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
    }

    internal static string? Show(IWin32Window owner, string title, string prompt)
    {
        using var dialog = new PromptDialog(title, prompt, string.Empty);
        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog._textBox.Text.Trim()
            : null;
    }
}
