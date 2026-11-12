# AudioPilot

<p align="center">
  <img src="Assets/app-preview.png" width="88" alt="AudioPilot icon">
</p>

> 一个热键，同时切换 Windows 的输出与输入音频设备。

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-0078D6)
![License MIT](https://img.shields.io/badge/license-MIT-green)

AudioPilot 是一个常驻 Windows 系统托盘的轻量级音频设备管理工具。它将「音频输出设备 + 音频输入设备」组合成可自定义的场景分组（会议、影音、游戏、办公等），通过一个全局快捷键即可完成整组切换，无需反复打开 Windows 声音设置逐项选择。

## 为什么需要它

Windows 支持连接多种音频设备，例如显示器扬声器、USB 麦克风、蓝牙耳机、独立声卡等。但当设备越来越多，频繁切换输入和输出设备就会变得繁琐。

不同使用场景往往需要不同的设备组合：

| 使用场景 | 音频输出 | 音频输入 |
| --- | --- | --- |
| 日常办公 | 显示器扬声器 | USB 麦克风 |
| 会议通话 | 蓝牙耳机 | 蓝牙耳机麦克风 |
| 游戏娱乐 | 独立声卡 | 桌面麦克风 |
| 影音播放 | 桌面音箱 | 不需要切换 |

Windows 原生设置需要分别选择输入和输出设备，而且系统还为不同用途维护了独立的默认设备角色。频繁切换时，不仅操作繁琐，还容易遗漏某个设备或角色，导致声音从错误的设备播放，或者应用程序使用了不期望的麦克风。

**AudioPilot 将输入设备和输出设备组合成一个场景，通过一个全局快捷键即可完成整组切换。**

它同时处理 Windows 的三个默认设备角色（`eConsole`、`eMultimedia`、`eCommunications`），并在切换后重新读取系统默认设备进行校验，尽可能确保切换结果符合预期。

不需要打开系统设置，不需要逐项选择设备，也不必记住每个场景对应的设备组合。

让音频设备切换从多步手动操作，变成一次快捷键操作。

## 功能特性

- **场景分组**：每组绑定一台输出设备和一台输入设备，可增删、重命名、自定义热键。
- **全局热键**：默认「右 Alt + 数字」（从 0 开始），另有一键循环切换。
- **三角色全设 + 回读校验**：输出与输入各 3 次调用，共 6 次；失败明确提示并记录日志。
- **蓝牙友好**：Stereo 与 Hands-Free 端点可为通话角色分别指定。
- **设备变化自愈**：插拔设备后防抖重新应用当前分组，防止 Windows 自动更改默认设备。
- **匹配稳健**：按 WASAPI 端点 ID 精确绑定，ID 失效时按名称兜底并告警，绝不静默失败。
- **低打扰交互**：托盘菜单、无焦点 OSD、可选提示音、开机自启（免 UAC）、单实例。
- **可降级**：主内核失败时可切换到外部命令后端。

## 快速开始

### 环境要求

- Windows 10 / 11（x64）
- 构建需要 .NET 8 SDK（可通过 `winget install Microsoft.DotNet.SDK.8` 安装）。
- 运行不需要额外安装运行时：发布产物为 self-contained 单文件 exe。

### 构建

仓库不包含编译产物，克隆后执行：

```bat
build.bat
```

产物位于 `publish\AudioPilot.exe`，连同 `profiles.json` 可将整个目录复制到其他电脑。

离线环境可改用依赖框架的单文件构建（目标机器需已安装 .NET 8 运行时）：

```bat
dotnet publish AudioPilot.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

### 使用

1. 运行 `publish\AudioPilot.exe`，系统托盘出现蓝色图标。
2. 双击托盘图标，进入 **设置**，为每个分组选择输出和输入设备。
3. 点击 **保存并生效**，完成场景配置。
4. 使用 `右 Alt + 0 / 1 / 2` 切换分组，使用 `右 Alt + 9` 循环切换下一组。

首次绑定设备前，建议先执行一次只读自检：

```bat
publish\AudioPilot.exe --selftest
```

它不显示托盘、不安装钩子、不修改任何设备，只将活动端点清单、三个角色的当前默认值、命中的 PolicyConfig 接口与图标资源状态写入 `selftest.txt`，用于确认 COM 互操作在本机正常。

> 可选：在 exe 同目录放置 `switch.wav`，切换成功时播放该文件；否则使用系统提示音。提示音通过切换后的默认输出设备播放，也可作为验证切换效果的一种方式。

## 热键

| 热键 | 作用 |
| --- | --- |
| 右 Alt + 0 / 1 / 2 | 切换到第 1 / 2 / 3 组 |
| 右 Alt + 9 | 循环下一组 |
| 新增分组 | 自动分配下一个空闲数字键（从 0 起，9 留给循环键） |
| 设置窗口 | 每个分组及循环热键均可自定义，按住修饰键再敲主键即可录入 |

修饰键支持 `RightAlt` / `LeftAlt` / `Alt` / `Ctrl` / `Shift` / `Win` 及其组合。

热键通过 `WH_KEYBOARD_LL` 低级键盘钩子实现。`RegisterHotKey` 的 `MOD_ALT` 不区分左右 Alt，无法准确表达「右 Alt」。

钩子回调只做按键判断，切换动作投递到 UI 线程串行执行，避免阻塞系统钩子链。

> 使用 AltGr 键盘布局时，右 Alt 通常承担 AltGr 功能（用于输入特殊字符），建议在设置中换成其他修饰键。

## 蓝牙耳机

蓝牙耳机通常同时暴露 **Stereo**（高音质）与 **Hands-Free**（通话）两个音频端点。Windows 在通话过程中也可能自动切换到 Hands-Free。

AudioPilot 支持在设置窗口中单独指定「通话输出 / 通话输入」：

- **留空**：与普通输出 / 输入相同，`eCommunications` 也指向同一设备。
- **单独选择**：仅将 `eCommunications` 指向指定端点，例如 Hands-Free。

这样可以让音乐、视频和游戏使用高音质 Stereo 端点，同时让语音通话使用 Hands-Free 端点。

## 工作原理

```text
热键 / 托盘
    ↓
读取 profiles.json
    ↓
解析目标设备端点 ID
    ↓
输出与输入各 3 次 SetDefaultEndpoint（共 6 次）
    ↓
GetDefaultAudioEndpoint 回读校验
    ↓
OSD + 提示音
```

### 核心实现

- **主内核**：通过进程内未公开的 COM 接口 `IPolicyConfig::SetDefaultEndpoint` 修改系统默认设备，带三版本 QueryInterface 回退（`IPolicyConfig` → `IPolicyConfigVista` → `IUnknown`）。

  该接口不带输入 / 输出参数，设备方向由传入的端点 ID 决定（Render 或 Capture）。

- **设备管理**：设备枚举、友好名称读取、默认设备回读与插拔通知均使用公开的 Windows Core Audio API。

- **备用后端**（默认关闭，可在设置中启用）：仅当主内核返回失败 HRESULT 时降级到外部命令。

  - `soundvolumeview`：可覆盖全部 3 个角色，推荐，需自行提供程序路径。
  - `audiodevicecmdlets`：只能设置 `eMultimedia` + `eCommunications`，不设置 `eConsole`，仅作为最后兜底。

  两种工具均不随本项目分发。

## 配置

配置保存在 exe 同目录的 `profiles.json`，可在设置窗口编辑，也可手工修改。

| 字段 | 说明 |
| --- | --- |
| `autoStart` / `playSound` / `showOsd` | 开机自启（HKCU Run，免 UAC）/ 提示音 / 右下角 OSD |
| `reapplyOnDeviceChange` / `reapplyDebounceMs` | 设备插拔后自动重新应用当前分组，默认开启，防抖 2000ms |
| `cycleHotkey` | 循环切换热键 |
| `profiles[].output` / `input` | 设备引用：`id`（WASAPI 端点 ID）+ `name`（兜底名称） |
| `profiles[].outputComm` / `inputComm` | 可选，通话角色专用端点 |
| `profiles[].hotkey` | 修饰键 + 主键 |
| `fallback` | 备用后端配置，默认关闭 |

仓库根目录的 `profiles.json` 只是默认空模板（3 个未绑定设备的分组）。

程序运行时写入的是 exe 同目录下的配置文件，其中包含本机真实设备 ID，属于个人数据，不应提交到版本库。

「设置 → 设备清单」页面可查看、复制完整端点 ID，并显示每个设备当前承担的默认角色。

## 已知限制

- `IPolicyConfig` 是 Windows **未公开接口**，不受微软兼容性承诺保护，未来系统版本可能变化，因此保留了备用后端。
- 已经打开的程序不一定跟随系统默认设备变化。部分软件会在启动时锁定设备，必要时需要重启该软件。
- Windows 有时会在插入设备时自行修改默认设备；本程序会在防抖后重新应用当前分组。
- 日志位于 `logs\yyyy-MM-dd.log`，包含命中的 COM 接口名、6 次角色调用结果与回读校验结果。

## 项目结构

```text
AudioPilot.csproj
LICENSE                       MIT 许可证
THIRD_PARTY_NOTICES.md        第三方组件与许可声明
build.bat                     单文件发布脚本
profiles.json                 默认空配置模板
Assets/icon-source.png        图标源图（600x600 RGBA）
Assets/app.ico                应用与托盘图标（内嵌，单文件发布也随包）
Assets/app-preview.png        图标预览
tools/make_icon.py            由源图生成多尺寸 ICO（纯 Python 标准库）
AHK/AudioPilot.ahk            AHK v2 直调 COM 的轻量版
src/Interop/                  Win32 P/Invoke、Core Audio 与 IPolicyConfig COM 声明
src/Models/                   配置模型、热键名称转换
src/Core/                     设备枚举与匹配、切换内核、热键钩子、设备通知、编排、日志、自启
src/UI/                       托盘、设置窗口、OSD、提示音、热键录入控件
src/Program.cs                入口、单实例、只读自检、生命周期
```

## AHK 轻量版

`AHK\AudioPilot.ahk` 通过 `ComObject(CLSID, IID)` + `ComCall(13, ...)` 直接调用同一个 `SetDefaultEndpoint`。

零外部依赖、无需编译、毫秒级。将脚本中的 `out` / `mic` 替换为实际端点 ID 即可使用，适合临时使用，也适合排查「到底是程序问题还是系统问题」。

需要 AutoHotkey v2.0+。

## 贡献

欢迎提交 Issue 与 Pull Request。

开发时请留意：

- 保持分层：`Interop`（COM / Win32 声明）、`Models`（配置模型）、`Core`（业务逻辑）、`UI`（界面）。
- 注释使用中文；接口声明的方法顺序必须与 Windows SDK / 上游实现一致，前 10 个占位方法不可调用。
- 提交前执行 `dotnet build -c Release` 与 `AudioPilot.exe --selftest`。
- 不要提交构建产物、日志以及包含真实设备 ID 的个人配置。

## 致谢

- [frgnca/AudioDeviceCmdlets](https://github.com/frgnca/AudioDeviceCmdlets)（MIT）：`IPolicyConfig` 系列接口的 GUID 与 vtable 方法顺序参考自该项目源码，版权归原作者所有。
- Microsoft Windows Core Audio API：设备枚举、默认设备回读与插拔通知。
- [NirSoft SoundVolumeView](https://www.nirsoft.net/utils/sound_volume_view.html)：可选备用后端，本项目不附带、不分发该工具。

第三方许可全文见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 许可证

[MIT](LICENSE) © 2026 AudioPilot contributors