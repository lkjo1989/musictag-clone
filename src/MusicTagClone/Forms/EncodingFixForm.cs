using System.Text;

namespace MusicTagClone.Forms;

/// <summary>
/// 编码修正对话框 — 展示多种编码方案及预览效果，允许用户选择正确的编码。
/// 支持单字段模式（从侧边栏编码按钮打开）和多字段模式（从菜单 文件>编码修正 打开）。
/// </summary>
internal class EncodingFixForm : Form
{
    private readonly ListView _listView;
    private readonly string _fieldLabel;
    private readonly string _originalText;
    private readonly Label _hintLabel;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;
    private bool _populated;

    // 多字段模式
    private readonly bool _multiField;
    private readonly KeyValuePair<string, string>[]? _fields;
    private Dictionary<string, string>? _fieldResults;

    /// <summary>用户选择的编码名称</summary>
    public string? SelectedEncoding { get; private set; }

    /// <summary>修正后的文本（单字段模式）</summary>
    public string? FixedText { get; private set; }

    /// <summary>各字段修正后的文本（多字段模式），key 为字段名</summary>
    public IReadOnlyDictionary<string, string>? FixedFields => _fieldResults;

    /// <summary>当前支持的编码列表</summary>
    private static readonly (string Name, string DisplayName)[] Encodings = new[]
    {
        // Unicode
        ("UTF-8", "UTF-8"),
        ("UTF-16", "UTF-16"),
        ("UTF-16BE", "UTF-16BE"),
        // 简体中文
        ("GB2312", "GB2312 (简体中文)"),
        ("GBK", "GBK (简体中文)"),
        ("GB18030", "GB18030 (中文)"),
        // 繁体中文
        ("BIG5", "BIG5 (繁体中文)"),
        // 日语
        ("shift_jis", "Shift-JIS (日语)"),
        ("euc-jp", "EUC-JP (日语)"),
        // 韩语
        ("euc-kr", "EUC-KR (韩语)"),
        // 西欧
        ("ISO-8859-1", "ISO-8859-1 (Latin-1 西欧)"),
        ("windows-1252", "Windows-1252 (西欧)"),
        // 中欧 / 东欧
        ("ISO-8859-2", "ISO-8859-2 (Latin-2 中欧)"),
        ("windows-1250", "Windows-1250 (中欧)"),
        ("IBM852", "IBM852 (DOS Latin-2 中欧)"),
        // 西里尔
        ("ISO-8859-5", "ISO-8859-5 (西里尔)"),
        ("koi8-r", "KOI8-R (俄语)"),
        ("koi8-u", "KOI8-U (乌克兰语)"),
        ("windows-1251", "Windows-1251 (西里尔)"),
        ("cp866", "CP866 (俄语/DOS)"),
        // 阿拉伯语
        ("ASMO-708", "ASMO-708 (阿拉伯语)"),
        ("ISO-8859-6", "ISO-8859-6 (阿拉伯语)"),
        ("windows-1256", "Windows-1256 (阿拉伯语)"),
        // 希腊语
        ("ISO-8859-7", "ISO-8859-7 (希腊语)"),
        ("windows-1253", "Windows-1253 (希腊语)"),
        // 希伯来语
        ("ISO-8859-8", "ISO-8859-8 (希伯来语)"),
        ("windows-1255", "Windows-1255 (希伯来语)"),
        // 波罗的语族
        ("ISO-8859-4", "ISO-8859-4 (波罗的)"),
        ("windows-1257", "Windows-1257 (波罗的)"),
        // 泰语
        ("windows-874", "Windows-874 (泰语)"),
        // 土耳其语
        ("ISO-8859-9", "ISO-8859-9 (土耳其语)"),
        ("windows-1254", "Windows-1254 (土耳其语)"),
        // 越南语
        ("windows-1258", "Windows-1258 (越南语)"),
    };

    public EncodingFixForm(string fieldLabel, string text, string? skipField = null)
        : this(fieldLabel, text, false, null)
    {
    }

