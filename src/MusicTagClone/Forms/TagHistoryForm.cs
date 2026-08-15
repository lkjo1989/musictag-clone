using MusicTagClone.Interfaces;
using MusicTagClone.Models;

namespace MusicTagClone.Forms;

/// <summary>
/// 标签历史对话框
/// 显示某个文件的历史标签记录，允许选择一条恢复到编辑器。
/// 含全部文本字段预览 + 歌词/封面存在标记。
/// </summary>
internal class TagHistoryForm : Form
{
    private readonly ListView _listView;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;
    private readonly ITagHistoryService _historyService;
    private readonly string _filePath;

    /// <summary>用户选中的历史记录（DialogResult == OK 时有值）</summary>
    public TagHistoryRecord? SelectedRecord { get; private set; }

    /// <summary>选中记录关联的封面数据（有封面时非 null）</summary>
    public byte[]? SelectedCoverData { get; private set; }

    public TagHistoryForm(ITagHistoryService historyService, string filePath, List<TagHistoryRecord> records)
    {
        _historyService = historyService;
        _filePath = filePath;

        Text = "标签历史";
        Size = new Size(950, 400);
        MinimumSize = new Size(700, 300);
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var fileName = Path.GetFileName(filePath);
        if (!string.IsNullOrEmpty(fileName))
            Text = $"标签历史 - {fileName}";

        // ----　上方说明标签 ----
        var infoLabel = new Label
        {
            Text = $"共 {records.Count} 条历史记录，双击或选中后点击「确定」恢复：",
            Location = new Point(12, 12),
            Width = 900,
            Height = 22
        };

        // ----　ListView ----
        _listView = new ListView
        {
            Location = new Point(12, 38),
            Width = 910,
            Height = 280,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            HideSelection = false,
            Font = new Font("Microsoft YaHei UI", 9F),
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };

        _listView.Columns.AddRange(new[]
        {
            new ColumnHeader { Text = "标题", Width = 110 },
            new ColumnHeader { Text = "艺术家", Width = 110 },
            new ColumnHeader { Text = "专辑", Width = 110 },
            new ColumnHeader { Text = "年份", Width = 50, TextAlign = HorizontalAlignment.Center },
            new ColumnHeader { Text = "音轨", Width = 45, TextAlign = HorizontalAlignment.Center },
            new ColumnHeader { Text = "碟号", Width = 40, TextAlign = HorizontalAlignment.Center },
            new ColumnHeader { Text = "风格", Width = 65 },
            new ColumnHeader { Text = "专辑艺术家", Width = 90 },
            new ColumnHeader { Text = "作曲家", Width = 80 },
            new ColumnHeader { Text = "作词家", Width = 70 },
            new ColumnHeader { Text = "歌词", Width = 40, TextAlign = HorizontalAlignment.Center },
            new ColumnHeader { Text = "封面", Width = 55, TextAlign = HorizontalAlignment.Center },
            new ColumnHeader { Text = "记录时间", Width = 130 },
        });

        // 填充数据
        foreach (var r in records)
        {
            var item = new ListViewItem(r.Title ?? "")
            {
                Tag = r
            };
            item.SubItems.AddRange(new[]
            {
                r.Artist ?? "",
                r.Album ?? "",
                r.Year ?? "",
                r.TrackStr ?? "",
                r.DiscStr ?? "",
                r.Genre ?? "",
                r.AlbumArtist ?? "",
                r.Composer ?? "",
                r.Lyricist ?? "",
                !string.IsNullOrEmpty(r.Lyrics) ? "✓" : "",
                GetCoverStatus(r.CoverPath),
                r.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            });
            _listView.Items.Add(item);
        }

        // 默认选中第一条
        if (_listView.Items.Count > 0)
            _listView.Items[0].Selected = true;

        _listView.DoubleClick += OnItemDoubleClick;

        // ----　按钮 ----
        _okBtn = new Button
        {
            Text = "确定",
            Location = new Point(710, 330),
            Width = 90,
            Height = 28,
        };
        _okBtn.Click += OnOkClick;

        _cancelBtn = new Button
        {
            Text = "取消",
            Location = new Point(810, 330),
            Width = 90,
            Height = 28,
        };
        _cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        // ----　组装 ----
        Controls.AddRange(new Control[] { infoLabel, _listView, _okBtn, _cancelBtn });
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count > 0)
        {
            SelectedRecord = _listView.SelectedItems[0].Tag as TagHistoryRecord;
            if (SelectedRecord != null && !string.IsNullOrEmpty(SelectedRecord.CoverPath))
            {
                SelectedCoverData = _historyService.ReadCoverData(SelectedRecord.Serial);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            MessageBox.Show(this, "请先选择一条历史记录", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private string GetCoverStatus(string? coverPath)
    {
        if (string.IsNullOrEmpty(coverPath)) return "";
        return _historyService.CoverExists(coverPath) ? "✓" : "已删除";
    }

    private void OnItemDoubleClick(object? sender, EventArgs e)
    {
        OnOkClick(sender, e);
    }
}
