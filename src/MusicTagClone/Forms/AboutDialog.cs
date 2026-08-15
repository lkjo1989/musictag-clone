namespace MusicTagClone.Forms;

/// <summary>
/// 关于对话框
/// </summary>
public partial class AboutDialog : Form
{
    public AboutDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "关于 MusicTag Clone";
        Size = new Size(400, 280);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var nameLabel = new Label
        {
            Text = "MusicTag Clone",
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Location = new Point(20, 20),
            Width = 340,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var versionLabel = new Label
        {
            Text = "版本 1.0.0",
            Location = new Point(20, 60),
            Width = 340,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var descLabel = new Label
        {
            Text = "开源音乐标签管理工具\n\n基于 .NET WinForms 构建\n使用 TagLibSharp 处理音频标签\n歌词搜索支持网易云音乐和QQ音乐\n封面搜索支持 iTunes API",
            Location = new Point(20, 90),
            Width = 340,
            Height = 100,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var okBtn = new Button
        {
            Text = "确定",
            Location = new Point(160, 200),
            Width = 80
        };
        okBtn.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { nameLabel, versionLabel, descLabel, okBtn });
    }
}
