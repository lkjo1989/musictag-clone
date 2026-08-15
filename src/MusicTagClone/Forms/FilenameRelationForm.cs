using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusicTagClone.Forms;

internal sealed class FilenameRelationForm : Form
{
    private static readonly string[] Presets =
    {
        "@2 - @1", "@1 - @2", "@5. @1", "@5. @2 - @1",
        "@4@5. @1", "@4@5. @2 - @1",
    };

    private static readonly string[] RegexFields =
    {
        "不使用", "标题", "艺术家", "专辑", "碟号", "音轨", "年份", "备注",
    };

    private readonly ISettingsService _settings;
    private readonly FilenameRelationMode? _forcedMode;
    private readonly TabControl _tabs;
    private readonly TabPage _regexPage;
    private readonly List<RadioButton> _presetButtons = new();
    private readonly RadioButton _customPatternRadio;
    private readonly TextBox _customPatternBox;
    private readonly RadioButton _renameRadio;
    private readonly RadioButton _changeTagsRadio;
    private readonly CheckBox _renameRelatedCheck;
    private readonly TextBox _regexBox;
    private readonly DataGridView _groupGrid;
    private readonly Button _clearButton;
    private readonly GroupBox _regexModeGroup;
    private readonly GroupBox _exampleGroup;
    private readonly Label _exampleLabel;
    private readonly RadioButton _regexChangeTagsRadio;

    public FilenameRelationOptions Options { get; private set; } = new();

    public FilenameRelationForm(ISettingsService settings, FilenameRelationMode? forcedMode = null)
    {
        _settings = settings;
        _forcedMode = forcedMode;

        Text = "文件名相关";
        ClientSize = new Size(720, 620);
        MinimumSize = new Size(650, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        ShowIcon = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        ForeColor = Color.Black;
        BackColor = Color.White;

        _tabs = new TabControl { Dock = DockStyle.Fill };
        var patternPage = new TabPage("模式匹配") { BackColor = Color.White, ForeColor = Color.Black };
        _regexPage = new TabPage("正则表达式") { BackColor = Color.White, ForeColor = Color.Black };
        _tabs.TabPages.Add(patternPage);
        _tabs.TabPages.Add(_regexPage);
        _tabs.Selecting += OnSelectingTab;
        _tabs.SelectedIndexChanged += (_, _) => LayoutRegexPage();

        var patternGroup = new GroupBox
        {
            Text = "文件名模式", Left = 12, Top = 12, Width = 688, Height = 200,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.Black,
        };
        for (var i = 0; i < Presets.Length; i++)
        {
            var radio = new RadioButton
            {
                Text = Presets[i], Left = 18 + (i % 2) * 320, Top = 27 + (i / 2) * 34,
                Width = 290, ForeColor = Color.Black,
            };
            _presetButtons.Add(radio);
            patternGroup.Controls.Add(radio);
        }
        _customPatternRadio = new RadioButton
        {
            Text = "自定义", Left = 18, Top = 133, Width = 80, ForeColor = Color.Black,
        };
        _customPatternBox = new TextBox
        {
            Left = 102, Top = 130, Width = 548, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.Black,
        };
        _customPatternBox.TextChanged += (_, _) => _customPatternRadio.Checked = true;
        patternGroup.Controls.Add(_customPatternRadio);
        patternGroup.Controls.Add(_customPatternBox);

        var tokenLabel = new Label
        {
            Text = "@1 标题    @2 艺术家    @3 专辑    @4 碟号    @5 音轨    @6 年份    @7 备注    @8 专辑艺术家    @0 不使用",
            Left = 18, Top = 168, Width = 650, Height = 22, ForeColor = Color.Black,
        };
        patternGroup.Controls.Add(tokenLabel);

        var modeGroup = new GroupBox
        {
            Text = "操作", Left = 12, Top = 224, Width = 688, Height = 116,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.Black,
        };
        _renameRadio = new RadioButton
        {
            Text = "根据标签重命名文件", Left = 18, Top = 27, Width = 210, ForeColor = Color.Black,
        };
        _renameRelatedCheck = new CheckBox
        {
            Text = "同时重命名同名歌词和图片文件", Left = 42, Top = 56, Width = 300, ForeColor = Color.Black,
        };
        _changeTagsRadio = new RadioButton
        {
            Text = "根据文件名修改标签", Left = 18, Top = 84, Width = 210, ForeColor = Color.Black,
        };
        _renameRadio.CheckedChanged += (_, _) => _renameRelatedCheck.Enabled = _renameRadio.Checked;
        modeGroup.Controls.AddRange(new Control[] { _renameRadio, _renameRelatedCheck, _changeTagsRadio });

        var notice = new Label
        {
            Text = "文件扩展名不参与匹配，重命名时会原样保留。",
            Left = 14, Top = 356, Width = 672, Height = 24, ForeColor = Color.Black,
        };
        patternPage.Controls.Add(patternGroup);
        patternPage.Controls.Add(modeGroup);
        patternPage.Controls.Add(notice);

        var regexLabel = new Label
        {
            Text = "正则表达式:", Left = 12, Top = 18, Width = 80, Height = 24, ForeColor = Color.Black,
        };
        _regexBox = new TextBox
        {
            Left = 96, Top = 15, Width = 604,
            ForeColor = Color.Black,
        };

        var mappingLabel = new Label
        {
            Text = "捕获组映射", Left = 12, Top = 55, Width = 120, Height = 24, ForeColor = Color.Black,
        };
        _groupGrid = new DataGridView
        {
            Left = 12, Top = 80, Width = 688, Height = 220,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White, ForeColor = Color.Black, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        };
        _groupGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "group", HeaderText = "组号", ReadOnly = true, Width = 60,
        });
        var fieldColumn = new DataGridViewComboBoxColumn
        {
            Name = "field", HeaderText = "匹配", FlatStyle = FlatStyle.Flat, Width = 150,
        };
        fieldColumn.Items.AddRange(RegexFields.Cast<object>().ToArray());
        _groupGrid.Columns.Add(fieldColumn);
        for (var i = 1; i <= 20; i++) _groupGrid.Rows.Add(i.ToString(), RegexFields[0]);

