# 第三方声明

本项目主体代码以 MIT 许可发布，见 [LICENSE](LICENSE)。
以下列出被参考、或可被使用者自行接入的第三方组件。

## frgnca/AudioDeviceCmdlets（MIT）

- 项目地址：https://github.com/frgnca/AudioDeviceCmdlets
- 用途：
  - `src/Interop/PolicyConfig.cs` 中 `IPolicyConfig` / `IPolicyConfigVista` / 兜底接口的
    GUID 与 vtable 方法顺序，参考自该项目的 `SOURCE/IPolicyConfig*.cs` 与
    `SOURCE/PolicyConfigClient.cs`。
  - `src/Core/FallbackSwitcher.cs` 可选地把该 PowerShell 模块作为降级后端调用；
    本项目**不附带、不分发**该模块，启用前需使用者自行安装。
- 许可证：MIT。按许可证要求，原文与版权声明保留如下。

---

MIT License

Copyright (c) 2016-2022 Francois Gendron

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

## NirSoft SoundVolumeView（免费软件，非开源）

`src/Core/FallbackSwitcher.cs` 支持把 SoundVolumeView 作为可选的外部命令后端。
本项目**不包含、不分发、不链接**其可执行文件；使用者如需启用，需自行从
https://www.nirsoft.net/utils/sound_volume_view.html 获取并遵守其许可条款。

## Microsoft Windows Core Audio API / Windows SDK

`IMMDeviceEnumerator`、`IMMDevice`、`IPropertyStore`、`IMMNotificationClient` 等接口的
IID 与方法顺序来自公开的 Windows SDK 文档。`IPolicyConfig` 是 Windows 未公开接口，
本项目仅按社区广泛使用的调用约定使用，不分发任何 Windows 系统组件。
