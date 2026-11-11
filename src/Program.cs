using System.Windows.Forms;
using AudioPilot.Core;
using AudioPilot.Interop;
using AudioPilot.Models;
using AudioPilot.UI;

namespace AudioPilot;

internal static class Program
{
    private const string MutexName = @"Local\AudioPilot_SingleInstance_v1";

    [STAThread]
    private static int Main(string[] args)
    {
        // 只读自检：不显示托盘、不安装钩子、不修改任何默认设备，结果写到 selftest.txt。
        if (args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
        {
            return RunSelfTest();
        }

        // 单实例：托盘程序重复启动没有意义，直接提示后退出。
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("AudioPilot 已经在运行，请查看系统托盘图标。", "AudioPilot",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var appDirectory = ConfigStore.AppDirectory;
        var logger = new Logger(Path.Combine(appDirectory, "logs"));
        var store = new ConfigStore();
        var config = store.Load(out string? startupWarning);
        if (!string.IsNullOrEmpty(startupWarning)) logger.Warn(startupWarning);

        var ui = new UiDispatcher(logger);
        var catalog = new DeviceCatalog(logger);
        var fallback = new FallbackSwitcher(logger);
        var switcher = new DeviceSwitcher(catalog, fallback, logger);

        // Feedback 需要读取「当前」配置，用闭包延迟到 service 创建之后。
        SwitchService? service = null;
        var feedback = new Feedback(() => service!.Config, appDirectory);
        service = new SwitchService(config, store, catalog, switcher, feedback, logger, ui);

        var hotkeys = new HotkeyManager(ui, logger, (profile, isCycle) =>
        {
            if (isCycle) service.CycleNext();
            else if (profile is not null) service.ApplyProfile(profile);
        });
        hotkeys.Rebind(config);

        using var tray = new TrayApp(service, catalog, hotkeys);

        try
        {
            service.Start();
            hotkeys.Install();

            if (!string.IsNullOrEmpty(startupWarning))
            {
                tray.ShowStartupHint(startupWarning + " 双击托盘图标可打开设置。");
            }

            Application.Run(tray);
        }
        finally
        {
            // 顺序：先摘钩子、再停服务、最后释放 COM 通知与 UI 队列。
            hotkeys.Dispose();
            service.Dispose();
            catalog.Dispose();
            ui.Dispose();
            logger.Info("AudioPilot 已退出。");
        }

        return 0;
    }

    /// <summary>
    /// 只读自检：验证设备枚举、当前三角色默认值、PolicyConfig 三版本 QI 是否可用。
    /// 不创建托盘、不注册设备通知、不安装全局钩子、绝不调用 SetDefaultEndpoint。
    /// 命令行用法：AudioPilot.exe --selftest，结果在 exe 同目录的 selftest.txt。
    /// </summary>
    private static int RunSelfTest()
    {
        var lines = new List<string>();
        try
        {
            var directory = ConfigStore.AppDirectory;
            var logger = new Logger(Path.Combine(directory, "logs"));
            using var catalog = new DeviceCatalog(logger);

            var outputs = catalog.ListActive(render: true);
            var inputs = catalog.ListActive(render: false);

            lines.Add($"活动输出端点：{outputs.Count} 个");
            foreach (var device in outputs.Take(10)) lines.Add($"  [输出] {device.Name} | {device.Id}");
            lines.Add($"活动输入端点：{inputs.Count} 个");
            foreach (var device in inputs.Take(10)) lines.Add($"  [输入] {device.Name} | {device.Id}");

            lines.Add("当前默认设备（只读回读）：");
            foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
            {
                lines.Add($"  输出/{role} = {catalog.GetDefaultId(true, role) ?? "(无)"}");
                lines.Add($"  输入/{role} = {catalog.GetDefaultId(false, role) ?? "(无)"}");
            }

            var policy = new PolicyConfigClient();
            lines.Add($"PolicyConfig 命中接口：{policy.InterfaceName}，可用：{policy.IsAvailable}");

            // 确认托盘图标资源确实打进了程序集（单文件发布时尤其重要）。
            using (var icon = TrayApp.LoadAppIcon())
            {
                lines.Add($"托盘图标：{icon.Width}x{icon.Height}，内嵌资源加载成功");
            }

            // 注册后立刻由 using 注销，用来验证 IMMNotificationClient 的托管 CCW 是否可用。
            var notifier = new DeviceNotifier(() => { });
            bool registered = catalog.RegisterNotifications(notifier);
            lines.Add(registered
                ? "设备变化通知注册：成功"
                : "设备变化通知注册：失败（仅影响自动重应用，主切换不受影响）");
            lines.Add("自检完成：未修改任何默认设备设置。");

            File.WriteAllLines(Path.Combine(directory, "selftest.txt"), lines);
            return policy.IsAvailable && outputs.Count > 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            lines.Add($"自检异常：{ex}");
            try { File.WriteAllLines(Path.Combine(ConfigStore.AppDirectory, "selftest.txt"), lines); }
            catch { /* 写不了结果也不能让进程崩掉 */ }
            return 3;
        }
    }
}