    /// <summary>多字段模式构造 — 同时修正多个标签字段</summary>
    public EncodingFixForm(IEnumerable<KeyValuePair<string, string>> fields)
        : this("全部字段", "", true, fields)
    {
    }

    private EncodingFixForm(string fieldLabel, string text, bool multiField, IEnumerable<KeyValuePair<string, string>>? fields)
    {
        _fieldLabel = fieldLabel;
        _originalText = text ?? "";
        _multiField = multiField;
        if (multiField && fields != null)
            _fields = fields.ToArray();

        Text = multiField ? "编码修正 — 全部字段" : $"编码修正 — {GetFieldDisplayName(fieldLabel)}";
        ClientSize = new Size(520, 420);
        MinimumSize = new Size(450, 350);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
            BackColor = Color.White
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));

        _hintLabel = new Label
        {
            Text = multiField
                ? $"共 {_fields!.Length} 个字段需要修正"
                : $"原文: {TruncateText(_originalText, 80)}",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 24,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.Black,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = false,
            Font = new Font("Microsoft YaHei UI", 9F),
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            UseCompatibleStateImageBehavior = false,
            ForeColor = Color.Black,
            BackColor = Color.White
        };
        _listView.Columns.Add("编码", 140);
        _listView.Columns.Add("转换后预览", 340);
        _listView.DoubleClick += OnListDoubleClick;
        _listView.SelectedIndexChanged += OnSelectionChanged;

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = Color.White
        };

        _okBtn = new Button
        {
            Text = "确定",
            Size = new Size(80, 30),
            Margin = new Padding(0),
            Enabled = false,
            ForeColor = Color.Black,
            UseVisualStyleBackColor = true
        };
        _okBtn.Click += OnOk;

        _cancelBtn = new Button
        {
            Text = "取消",
            Size = new Size(80, 30),
            Margin = new Padding(8, 0, 0, 0),
            ForeColor = Color.Black,
            UseVisualStyleBackColor = true
        };
        _cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        buttonPanel.Controls.Add(_cancelBtn);
        buttonPanel.Controls.Add(_okBtn);
        root.Controls.Add(_hintLabel, 0, 0);
        root.Controls.Add(_listView, 0, 1);
        root.Controls.Add(buttonPanel, 0, 2);
        Controls.Add(root);

        Shown += (s, e) => PopulateEncodings();
        Resize += (s, e) => ResizePreviewColumn();
    }

    private void PopulateEncodings()
    {
        if (_populated)
            return;

        _populated = true;
        _listView.BeginUpdate();
        try
        {
            _listView.Items.Clear();

            if (_multiField)
            {
                PopulateMultiField();
            }
            else
            {
                PopulateSingleField();
            }

            if (_listView.Items.Count == 0)
                AddMessageItem("(无可用编码)", "");
        }
        catch
        {
            AddMessageItem("(转换失败)", "");
        }
        finally
        {
            ResizePreviewColumn();
            if (_listView.Items.Count > 0)
            {
                var tag = _listView.Items[0].Tag;
                if (tag is ValueTuple<string, string> || tag is ValueTuple<string, Dictionary<string, string>>)
                    _listView.Items[0].Selected = true;
            }
            _listView.EndUpdate();
        }
    }

    /// <summary>单字段模式 — 展示一个字段在各种编码下的预览</summary>
    private void PopulateSingleField()
    {
        if (string.IsNullOrEmpty(_originalText))
        {
            AddMessageItem("(空文本)", "");
            return;
        }

        byte[] rawBytes = Encoding.Default.GetBytes(_originalText);
        foreach (var (encName, displayName) in Encodings)
        {
            try
            {
                Encoding encoding = Encoding.GetEncoding(encName);
                string converted = encoding.GetString(rawBytes);

                bool hasContent = !string.IsNullOrEmpty(converted);
                bool isSame = string.Equals(converted, _originalText, StringComparison.Ordinal);
                string preview = TruncateText(converted, 80);

                var item = new ListViewItem(new[] { displayName, preview });
                item.Tag = (encName, converted);

                if (isSame)
                    item.BackColor = Color.FromArgb(230, 255, 230);
                else if (!hasContent)
                    item.ForeColor = Color.Black;

                _listView.Items.Add(item);
            }
            catch
            {
            }
        }
    }

    /// <summary>多字段模式 — 每个编码展示所有字段修正后的合并预览</summary>
    private void PopulateMultiField()
    {
        if (_fields == null || _fields.Length == 0)
        {
            AddMessageItem("(空字段)", "");
            return;
        }

        // 预计算：每个字段的原始字节
        var fieldBytes = _fields.Select(f => new
        {
            f.Key,
            f.Value,
            RawBytes = Encoding.Default.GetBytes(f.Value ?? "")
        }).ToArray();

        foreach (var (encName, displayName) in Encodings)
        {
            try
            {
                Encoding encoding = Encoding.GetEncoding(encName);
                var parts = new List<string>();
                bool allSame = true;

                foreach (var fb in fieldBytes)
                {
                    string converted = encoding.GetString(fb.RawBytes);

                    if (!string.IsNullOrEmpty(converted) && !string.Equals(converted, fb.Value, StringComparison.Ordinal))
                        allSame = false;

                    // 只显示非空的结果
                    if (!string.IsNullOrEmpty(converted))
                        parts.Add($"{fb.Key}:{converted}");
                }

                if (parts.Count == 0)
                    continue;

                string preview = string.Join(" | ", parts);
                if (preview.Length > 200)
                    preview = preview.Substring(0, 200) + "…";

                var item = new ListViewItem(new[] { displayName, preview });

                // 保存每个字段的转换结果用于 OK 确认
                var fieldResults = new Dictionary<string, string>();
                foreach (var fb in fieldBytes)
                {
                    string converted = encoding.GetString(fb.RawBytes);
                    fieldResults[fb.Key] = converted;
                }
                item.Tag = (encName, fieldResults);

                if (allSame)
                    item.BackColor = Color.FromArgb(230, 255, 230);

                _listView.Items.Add(item);
            }
            catch
            {
            }
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _okBtn.Enabled = _listView.SelectedItems.Count > 0 &&
            (_listView.SelectedItems[0].Tag is ValueTuple<string, string> ||
             _listView.SelectedItems[0].Tag is ValueTuple<string, Dictionary<string, string>>);
    }

    private void OnListDoubleClick(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count > 0)
        {
            OnOk(sender, e);
        }
    }

    private void OnOk(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count == 0)
            return;

        var item = _listView.SelectedItems[0];

        if (item.Tag is ValueTuple<string, string> result)
        {
            var (encName, converted) = result;
            SelectedEncoding = encName;
            FixedText = converted;
            DialogResult = DialogResult.OK;
            Close();
        }
        else if (item.Tag is ValueTuple<string, Dictionary<string, string>> multiResult)
        {
            var (encName, fieldResults) = multiResult;
            SelectedEncoding = encName;
            _fieldResults = fieldResults;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void AddMessageItem(string encodingText, string previewText)
    {
        _listView.Items.Add(new ListViewItem(new[] { encodingText, previewText })
        {
            ForeColor = Color.Black
        });
    }

    private void ResizePreviewColumn()
    {
        if (_listView.Columns.Count < 2)
            return;

        int previewWidth = _listView.ClientSize.Width - _listView.Columns[0].Width - 4;
        _listView.Columns[1].Width = previewWidth > 80 ? previewWidth : 80;
    }

    private static string TruncateText(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text))
            return "(空)";
        return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "…";
    }

    /// <summary>字段英文 key → 中文显示名称</summary>
    private static string GetFieldDisplayName(string fieldName)
    {
        return fieldName.ToLowerInvariant() switch
        {
            "title" => "标题",
            "artist" => "艺术家",
            "album" => "专辑",
            "year" => "年份",
            "genre" => "风格",
            "albumartist" => "专辑艺术家",
            "composer" => "作曲家",
            "lyricist" => "作词家",
            "comment" => "注释",
            "lyrics" => "歌词",
            _ => fieldName
        };
    }
}
