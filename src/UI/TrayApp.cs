using System.Drawing;
using System.Windows.Forms;
using AudioPilot.Core;
using AudioPilot.Models;

namespace AudioPilot.UI;

/// <summary>
/// 系统托盘宿主。程序没有主窗口，ApplicationContext 一直运行到用户点「退出」。
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly SwitchService _service;
    private readonly DeviceCatalog _catalog;
    private readonly HotkeyManager _hotkeys;
    private readonly Icon _appIcon;
    private SettingsForm? _settingsForm;

    /// <summary>
    /// 从嵌入资源加载自定义图标。单文件发布时资源随程序集一起打包，
    /// 不需要在 exe 旁边额外放 ico 文件；加载失败时才退回系统图标。
    /// </summary>
    internal static Icon LoadAppIcon()
    {
        try
        {
            using var stream = typeof(TrayApp).Assembly
                .GetManifestResourceStream("AudioPilot.Assets.app.ico");
            if (stream is not null) return new Icon(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载内嵌图标失败：{ex.Message}");
        }
        // Clone 一份，避免退出时 dispose 影响系统共享图标。
        return (Icon)SystemIcons.Application.Clone();
    }

    public TrayApp(SwitchService service, DeviceCatalog catalog, HotkeyManager hotkeys)
    {
        _service = service;
        _catalog = catalog;
        _hotkeys = hotkeys;
        _appIcon = LoadAppIcon();

        _menu = new ContextMenuStrip();
        _icon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "AudioPilot · 音频分组切换",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.DoubleClick += (_, _) => OpenSettings();
        _icon.BalloonTipClicked += (_, _) => OpenSettings();

        _service.StateChanged += RebuildMenu;
        _service.Alert += ShowAlert;
        RebuildMenu();
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();

        foreach (var profile in _service.Config.Profiles)
        {
            var target = profile;
            var item = new ToolStripMenuItem($"{target.Name}    [{target.Hotkey.Display}]")
            {
                Checked = string.Equals(target.Name, _service.Config.ActiveProfileName, StringComparison.Ordinal)
            };
            item.Click += (_, _) => _service.ApplyProfile(target);
            _menu.Items.Add(item);
        }

        _menu.Items.Add(new ToolStripSeparator());
        var cycle = new ToolStripMenuItem($"循环下一组    [{_service.Config.CycleHotkey.Display}]");
        cycle.Click += (_, _) => _service.CycleNext();
        _menu.Items.Add(cycle);

        _menu.Items.Add(new ToolStripSeparator());
        var settings = new ToolStripMenuItem("设置...");
        settings.Click += (_, _) => OpenSettings();
        _menu.Items.Add(settings);

        var devices = new ToolStripMenuItem("设备清单...");
        devices.Click += (_, _) => OpenSettings(initialTab: 1);
        _menu.Items.Add(devices);

        _menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitThread();
        _menu.Items.Add(exit);
    }

    private void OpenSettings(int initialTab = 0)
    {
        if (_settingsForm is not null && !_settingsForm.IsDisposed)
        {
            _settingsForm.Activate();
            _settingsForm.BringToFront();
            return;
        }

        _settingsForm = new SettingsForm(_service.Config, _catalog, OnConfigSaved, initialTab);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    private void OnConfigSaved(AppConfig config)
    {
        _service.ReloadConfig(config);
        _hotkeys.Rebind(config);
        RebuildMenu();
    }

    private void ShowAlert(string message)
    {
        _icon.ShowBalloonTip(5000, "AudioPilot", message, ToolTipIcon.Warning);
    }

    /// <summary>首次运行或配置异常时给用户一个明确入口。</summary>
    public void ShowStartupHint(string message)
    {
        _icon.ShowBalloonTip(6000, "AudioPilot", message, ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        _service.StateChanged -= RebuildMenu;
        _service.Alert -= ShowAlert;
        _settingsForm?.Close();
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _appIcon.Dispose();
        base.ExitThreadCore();
    }
}
