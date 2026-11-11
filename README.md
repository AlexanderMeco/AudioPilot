<p align="center">
  <img src="Assets/app-preview.png" width="88" alt="AudioGroup icon">
</p>

# AudioGroup

> 一个热键，同时切换 Windows 的输出与输入音频设备。

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-0078D6)
![License MIT](https://img.shields.io/badge/license-MIT-green)

AudioGroup 是一个常驻系统托盘的 Windows 小工具：把「输出设备 + 输入设备」打包成场景分组
（会议 / 影音 / 游戏……），按一个全局热键即可整组切换，不用再打开声音设置逐项点选。

## 为什么需要它

Windows 至今**没有公开的设置默认音频设备 API**，而且对输出和输入各维护 **3 套独立的默认值**：

| 角色 | 典型用途 |
| --- | --- |
| `eConsole` | 大多数桌面程序 |
| `eMultimedia` | 音乐 / 视频播放 |
| `eCommunications` | 会议、语音通话 |

只改其中一两个，就会出现「音乐已经从耳机播放，会议软件却还在用旧麦克风」。
手动切换既慢又容易漏掉通信角色。AudioGroup 每次把三个角色全部设置一遍，
并在切换后回读校验，确认系统确实指向了目标设备。

## 功能特性

- **场景分组**：每组绑定一台输出 + 一台输入，可增删、重命名、自定义热键
- **全局热键**：默认「右 Alt + 数字」（从 0 开始），另有一键循环切换
- **三角色全设 + 回读校验**：输出与输入各 3 次调用共 6 次，失败明确提示并记日志
- **蓝牙友好**：Stereo 与 Hands-Free 端点可为通话角色分别指定
- **设备变化自愈**：插拔设备后防抖重新应用当前分组，防止 Windows 抢默认设备
- **匹配稳健**：按 WASAPI 端点 ID 精确绑定，ID 失效时按名称兜底并告警，绝不静默失败
- **低打扰交互**：托盘菜单、无焦点 OSD、可选提示音、开机自启（免 UAC）、单实例
- **可降级**：主内核失败时可切换到外部命令后端

## 快速开始

### 环境要求

- Windows 10 / 11（x64）
- 构建需要 .NET 8 SDK（可用 `winget install Microsoft.DotNet.SDK.8` 安装）
- 运行不需要额外运行时：发布产物是 self-contained 单文件 exe

### 构建

仓库不包含编译产物，克隆后执行：

```bat
build.bat
```

产物位于 `publish\AudioGroup.exe`，连同 `profiles.json` 可整个目录拷到其他电脑。
离线环境可改用依赖框架的单文件构建（目标机器需已装 .NET 8 运行时）：

```bat
dotnet publish AudioGroup.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

### 使用

1. 运行 `publish\AudioGroup.exe`，托盘出现蓝色图标
2. 双击托盘 → **设置** → 给每个分组在下拉框选输出 / 输入设备 → **保存并生效**
3. `右Alt + 0 / 1 / 2` 切换分组，`右Alt + 9` 循环下一组

首次绑定设备前，建议先做一次只读自检：

```bat
AudioGroup.exe --selftest
```

它不显示托盘、不安装钩子、不改动任何设备，只把活动端点清单、三个角色的当前默认值、
命中的 PolicyConfig 接口与图标资源状态写入 `selftest.txt`，用于确认 COM 互操作在本机正常。

> 可选：在 exe 同目录放 `switch.wav`，切换成功时播放它；否则使用系统提示音。
> 提示音走当前默认输出设备，顺手验证切换是否真的生效。

## 热键

| 热键 | 作用 |
| --- | --- |
| 右Alt + 0 / 1 / 2 | 切换到第 1 / 2 / 3 组 |
| 右Alt + 9 | 循环下一组 |
| 新增分组 | 自动分配下一个空闲数字键（从 0 起，9 留给循环键） |
| 设置窗口 | 每个分组与循环热键都能自定义：按住修饰键再敲主键即可录入 |

修饰键支持 `RightAlt` / `LeftAlt` / `Alt` / `Ctrl` / `Shift` / `Win` 及其组合。
热键通过 `WH_KEYBOARD_LL` 低级键盘钩子实现 —— `RegisterHotKey` 的 `MOD_ALT` 不区分左右 Alt，
无法表达「右 Alt」；钩子回调只做按键判断，切换动作投递到 UI 线程串行执行，避免阻塞系统钩子链。

> 使用 AltGr 键盘布局时右 Alt 是 AltGr（用于输入特殊字符），建议在设置里换成别的修饰键。

## 蓝牙耳机

蓝牙耳机通常同时暴露 **Stereo**（高音质）与 **Hands-Free**（通话）两个输出 / 输入端点，
Windows 在通话时还会自行切换到 Hands-Free。设置窗口中的「通话输出 / 通话输入」：

- 留空 → 与普通输出 / 输入相同，`eCommunications` 也指向同一设备；
- 单独选择 → 仅把 `eCommunications` 指向指定端点（例如 Hands-Free）。

## 工作原理

```
热键 / 托盘 → 读取 profiles.json → 解析端点 ID
            → 输出与输入各 3 次 SetDefaultEndpoint（共 6 次）
            → GetDefaultAudioEndpoint 回读校验 → OSD + 提示音
```

- 主内核是进程内未公开的 COM 接口 `IPolicyConfig::SetDefaultEndpoint`，带三版本
  QueryInterface 回退（`IPolicyConfig` → `IPolicyConfigVista` → `IUnknown`）。
  该接口不带输入 / 输出参数，方向由传入的端点 ID 决定（Render 或 Capture）。
- 设备枚举、友好名称读取、默认设备回读与插拔通知均来自公开的 Windows Core Audio API。
- **备用后端**（默认关闭，设置里可开）：仅当主内核返回失败 HRESULT 时降级到外部命令。
  `soundvolumeview` 能覆盖全部 3 个角色（推荐，需自行提供程序路径）；
  `audiodevicecmdlets` 只能设 `eMultimedia` + `eCommunications`，不设置 `eConsole`，仅作最后兜底。
  两者都不随本项目分发。

## 配置

配置保存在 exe 同目录的 `profiles.json`，可在设置窗口编辑，也可手工修改。

| 字段 | 说明 |
| --- | --- |
| `autoStart` / `playSound` / `showOsd` | 开机自启（HKCU Run，免 UAC）/ 提示音 / 右下角 OSD |
| `reapplyOnDeviceChange` / `reapplyDebounceMs` | 设备插拔后自动重新应用当前分组，默认开、防抖 2000ms |
| `cycleHotkey` | 循环切换热键 |
| `profiles[].output` / `input` | 设备引用：`id`（WASAPI 端点 ID）+ `name`（兜底名称） |
| `profiles[].outputComm` / `inputComm` | 可选，通话角色专用端点 |
| `profiles[].hotkey` | 修饰键 + 主键 |
| `fallback` | 备用后端配置，默认关闭 |

仓库根目录的 `profiles.json` 只是**默认空模板**（3 个未绑定设备的分组）；
程序运行时写入的是 exe 同目录那份，包含你的真实设备 ID，属于个人数据，不进版本库。

「设置 → 设备清单」页可查看 / 复制完整端点 ID，并显示每个设备当前承担的默认角色。

## 已知限制

- `IPolicyConfig` 是 Windows **未公开接口**，未来系统版本可能变化 —— 这正是保留备用后端的原因。
- 已经打开的程序不一定跟随默认设备变化（部分软件在启动时锁定设备），必要时重开该程序。
- Windows 有时会在插入设备时自行改默认设备；本程序会在防抖后把当前分组拉回来。
- 日志位于 `logs\yyyy-MM-dd.log`，包含命中的 COM 接口名、6 次角色调用结果与回读校验结果。

## 项目结构

```
AudioGroup.csproj
LICENSE                       MIT 许可证
THIRD_PARTY_NOTICES.md        第三方组件与许可声明
build.bat                     单文件发布脚本
profiles.json                 默认空配置模板
Assets/app.ico                应用与托盘图标（内嵌，单文件发布也随包）
Assets/app-preview.png        图标预览
tools/make_icon.py            图标生成脚本（纯 Python 标准库，可自行改配色）
AHK/AudioGroup.ahk            AHK v2 直调 COM 的十几行轻量版
src/Interop/                  Win32 P/Invoke、Core Audio 与 IPolicyConfig COM 声明
src/Models/                   配置模型、热键名称转换
src/Core/                     设备枚举与匹配、切换内核、热键钩子、设备通知、编排、日志、自启
src/UI/                       托盘、设置窗口、OSD、提示音、热键录入控件
src/Program.cs                入口、单实例、只读自检、生命周期
```

## AHK 轻量版

`AHK\AudioGroup.ahk` 直接用 `ComObject(CLSID, IID)` + `ComCall(13, ...)` 调用同一个
`SetDefaultEndpoint`，零外部依赖、无需编译、毫秒级。把脚本里的 `out` / `mic` 换成端点 ID 即可用，
适合临时顶上，也适合排查「到底是程序问题还是系统问题」。需要 AutoHotkey v2.0+。

## 贡献

欢迎提交 Issue 与 Pull Request。开发时请留意：

- 保持分层：`Interop`（COM / Win32 声明）、`Models`（配置模型）、`Core`（业务逻辑）、`UI`（界面）
- 注释使用中文；接口声明的方法顺序必须与 Windows SDK / 上游实现一致，前 10 个占位方法不可调用
- 提交前执行 `dotnet build -c Release` 与 `AudioGroup.exe --selftest`
- 不要提交构建产物、日志以及包含真实设备 ID 的个人配置

## 致谢

- [frgnca/AudioDeviceCmdlets](https://github.com/frgnca/AudioDeviceCmdlets)（MIT）：
  `IPolicyConfig` 系列接口的 GUID 与 vtable 方法顺序参考自该项目源码，版权归原作者所有。
- Microsoft Windows Core Audio API：设备枚举、默认设备回读与插拔通知。
- [NirSoft SoundVolumeView](https://www.nirsoft.net/utils/sound_volume_view.html)：
  可选备用后端，本项目不附带、不分发该工具。

第三方许可全文见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 许可证

[MIT](LICENSE) © 2026 AudioGroup contributors
