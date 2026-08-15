using MusicTagClone.Interfaces;
using MusicTagClone.Models;

namespace MusicTagClone.Forms;

internal sealed class AutoMatchTagsForm : Form
{
    private readonly ISettingsService _settings;
    private readonly DataGridView _grid = new();
    private readonly NumericUpDown _threads = new();
    private readonly CheckBox _instrumental = new();
    public AutoMatchOptions Options { get; }

    public AutoMatchTagsForm(ISettingsService settings)
    {
        _settings = settings;
        Options = AutoMatchOptions.Load(settings);
        Text = "自动匹配标签";
        Width = 650;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ForeColor = Color.Black;
        BuildLayout();
    }

    private void BuildLayout()
    {
        var intro = new Label { Text = "选择要匹配的字段、写入位置和覆盖方式", Dock = DockStyle.Top, Height = 28, ForeColor = Color.Black };
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.ForeColor = Color.Black;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = "匹配", Width = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "field", HeaderText = "字段", ReadOnly = true, Width = 145 });
        var mode = new DataGridViewComboBoxColumn { Name = "mode", HeaderText = "写入位置", Width = 180, FlatStyle = FlatStyle.Flat };
        _grid.Columns.Add(mode);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "overwrite", HeaderText = "覆盖已有值", Width = 100 });
        foreach (var name in AutoMatchOptions.Names)
        {
            var option = Options.Get(name);
            var row = _grid.Rows.Add(option.Enabled, DisplayName(name), string.Empty, option.Overwrite);
            _grid.Rows[row].Tag = name;
            var combo = _grid.Rows[row].Cells["mode"] as DataGridViewComboBoxCell;
            combo!.Items.Add("写入标签");
            if (name == AutoMatchOptions.Cover || name == AutoMatchOptions.Lyrics)
            {
                combo.Items.Add("保存为文件");
                if (name == AutoMatchOptions.Cover) combo.Items.Add("同时写入标签和文件");
            }
            combo.Value = ModeText(option.WriteMode);
        }
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == _grid.Columns["enabled"]!.Index)
                _grid.InvalidateRow(e.RowIndex);
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        var settingsPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(8) };
        settingsPanel.Controls.Add(new Label { Text = "搜索线程数(自动匹配时):", AutoSize = true, Margin = new Padding(3, 7, 3, 0), ForeColor = Color.Black });
        _threads.Minimum = 1; _threads.Maximum = AutoMatchOptions.MaxThreadCount; _threads.Value = Options.ThreadCount; _threads.Width = 55;
        settingsPanel.Controls.Add(_threads);
        _instrumental.Text = "标题含伴奏/纯音乐时不下载歌词";
        _instrumental.Checked = Options.DontDownloadLyricWithInstrumentInTitle;
        _instrumental.AutoSize = true; _instrumental.ForeColor = Color.Black;
        settingsPanel.Controls.Add(_instrumental);

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 48 };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.None, Width = 88, Height = 28, Left = 430, Top = 8, ForeColor = Color.Black };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 88, Height = 28, Left = 530, Top = 8, ForeColor = Color.Black };
        ok.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(_grid); Controls.Add(intro); Controls.Add(settingsPanel); Controls.Add(buttons);
    }

    private void SaveAndClose()
    {
        var selected = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var name = row.Tag as string;
            if (name == null) continue;
            var enabled = Convert.ToBoolean(row.Cells["enabled"].Value ?? false);
            var option = Options.Get(name);
            option.Enabled = enabled;
            option.Overwrite = Convert.ToBoolean(row.Cells["overwrite"].Value ?? false);
            option.WriteMode = ParseMode(row.Cells["mode"].Value?.ToString());
            if (enabled) selected++;
        }
        if (selected == 0)
        {
            MessageBox.Show(this, "至少选择一个匹配字段", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Options.ThreadCount = (int)_threads.Value;
        Options.DontDownloadLyricWithInstrumentInTitle = _instrumental.Checked;
        Options.Save(_settings);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string DisplayName(string name) => name switch
    {
        AutoMatchOptions.Cover => "封面",
        AutoMatchOptions.Lyrics => "歌词",
        AutoMatchOptions.Title => "标题",
        AutoMatchOptions.Artist => "艺术家",
        AutoMatchOptions.Album => "专辑",
        AutoMatchOptions.Year => "年份",
        AutoMatchOptions.Track => "音轨号",
        AutoMatchOptions.Disc => "碟片号",
        AutoMatchOptions.Genre => "流派",
        AutoMatchOptions.Comment => "备注",
        _ => name
    };

    private static string ModeText(AutoMatchWriteMode mode) => mode switch
    {
        AutoMatchWriteMode.SaveToFile => "保存为文件",
        AutoMatchWriteMode.SaveToTagAndFile => "同时写入标签和文件",
        _ => "写入标签"
    };

    private static AutoMatchWriteMode ParseMode(string? value) => value switch
    {
        "保存为文件" => AutoMatchWriteMode.SaveToFile,
        "同时写入标签和文件" => AutoMatchWriteMode.SaveToTagAndFile,
        _ => AutoMatchWriteMode.SaveToTag
    };
}
