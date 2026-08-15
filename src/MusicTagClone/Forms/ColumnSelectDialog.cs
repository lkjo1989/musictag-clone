using MusicTagClone.Models;
using Newtonsoft.Json;

namespace MusicTagClone.Forms;

/// <summary>
/// 自定义显示列对话框 — 管理列可见性和显示顺序。
/// </summary>
public class ColumnSelectDialog : Form
{
    private readonly ListView _listView;
    private readonly Button _btnMoveUp;
    private readonly Button _btnMoveDown;
    private readonly Button _btnReset;
    private readonly Button _btnOk;
    private readonly Button _btnCancel;

    /// <summary>对话框返回的列设置（OK 时有效）</summary>
    public List<ColumnHeaderInfo>? Result { get; private set; }

    private readonly List<ColumnHeaderInfo> _columns;
    private bool _isUpdatingCheckState;

    public ColumnSelectDialog(List<ColumnHeaderInfo> columns)
    {
        // Deep copy — 不修改传入的原始数据
        var json = JsonConvert.SerializeObject(columns);
        _columns = JsonConvert.DeserializeObject<List<ColumnHeaderInfo>>(json) ?? new();

        Text = "自定义显示列";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        SizeGripStyle = SizeGripStyle.Hide;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(320, 560);
        MinimumSize = new Size(320, 560);
        MaximumSize = new Size(320, 600);
        Font = new Font("Tahoma", 9F, FontStyle.Regular);
        BackColor = Color.White;

        // ---- ListView ----
        _listView = new ListView
        {
            CheckBoxes = true,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details,
            Location = new Point(0, 0),
            Size = new Size(180, ClientSize.Height),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            BorderStyle = BorderStyle.Fixed3D,
        };
        _listView.Columns.Add("列名", 178);
        _listView.ItemCheck += OnItemCheck;

        // ---- Buttons ----
        _btnMoveUp = new Button
        {
            Text = "上移",
            Location = new Point(190, 65),
            Size = new Size(85, 23),
            UseVisualStyleBackColor = true,
            TabIndex = 0,
        };
        _btnMoveUp.Click += OnMoveUp;

        _btnMoveDown = new Button
        {
            Text = "下移",
            Location = new Point(190, 95),
            Size = new Size(85, 23),
            UseVisualStyleBackColor = true,
            TabIndex = 1,
        };
        _btnMoveDown.Click += OnMoveDown;

        _btnReset = new Button
        {
            Text = "重置",
            Location = new Point(190, 135),
            Size = new Size(85, 23),
            UseVisualStyleBackColor = true,
            TabIndex = 2,
        };
        _btnReset.Click += OnReset;

        _btnOk = new Button
        {
            Text = "确定",
            Location = new Point(190, ClientSize.Height - 100),
            Size = new Size(85, 23),
            UseVisualStyleBackColor = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            TabIndex = 3,
        };
        _btnOk.Click += OnOk;

        _btnCancel = new Button
        {
            Text = "取消",
            Location = new Point(190, ClientSize.Height - 70),
            Size = new Size(85, 23),
            UseVisualStyleBackColor = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            TabIndex = 4,
        };
        _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] {
            _listView, _btnMoveUp, _btnMoveDown, _btnReset, _btnOk, _btnCancel
        });

        RebuildList();
    }

    /// <summary>用当前 _columns 数据填充 ListView，始终按 DisplayIndex 排序</summary>
    private void RebuildList()
    {
        _isUpdatingCheckState = true;

        var sorted = _columns.OrderBy(c => c.DisplayIndex).ToList();

        _listView.BeginUpdate();
        _listView.Items.Clear();

        var imageList = new ImageList { ImageSize = new Size(1, 18) };
        _listView.SmallImageList = imageList;

        foreach (var col in sorted)
        {
            var displayName = ColumnHeaderInfo.DisplayNames.TryGetValue(col.Name, out var name)
                ? name : col.Name;

            var item = new ListViewItem(displayName)
            {
                Checked = col.IsShow,
                Tag = col,
            };
            _listView.Items.Add(item);
        }

        _listView.EndUpdate();
        _isUpdatingCheckState = false;
    }

    /// <summary>CheckBox 切换 — 仅切换可见性，不改变顺序</summary>
    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_isUpdatingCheckState) return;

        var col = _listView.Items[e.Index].Tag as ColumnHeaderInfo;
        if (col == null) return;

        // 仅切换可见性，不改变 DisplayIndex
        col.IsShow = e.NewValue == CheckState.Checked;
    }

    /// <summary>上移选中项</summary>
    private void OnMoveUp(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count == 0) return;
        var item = _listView.SelectedItems[0];
        int idx = item.Index;
        if (idx <= 0) return;

        var col = item.Tag as ColumnHeaderInfo;
        var prevItem = _listView.Items[idx - 1];
        var prevCol = prevItem.Tag as ColumnHeaderInfo;
        if (col == null || prevCol == null) return;

        // 交换 DisplayIndex
        (col.DisplayIndex, prevCol.DisplayIndex) = (prevCol.DisplayIndex, col.DisplayIndex);

        RebuildList();
        _listView.Items[idx - 1].Selected = true;
        _listView.EnsureVisible(idx - 1);
    }

    /// <summary>下移选中项</summary>
    private void OnMoveDown(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count == 0) return;
        var item = _listView.SelectedItems[0];
        int idx = item.Index;
        if (idx >= _listView.Items.Count - 1) return;

        var col = item.Tag as ColumnHeaderInfo;
        var nextItem = _listView.Items[idx + 1];
        var nextCol = nextItem.Tag as ColumnHeaderInfo;
        if (col == null || nextCol == null) return;

        // 交换 DisplayIndex
        (col.DisplayIndex, nextCol.DisplayIndex) = (nextCol.DisplayIndex, col.DisplayIndex);

        RebuildList();
        _listView.Items[idx + 1].Selected = true;
        _listView.EnsureVisible(idx + 1);
    }

    /// <summary>重置为默认设置</summary>
    private void OnReset(object? sender, EventArgs e)
    {
        var defaults = ColumnHeaderInfo.CreateDefaults();
        _columns.Clear();
        _columns.AddRange(defaults);
        RebuildList();
    }

    /// <summary>确定 — 更新 DisplayIndex 并返回结果</summary>
    private void OnOk(object? sender, EventArgs e)
    {
        // 根据列表顺序刷新 DisplayIndex
        for (int i = 0; i < _listView.Items.Count; i++)
        {
            var col = _listView.Items[i].Tag as ColumnHeaderInfo;
            if (col != null) col.DisplayIndex = i;
        }

        Result = _columns;
        DialogResult = DialogResult.OK;
        Close();
    }
}