        _clearButton = new Button
        {
            Text = "清除映射", Left = 600, Top = 53, Width = 100, Height = 28,
            ForeColor = Color.Black,
        };
        _clearButton.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in _groupGrid.Rows) row.Cells[1].Value = RegexFields[0];
        };

        _regexModeGroup = new GroupBox
        {
            Text = "操作模式", Left = 12, Height = 50,
            ForeColor = Color.Black,
        };
        _regexChangeTagsRadio = new RadioButton
        {
            Text = "根据文件名修改标签", Left = 18, Top = 22, Width = 210,
            ForeColor = Color.Black, Checked = true, Enabled = false,
        };
        _regexModeGroup.Controls.Add(_regexChangeTagsRadio);

        _exampleGroup = new GroupBox
        {
            Text = "示例", Left = 12, Height = 100,
            ForeColor = Color.Black,
        };
        _exampleLabel = new Label
        {
            Left = 18, Top = 18, Width = 650, Height = 76, ForeColor = Color.Black,
            Text = "文件名: 03. xxartist - xxtitle\r\n" +
                   "正则表达式: ^(\\d*)\\. (.*) - (.*)$\r\n" +
                   "捕获组: 1 - 音轨号,  2 - 艺术家,  3 - 标题\r\n" +
                   "结果: 标题 - xxtitle, 艺术家 - xxartist, 音轨号 - 3",
        };
        _exampleGroup.Controls.Add(_exampleLabel);

        _regexPage.Controls.Add(regexLabel);
        _regexPage.Controls.Add(_regexBox);
        _regexPage.Controls.Add(mappingLabel);
        _regexPage.Controls.Add(_groupGrid);
        _regexPage.Controls.Add(_clearButton);
        _regexPage.Controls.Add(_regexModeGroup);
        _regexPage.Controls.Add(_exampleGroup);
        _regexPage.Resize += (_, _) => LayoutRegexPage();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.White };
        var okButton = new Button
        {
            Text = "确定", Width = 80, Height = 30, Left = 536, Top = 10,
            Anchor = AnchorStyles.Top | AnchorStyles.Right, ForeColor = Color.Black,
        };
        var cancelButton = new Button
        {
            Text = "取消", Width = 80, Height = 30, Left = 626, Top = 10,
            Anchor = AnchorStyles.Top | AnchorStyles.Right, ForeColor = Color.Black,
            DialogResult = DialogResult.Cancel,
        };
        okButton.Click += OnOk;
        bottom.Controls.Add(okButton);
        bottom.Controls.Add(cancelButton);

        Controls.Add(_tabs);
        Controls.Add(bottom);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        LoadSettings();
        LayoutRegexPage();
    }

    private void LoadSettings()
    {
        _customPatternBox.Text = _settings.FilenameCustomPattern ?? string.Empty;
        var pattern = Presets[0];
        var changeTags = false;
        var renameRelated = false;
        try
        {
            var json = JObject.Parse(_settings.FilenameRelCondition ?? "{}");
            pattern = json.Value<string>("pattern") ?? pattern;
            changeTags = json.Value<bool?>("is_change_tags_mode") ?? false;
            renameRelated = json.Value<bool?>("is_rename_with_extra") ?? false;
        }
        catch (JsonException) { }

        var presetIndex = Array.IndexOf(Presets, pattern);
        if (presetIndex >= 0) _presetButtons[presetIndex].Checked = true;
        else
        {
            _customPatternRadio.Checked = true;
            _customPatternBox.Text = pattern;
        }
        _renameRadio.Checked = !changeTags;
        _changeTagsRadio.Checked = changeTags;
        _renameRelatedCheck.Checked = renameRelated;

        try
        {
            var json = JObject.Parse(_settings.FilenameRelRegexCondition ?? "{}");
            _regexBox.Text = json.Value<string>("regex") ?? string.Empty;
            var map = json["match_group_map"]?.ToObject<Dictionary<int, int>>() ?? new Dictionary<int, int>();
            foreach (var pair in map)
            {
                if (pair.Key >= 1 && pair.Key <= 20 && pair.Value >= 0 && pair.Value < RegexFields.Length)
                    _groupGrid.Rows[pair.Key - 1].Cells[1].Value = RegexFields[pair.Value];
            }
            // 正则Tab只支持"根据文件名修改标签"，忽略保存的模式设置
        }
        catch (JsonException) { }

        _tabs.SelectedIndex = Math.Max(0, Math.Min(1, _settings.FilenameRelSelectedTab));
        if (_forcedMode.HasValue)
        {
            _renameRadio.Checked = _forcedMode.Value == FilenameRelationMode.RenameFiles;
            _changeTagsRadio.Checked = _forcedMode.Value == FilenameRelationMode.ChangeTags;
            if (_forcedMode.Value == FilenameRelationMode.RenameFiles) _tabs.SelectedIndex = 0;
        }
    }

    private void OnSelectingTab(object? sender, TabControlCancelEventArgs e)
    {
        if (_forcedMode == FilenameRelationMode.RenameFiles && e.TabPageIndex == 1) e.Cancel = true;
    }

    /// <summary>
    /// 正则表达式 Tab 采用显式定位布局（不依赖 Anchor）：
    /// 两个分组框贴底，映射表格在中间上下伸缩，正则输入框/清除按钮/分组框宽度随页面宽度自适应。
    /// 每次页面尺寸变化（含切换到本 Tab 时）都重新计算，保证任意窗口大小下内容都完整可见。
    /// </summary>
    private void LayoutRegexPage()
    {
        var w = _regexPage.ClientSize.Width;
        var h = _regexPage.ClientSize.Height;
        if (w <= 0 || h <= 0) return;

        _regexBox.SetBounds(96, 15, w - 108, _regexBox.Height);

        _regexModeGroup.SetBounds(12, h - 6 - _regexModeGroup.Height, w - 24, _regexModeGroup.Height);
        _exampleGroup.SetBounds(12, _regexModeGroup.Top - 8 - _exampleGroup.Height, w - 24, _exampleGroup.Height);

        var gridHeight = _exampleGroup.Top - 10 - _groupGrid.Top;
        _groupGrid.SetBounds(12, _groupGrid.Top, w - 24, Math.Max(120, gridHeight));

        _clearButton.SetBounds(w - 112, 53, 100, 28);

        _exampleLabel.SetBounds(18, 18, _exampleGroup.Width - 36, _exampleLabel.Height);
    }

    private void OnOk(object? sender, EventArgs e)
    {
        var useRegex = _tabs.SelectedIndex == 1;
        var map = ReadRegexMap();
        if (useRegex)
        {
            if (!FilenameRelationService.TryValidateRegex(_regexBox.Text.Trim(), map, out var error))
            {
                MessageBox.Show(this, error, "文件名相关", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        else
        {
            var pattern = SelectedPattern();
            if (!FilenameRelationService.TryValidatePattern(pattern, _changeTagsRadio.Checked, out var error))
            {
                MessageBox.Show(this, error, "文件名相关", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        Options = new FilenameRelationOptions
        {
            Pattern = SelectedPattern(),
            Mode = useRegex
                ? FilenameRelationMode.ChangeTags
                : (_changeTagsRadio.Checked ? FilenameRelationMode.ChangeTags : FilenameRelationMode.RenameFiles),
            RenameRelatedFiles = useRegex ? false : _renameRelatedCheck.Checked,
            UseRegex = useRegex,
            RegexPattern = _regexBox.Text.Trim(),
            RegexGroupMap = map,
        };

        _settings.FilenameCustomPattern = _customPatternBox.Text.Trim();
        _settings.FilenameRelCondition = JsonConvert.SerializeObject(new Dictionary<string, object>
        {
            ["pattern"] = Options.Pattern,
            ["is_change_tags_mode"] = _changeTagsRadio.Checked,
            ["is_rename_with_extra"] = _renameRelatedCheck.Checked,
        });
        _settings.FilenameRelRegexCondition = JsonConvert.SerializeObject(new Dictionary<string, object>
        {
            ["regex"] = Options.RegexPattern,
            ["match_group_map"] = Options.RegexGroupMap,
            ["is_change_tags_mode"] = true,
            ["is_rename_with_extra"] = false,
        });
        _settings.FilenameRelSelectedTab = _tabs.SelectedIndex;
        _settings.Save();

        DialogResult = DialogResult.OK;
        Close();
    }

    private string SelectedPattern()
    {
        for (var i = 0; i < _presetButtons.Count; i++)
            if (_presetButtons[i].Checked) return Presets[i];
        return _customPatternBox.Text.Trim();
    }

    private Dictionary<int, int> ReadRegexMap()
    {
        var result = new Dictionary<int, int>();
        foreach (DataGridViewRow row in _groupGrid.Rows)
        {
            var selected = Convert.ToString(row.Cells[1].Value) ?? RegexFields[0];
            var field = Array.IndexOf(RegexFields, selected);
            if (field > 0) result[row.Index + 1] = field;
        }
        return result;
    }
}
