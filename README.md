# AudioGroup · 音频设备分组热键切换

Windows 上多个音频输出 / 输入设备，按场景一键同时切换。
一个分组 = 一台输出 + 一台输入（可为通话角色另指定设备）；按下热键后对输出与输入分别设置
`eConsole` / `eMultimedia` / `eCommunications` 三种默认角色（共 6 次调用），随后回读校验。

## 快速开始

1. 拿到 `publish` 包（已含 `AudioGroup.exe`）可直接跳到第 2 步；需要从源码重新构建时双击 `build.bat`
   （需要 .NET 8 SDK：`winget install Microsoft.DotNet.SDK.8`），产物是 self-contained 单文件，可整包拷到其他电脑。
2. 运行 exe，托盘出现图标；同目录已有 `profiles.json`（预置 3 个空分组）。
3. 双击托盘图标 → **设置** → 在「输出设备 / 输入设备」下拉框里点选真实设备 → **保存并生效**。
4. 按 `右Alt + 0 / 1 / 2` 切换到会议 / 影音 / 游戏；`右Alt + 9` 循环下一组。

> 可选：exe 同目录放一个 `switch.wav`，切换成功时播放它；否则用系统提示音。
> 提示音走当前默认输出设备，因此也顺手验证了切换是否真的生效。

**只读自检**：命令行执行 `AudioGroup.exe --selftest`，程序不会显示托盘、不会装钩子、不会改动任何设备，
只把活动设备清单、三角色当前默认值、命中的 PolicyConfig 接口写到 exe 同目录的 `selftest.txt`。
绑定设备前先跑一次，可以确认 COM 互操作在这台机器上是否正常。

## 热键

| 热键 | 作用 |
| --- | --- |
| 右Alt + 0 / 1 / 2 | 切换到第 1 / 2 / 3 组 |
| 右Alt + 9 | 循环下一组 |
| 新增分组 | 自动分配下一个空闲数字键（从 0 开始，9 留给循环键） |
| 设置窗口 | 分组热键与循环热键均可自定义：在录入框里按住修饰键再敲主键 |

热键通过 `WH_KEYBOARD_LL` 低级键盘钩子实现 —— `RegisterHotKey` 的 `MOD_ALT` 不区分左右 Alt，
无法表达「右 Alt」。钩子回调只做按键判断，实际切换投递到 UI 线程串行执行，避免阻塞系统钩子链。

> 如果你使用 AltGr 键盘布局，右 Alt 是 AltGr（用于输入特殊字符），建议在设置里换成别的修饰键。

## 蓝牙耳机

蓝牙耳机通常同时暴露 **Stereo**（高音质）与 **Hands-Free**（通话）两个输出 / 输入端点，
Windows 在通话时还会自行切换到 Hands-Free。设置窗口中的「通话输出 / 通话输入」：

- 留空 → 与普通输出 / 输入相同（eCommunications 也指向同一设备）；
- 单独选择 → 仅把 eCommunications 指向指定端点（例如 Hands-Free）。

## 配置 `profiles.json`

| 字段 | 说明 |
| --- | --- |
| `autoStart` / `playSound` / `showOsd` | 开机自启（HKCU Run，免 UAC）/ 提示音 / 右下角 OSD |
| `reapplyOnDeviceChange` / `reapplyDebounceMs` | 设备插拔后自动重新应用当前分组，默认开、防抖 2000ms |
| `cycleHotkey` | 循环切换热键 |
| `profiles[].output` / `input` | 设备引用：`id`（WASAPI 端点 ID）+ `name`（兜底名称） |
| `profiles[].outputComm` / `inputComm` | 可选，通话角色专用设备 |
| `profiles[].hotkey` | 修饰键 + 主键，修饰键支持 `RightAlt` / `LeftAlt` / `Alt` / `Ctrl` / `Shift` / `Win` |
| `fallback` | 备用后端，默认关闭 |

设备按 **ID 精确匹配**；驱动重装或换 USB 口导致 ID 失效时，按名称模糊兜底，并写日志 + 明确提示，
绝不静默失败。「设置 → 设备清单」页可查看 / 复制完整端点 ID，并显示每个设备当前承担的默认角色。

## 备用后端

主内核是进程内 `IPolicyConfig::SetDefaultEndpoint`，带三版本 QueryInterface 回退
（`IPolicyConfig` → `IPolicyConfigVista` → `IUnknown`），声明参考 MIT 许可的
`frgnca/AudioDeviceCmdlets` 源码。若某次调用返回失败 HRESULT 且设置里启用了备用后端，会自动降级：

- `soundvolumeview`（推荐）：填 `SoundVolumeView.exe` 完整路径，能覆盖全部 3 个角色；
- `audiodevicecmdlets`：需已安装 PowerShell 模块。该模块只能设 `eMultimedia` + `eCommunications`，
  **不设置 eConsole**，仅作最后兜底。

## 已知限制

- `IPolicyConfig` 是 Windows 未公开接口，未来系统版本可能变化 —— 这正是保留备用后端的原因。
- 已经打开的程序不一定跟随默认设备变化（部分软件在启动时锁定设备），必要时重开该程序。
- Windows 有时会在插入设备时自行改默认设备；本程序会在防抖后把当前分组拉回来。
- 日志位于 `logs\yyyy-MM-dd.log`，包含命中的 COM 接口名、6 次角色调用结果与回读校验结果。

## 工程结构

```
AudioGroup.csproj
Assets/app.ico                应用与托盘图标（内嵌，单文件发布也随包）
tools/make_icon.py            图标生成脚本（纯 Python 标准库，可自行改配色）
build.bat                     发布脚本
profiles.json                 默认配置（可手工编辑）
AHK/AudioGroup.ahk            AHK v2 直调 COM 的十几行轻量版
src/Interop/                  Win32 P/Invoke、Core Audio 与 IPolicyConfig COM 声明
src/Models/                   配置模型、热键名称转换
src/Core/                     设备枚举与匹配、切换内核、热键钩子、设备通知、编排、日志、自启
src/UI/                       托盘、设置窗口、OSD、提示音、热键录入控件
src/Program.cs                入口、单实例、生命周期
```

## AHK 轻量版

`AHK\AudioGroup.ahk` 直接用 `ComObject(CLSID, IID)` + `ComCall(13, ...)` 调用同一个
`SetDefaultEndpoint`，零外部依赖、无需编译、毫秒级。把脚本里的 `out` / `mic` 换成设备 ID 即可用。
它适合在 C# 工程构建之前临时顶上，也适合排查「到底是程序问题还是系统问题」。需要 AHK v2.0+。
