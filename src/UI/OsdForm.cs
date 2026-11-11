using System.Drawing;
using System.Windows.Forms;

namespace AudioPilot.UI;

/// <summary>
/// 右下角的无焦点提示条。重写 ShowWithoutActivation 并加 WS_EX_NOACTIVATE，
/// 保证切换提示不会抢走当前正在打字 / 开会的窗口焦点。
/// </summary>
internal sealed class OsdForm : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private OsdForm(string text, int durationMs)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(28, 30, 34);
        ForeColor = Color.White;
        Size = new Size(340, 56);
        Opacity = 0.94;

        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 10F),
            ForeColor = Color.White
        };
        Controls.Add(label);

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);

        var timer = new System.Windows.Forms.Timer { Interval = durationMs };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            Close();
        };
        timer.Start();
    }

    public static void ShowOsd(string text, int durationMs = 1800)
    {
        var form = new OsdForm(text, durationMs);
        form.Show();
    }
}
