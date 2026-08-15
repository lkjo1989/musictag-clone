namespace MusicTagClone.Forms;

internal sealed class AutoMatchProgressForm : Form
{
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _cancel = new();
    private readonly CancellationTokenSource _cts;

    public AutoMatchProgressForm(int total, CancellationTokenSource cts)
    {
        _cts = cts;
        Text = "自动匹配标签";
        Width = 470;
        Height = 165;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ForeColor = Color.Black;

        _status.Text = "正在准备...";
        _status.SetBounds(16, 14, 420, 25);
        _status.AutoEllipsis = true;
        _status.ForeColor = Color.Black;
        _progress.SetBounds(16, 45, 420, 22);
        _progress.Minimum = 0;
        _progress.Maximum = Math.Max(1, total);
        _cancel.Text = "取消";
        _cancel.SetBounds(348, 78, 88, 28);
        _cancel.ForeColor = Color.Black;
        _cancel.Click += (_, _) =>
        {
            _cancel.Enabled = false;
            _status.Text = "正在取消...";
            _cts.Cancel();
        };
        Controls.AddRange(new Control[] { _status, _progress, _cancel });
    }

    public void SetFile(string filename)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action<string>(SetFile), filename); return; }
        _status.Text = "正在匹配: " + filename;
    }

    public void SetProgress(int value)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action<int>(SetProgress), value); return; }
        _progress.Value = Math.Max(_progress.Minimum, Math.Min(_progress.Maximum, value));
    }
}
