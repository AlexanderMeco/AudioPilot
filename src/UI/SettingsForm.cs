using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using AudioPilot.Core;
using AudioPilot.Interop;
using AudioPilot.Models;

namespace AudioPilot.UI;

/// <summary>
/// 设置窗口：分组增删改、设备下拉绑定、热键录入、蓝牙通话设备覆盖、通用选项、设备清单。
/// 所有修改先落在窗口内的配置副本上，点「保存」才写回并生效。
/// </summary>
internal sealed class SettingsForm : Form
{
    /// <summary>下拉框项：显示友好名称，同时保存 WASAPI 端点 ID。</summary>
    private sealed class ComboItem
    {
        public string Text { get; }
        public DeviceRef Reference { get; }

        public ComboItem(string text, DeviceRef reference)
        {
            Text = text;
            Reference = reference;
        }

        public override string ToString() => Text;
    }

    private readonly DeviceCatalog _catalog;
    private readonly Action<AppConfig> _onSave;
    private readonly AppConfig _config;
    private readonly List<Profile> _profiles;

    private readonly ListBox _list = new();
    private readonly TextBox _nameBox = new();
    private readonly HotkeyInputBox _hotkeyBox = new();
    private readonly ComboBox _outputBox = new();
    private readonly ComboBox _inputBox = new();
    private readonly ComboBox _outputCommBox = new();
    private readonly ComboBox _inputCommBox = new();

    private readonly CheckBox _autoStartBox = new();
    private readonly CheckBox _playSoundBox = new();
    private readonly CheckBox _showOsdBox = new();
    private readonly CheckBox _reapplyBox = new();
    private readonly NumericUpDown _debounceBox = new();
    private readonly HotkeyInputBox _cycleBox = new();
    private readonly CheckBox _fallbackBox = new();
    private readonly ComboBox _fallbackKindBox = new();
    private readonly TextBox _fallbackPathBox = new();

    private readonly ListView _deviceList = new();
    private readonly TabControl _tabs = new();

    private Profile? _current;
    private bool _loading;

    // 切换 ListBox 选中项时 SelectedIndex 已经指向新项，
    // 必须单独记住「正在编辑的旧项索引」，否则会把旧数据写到新行上。
    private int _editingIndex = -1;

    public SettingsForm(AppConfig config, DeviceCatalog catalog, Action<AppConfig> onSave, int initialTab = 0)
    {
        _catalog = catalog;
        _onSave = onSave;
        _config = DeepClone(config);
        _profiles = _config.Profiles;

        Text = "AudioPilot 设置";
        Size = new Size(780, 590);
        MinimumSize = new Size(780, 590);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
        ReloadDevices();
        ReloadProfileList();
        LoadCommonSettings();
        _tabs.SelectedIndex = Math.Clamp(initialTab, 0, _tabs.TabPages.Count - 1);
    }

    /// <summary>用序列化做深拷贝，避免编辑过程中改到运行中的配置对象。</summary>
    private static AppConfig DeepClone(AppConfig source)
    {
        var json = JsonSerializer.Serialize(source, ConfigStore.JsonOptions);
        return JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.JsonOptions) ?? AppConfig.CreateDefault();
    }

    private void BuildUi()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(BuildProfileTab());
        _tabs.TabPages.Add(BuildDeviceTab());
        _tabs.TabPages.Add(BuildCommonTab());
        Controls.Add(_tabs);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
        var save = new Button { Text = "保存并生效", Size = new Size(110, 30), Location = new Point(550, 9) };
        save.Click += (_, _) => SaveAndClose();
        var cancel = new Button { Text = "取消", Size = new Size(80, 30), Location = new Point(670, 9) };
        cancel.Click += (_, _) => Close();
        var hint = new Label
        {
            Text = "配置保存在 exe 同目录的 profiles.json，可整包拷贝到其他电脑。",
            Location = new Point(12, 15),
            Size = new Size(520, 24),
            ForeColor = SystemColors.GrayText
        };
        bottom.Controls.AddRange(new Control[] { hint, save, cancel });
        Controls.Add(bottom);
    }

    private TabPage BuildProfileTab()
    {
        var page = new TabPage("分组与热键");

        var listLabel = new Label { Text = "分组列表", Location = new Point(12, 10), Size = new Size(180, 20) };
        _list.Location = new Point(12, 32);
        _list.Size = new Size(180, 400);
        _list.SelectedIndexChanged += (_, _) => OnProfileSelectionChanged();

        var add = new Button { Text = "新增", Location = new Point(12, 440), Size = new Size(85, 28) };
        add.Click += (_, _) => AddProfile();
        var remove = new Button { Text = "删除", Location = new Point(107, 440), Size = new Size(85, 28) };
        remove.Click += (_, _) => RemoveProfile();

        var group = new GroupBox { Text = "分组设置", Location = new Point(205, 10), Size = new Size(545, 458) };
        group.Controls.Add(MakeLabel("名称", 16, 30));
        _nameBox.SetBounds(124, 27, 400, 24);
        group.Controls.Add(_nameBox);

        group.Controls.Add(MakeLabel("热键", 16, 66));
        _hotkeyBox.SetBounds(124, 63, 220, 24);
        group.Controls.Add(_hotkeyBox);
        group.Controls.Add(MakeTip("按住修饰键再敲主键即可录入，例如「右Alt + 0」", 16, 92, 500));

        group.Controls.Add(MakeLabel("输出设备", 16, 130));
        _outputBox.SetBounds(124, 127, 400, 24);
        group.Controls.Add(_outputBox);

        group.Controls.Add(MakeLabel("输入设备", 16, 166));
        _inputBox.SetBounds(124, 163, 400, 24);
        group.Controls.Add(_inputBox);

        group.Controls.Add(MakeLabel("通话输出", 16, 214));
        _outputCommBox.SetBounds(124, 211, 400, 24);
        group.Controls.Add(_outputCommBox);

        group.Controls.Add(MakeLabel("通话输入", 16, 250));
        _inputCommBox.SetBounds(124, 247, 400, 24);
        group.Controls.Add(_inputCommBox);

        group.Controls.Add(MakeTip(
            "蓝牙耳机通常有 Stereo 与 Hands-Free 两个端点：上面两项留空表示与普通设备一致；\n" +
            "若希望通话时自动使用 Hands-Free（Windows 常自行切换），可在这里单独指定。",
            16, 286, 510, 46));

        group.Controls.Add(MakeTip(
            "设备按 ID 精确绑定，驱动重装后 ID 变化会按名称兜底匹配并在日志中告警。\n" +
            "在「设备清单」页可以复制完整 ID，也建议绑定后确认一次。",
            16, 340, 510, 46));

        page.Controls.AddRange(new Control[] { listLabel, _list, add, remove, group });
        return page;
    }

    private TabPage BuildDeviceTab()
    {
        var page = new TabPage("设备清单");

        var top = new Panel { Dock = DockStyle.Top, Height = 40 };
        var refresh = new Button { Text = "刷新设备", Location = new Point(10, 7), Size = new Size(100, 26) };
        refresh.Click += (_, _) => ReloadDevices();
        var copy = new Button { Text = "复制选中设备 ID", Location = new Point(120, 7), Size = new Size(140, 26) };
        copy.Click += (_, _) => CopySelectedId();
        var tip = new Label
        {
            Text = "仅列出当前活动的端点；「当前默认角色」一列可核对三角色是否指向同一设备。",
            Location = new Point(272, 12),
            Size = new Size(470, 24),
            ForeColor = SystemColors.GrayText
        };
        top.Controls.AddRange(new Control[] { refresh, copy, tip });

        _deviceList.Dock = DockStyle.Fill;
        _deviceList.View = View.Details;
        _deviceList.FullRowSelect = true;
        _deviceList.GridLines = true;
        _deviceList.Columns.Add("方向", 60);
        _deviceList.Columns.Add("设备名称", 220);
        _deviceList.Columns.Add("当前默认角色", 160);
        _deviceList.Columns.Add("设备 ID", 330);

        page.Controls.Add(_deviceList);
        page.Controls.Add(top);
        return page;
    }

    private TabPage BuildCommonTab()
    {
        var page = new TabPage("通用设置");
        int y = 20;

        _autoStartBox.Text = "开机自动启动（写入 HKCU Run，无需管理员权限）";
        _autoStartBox.SetBounds(20, y, 500, 24);
        y += 34;

        _playSoundBox.Text = "切换成功后播放提示音（同时验证新的输出设备是否生效）";
        _playSoundBox.SetBounds(20, y, 500, 24);
        y += 34;

        _showOsdBox.Text = "右下角显示切换提示（不抢焦点）";
        _showOsdBox.SetBounds(20, y, 500, 24);
        y += 34;

        _reapplyBox.Text = "设备插拔后自动重新应用当前分组（防止 Windows 抢默认设备）";
        _reapplyBox.SetBounds(20, y, 520, 24);
        y += 34;

        page.Controls.Add(MakeLabel("防抖时间（毫秒）", 20, y, 130));
        _debounceBox.SetBounds(160, y - 3, 100, 24);
        _debounceBox.Minimum = 500;
        _debounceBox.Maximum = 30000;
        _debounceBox.Increment = 500;
        page.Controls.Add(_debounceBox);
        y += 42;

        page.Controls.Add(MakeLabel("循环下一组热键", 20, y));
        _cycleBox.SetBounds(160, y - 3, 220, 24);
        page.Controls.Add(_cycleBox);
        y += 48;

        _fallbackBox.Text = "启用备用切换后端（仅在主内核失败时使用）";
        _fallbackBox.SetBounds(20, y, 500, 24);
        y += 32;

        page.Controls.Add(MakeLabel("备用后端类型", 20, y));
        _fallbackKindBox.SetBounds(160, y - 3, 200, 24);
        _fallbackKindBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _fallbackKindBox.Items.AddRange(new object[] { "soundvolumeview", "audiodevicecmdlets" });
        page.Controls.Add(_fallbackKindBox);
        y += 38;

        page.Controls.Add(MakeLabel("外部程序路径", 20, y));
        _fallbackPathBox.SetBounds(160, y - 3, 520, 24);
        page.Controls.Add(_fallbackPathBox);
        y += 34;

        page.Controls.Add(MakeTip(
            "soundvolumeview：填 SoundVolumeView.exe 的完整路径，能覆盖全部 3 个角色（推荐）。\n" +
            "audiodevicecmdlets：填 PowerShell 路径或留空，需已安装该模块；它只能设多媒体 + 通信两个角色，\n" +
            "无法设置 eConsole，仅作最后兜底。",
            20, y, 660, 62));

        page.Controls.AddRange(new Control[]
        {
            _autoStartBox, _playSoundBox, _showOsdBox, _reapplyBox
        });
        return page;
    }

    // 默认宽度必须小于右侧控件的起始 x（124），否则标签会盖住输入框左侧文字。
    private static Label MakeLabel(string text, int x, int y, int width = 100) =>
        new() { Text = text, Location = new Point(x, y), Size = new Size(width, 24), TextAlign = ContentAlignment.MiddleLeft };

    private static Label MakeTip(string text, int x, int y, int width, int height = 24) =>
        new()
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            ForeColor = SystemColors.GrayText
        };

    private void ReloadDevices()
    {
        var render = _catalog.ListActive(true).ToList();
        var capture = _catalog.ListActive(false).ToList();

        // 记住当前选择，刷新后恢复，避免打断正在进行的编辑。
        var snapshot = new[]
        {
            SelectedReference(_outputBox), SelectedReference(_inputBox),
            SelectedReference(_outputCommBox), SelectedReference(_inputCommBox)
        };

        FillCombo(_outputBox, render, sameAsMain: false);
        FillCombo(_inputBox, capture, sameAsMain: false);
        FillCombo(_outputCommBox, render, sameAsMain: true);
        FillCombo(_inputCommBox, capture, sameAsMain: true);

        var boxes = new[] { _outputBox, _inputBox, _outputCommBox, _inputCommBox };
        for (int i = 0; i < boxes.Length; i++) SelectCombo(boxes[i], snapshot[i]);

        FillDeviceList(render, capture);
    }

    private static void FillCombo(ComboBox combo, List<AudioEndpoint> devices, bool sameAsMain)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Items.Clear();
        combo.Items.Add(new ComboItem(sameAsMain ? "（与普通设备相同）" : "（未设置）", new DeviceRef()));
        foreach (var device in devices)
        {
            combo.Items.Add(new ComboItem(device.Name,
                new DeviceRef { Id = device.Id, Name = device.Name }));
        }
        if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
    }

    private static void SelectCombo(ComboBox combo, DeviceRef reference)
    {
        if (!string.IsNullOrEmpty(reference.Id))
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboItem item &&
                    string.Equals(item.Reference.Id, reference.Id, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }
        if (!string.IsNullOrEmpty(reference.Name))
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboItem item &&
                    string.Equals(item.Reference.Name, reference.Name, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }
        combo.SelectedIndex = 0;
    }

    private static DeviceRef SelectedReference(ComboBox combo) =>
        combo.SelectedItem is ComboItem item ? item.Reference.Clone() : new DeviceRef();

    private void FillDeviceList(List<AudioEndpoint> render, List<AudioEndpoint> capture)
    {
        _deviceList.BeginUpdate();
        _deviceList.Items.Clear();
        foreach (var device in render.Concat(capture))
        {
            var roles = new List<string>();
            foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
            {
                var current = _catalog.GetDefaultId(device.IsRender, role);
                if (string.Equals(current, device.Id, StringComparison.OrdinalIgnoreCase))
                {
                    roles.Add(role switch
                    {
                        ERole.eConsole => "控制台",
                        ERole.eMultimedia => "多媒体",
                        _ => "通信"
                    });
                }
            }
            _deviceList.Items.Add(new ListViewItem(new[]
            {
                device.FlowText,
                device.Name,
                roles.Count == 0 ? "-" : string.Join(" / ", roles),
                device.Id
            }));
        }
        _deviceList.EndUpdate();
    }

    private void CopySelectedId()
    {
        if (_deviceList.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选中一行设备。", "AudioPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var id = _deviceList.SelectedItems[0].SubItems[3].Text;
        Clipboard.SetText(id);
        MessageBox.Show("设备 ID 已复制到剪贴板。", "AudioPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ReloadProfileList()
    {
        _loading = true;
        _list.Items.Clear();
        foreach (var profile in _profiles) _list.Items.Add(profile.Name);
        _list.SelectedIndex = _profiles.Count > 0 ? 0 : -1;
        _loading = false;
        LoadEditor();
    }

    private void OnProfileSelectionChanged()
    {
        if (_loading) return;
        CommitEditor();
        _current = _list.SelectedIndex >= 0 ? _profiles[_list.SelectedIndex] : null;
        LoadEditor();
    }

    private void LoadEditor()
    {
        _loading = true;
        _editingIndex = _list.SelectedIndex;
        _current = _editingIndex >= 0 ? _profiles[_editingIndex] : null;

        _nameBox.Text = _current?.Name ?? "";
        _hotkeyBox.SetBinding(_current?.Hotkey);
        SelectCombo(_outputBox, _current?.Output ?? new DeviceRef());
        SelectCombo(_inputBox, _current?.Input ?? new DeviceRef());
        SelectCombo(_outputCommBox, _current?.OutputComm ?? new DeviceRef());
        SelectCombo(_inputCommBox, _current?.InputComm ?? new DeviceRef());
        _loading = false;
    }

    private void CommitEditor()
    {
        if (_current is null || _loading) return;

        _current.Name = _nameBox.Text.Trim();
        if (_hotkeyBox.Binding is not null) _current.Hotkey = _hotkeyBox.Binding.Clone();
        _current.Output = SelectedReference(_outputBox);
        _current.Input = SelectedReference(_inputBox);
        _current.OutputComm = SelectedReference(_outputCommBox);
        _current.InputComm = SelectedReference(_inputCommBox);

        int index = _editingIndex;
        if (index >= 0 && index < _list.Items.Count && _list.Items[index].ToString() != _current.Name)
        {
            _loading = true;
            _list.Items[index] = _current.Name;
            _loading = false;
        }
    }

    private void AddProfile()
    {
        CommitEditor();
        int index = 1;
        while (_profiles.Any(p => p.Name == $"新分组{index}")) index++;

        var profile = new Profile
        {
            Name = $"新分组{index}",
            Hotkey = new HotkeyBinding { Modifiers = "RightAlt", Key = NextFreeKey() }
        };
        _profiles.Add(profile);
        _list.Items.Add(profile.Name);
        _list.SelectedIndex = _list.Items.Count - 1;
    }

    /// <summary>新增分组时自动分配一个未被占用的数字键，从 0 开始（9 保留给循环热键）。</summary>
    private string NextFreeKey()
    {
        for (int i = 0; i <= 8; i++)
        {
            var key = i.ToString();
            if (!_profiles.Any(p =>
                    string.Equals(p.Hotkey.Modifiers, "RightAlt", StringComparison.OrdinalIgnoreCase) &&
                    p.Hotkey.Key == key))
            {
                return key;
            }
        }
        return "9";
    }

    private void RemoveProfile()
    {
        if (_list.SelectedIndex < 0) return;
        if (_profiles.Count <= 1)
        {
            MessageBox.Show("至少需要保留一个分组。", "AudioPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var name = _profiles[_list.SelectedIndex].Name;
        if (MessageBox.Show($"确定删除分组「{name}」？", "AudioPilot",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        int index = _list.SelectedIndex;
        _loading = true;
        _current = null;
        _editingIndex = -1;
        _profiles.RemoveAt(index);
        _list.Items.RemoveAt(index);
        _list.SelectedIndex = Math.Min(index, _profiles.Count - 1);
        _loading = false;
        LoadEditor();
    }

    private void LoadCommonSettings()
    {
        _autoStartBox.Checked = _config.AutoStart;
        _playSoundBox.Checked = _config.PlaySound;
        _showOsdBox.Checked = _config.ShowOsd;
        _reapplyBox.Checked = _config.ReapplyOnDeviceChange;
        _debounceBox.Value = Math.Clamp(_config.ReapplyDebounceMs, 500, 30000);
        _cycleBox.SetBinding(_config.CycleHotkey);
        _fallbackBox.Checked = _config.Fallback.Enabled;
        _fallbackKindBox.SelectedItem = _config.Fallback.Kind;
        if (_fallbackKindBox.SelectedIndex < 0) _fallbackKindBox.SelectedIndex = 0;
        _fallbackPathBox.Text = string.Equals(_config.Fallback.Kind, "audiodevicecmdlets", StringComparison.OrdinalIgnoreCase)
            ? _config.Fallback.PowerShellPath
            : _config.Fallback.SoundVolumeViewPath;
    }

    private void SaveAndClose()
    {
        CommitEditor();

        // 校验：名称非空且唯一。
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in _profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                MessageBox.Show("分组名称不能为空。", "AudioPilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!names.Add(profile.Name))
            {
                MessageBox.Show($"分组名称「{profile.Name}」重复。", "AudioPilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        _config.AutoStart = _autoStartBox.Checked;
        _config.PlaySound = _playSoundBox.Checked;
        _config.ShowOsd = _showOsdBox.Checked;
        _config.ReapplyOnDeviceChange = _reapplyBox.Checked;
        _config.ReapplyDebounceMs = (int)_debounceBox.Value;
        if (_cycleBox.Binding is not null) _config.CycleHotkey = _cycleBox.Binding.Clone();
        _config.Fallback.Enabled = _fallbackBox.Checked;
        _config.Fallback.Kind = _fallbackKindBox.SelectedItem?.ToString() ?? "soundvolumeview";
        if (string.Equals(_config.Fallback.Kind, "audiodevicecmdlets", StringComparison.OrdinalIgnoreCase))
            _config.Fallback.PowerShellPath = _fallbackPathBox.Text.Trim();
        else
            _config.Fallback.SoundVolumeViewPath = _fallbackPathBox.Text.Trim();

        _onSave(_config);
        Close();
    }
}
