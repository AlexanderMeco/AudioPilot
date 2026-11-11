#Requires AutoHotkey v2.0
#SingleInstance Force

; AudioPilot 轻量版：直接调用未公开的 IPolicyConfig::SetDefaultEndpoint。
; 零外部依赖、无需编译、切换在毫秒级完成。
; 设备 ID 可在 C# 版「设置 -> 设备清单」复制，或用 SoundVolumeView 查看。
; 把下面每组的 out / mic 换成实际端点 ID（形如 {0.0.0.00000000}.{guid}）即可使用。

profiles := [
    { name: "会议", out: "", mic: "" },
    { name: "影音", out: "", mic: "" },
    { name: "游戏", out: "", mic: "" }
]

; CLSID_CPolicyConfigClient / IID_IPolicyConfig
PC := ComObject("{870AF99C-171D-4F9E-AF0D-E63DF40C2BC9}",
                "{F8679F50-850A-41CF-9C72-430F290290C8}")

; vtable 第 13 号槽就是 SetDefaultEndpoint(deviceId, ERole)
SetDefault(id, role) => ComCall(13, PC, "Str", id, "Int", role)

Apply(p) {
    if (!p.out || !p.mic) {
        TrayTip("分组「" p.name "」还没绑定设备 ID", "AudioPilot")
        return
    }
    for role in [0, 1, 2] {          ; eConsole / eMultimedia / eCommunications
        SetDefault(p.out, role)      ; 输出端点
        SetDefault(p.mic, role)      ; 输入端点
    }
    TrayTip("已切换到：" p.name, "AudioPilot")
}

idx := 0
RAlt & 0::Apply(profiles[1])        ; 右 Alt + 0
RAlt & 1::Apply(profiles[2])
RAlt & 2::Apply(profiles[3])
RAlt & 9:: {                        ; 右 Alt + 9 循环下一组
    global idx
    idx := Mod(idx + 1, profiles.Length)
    Apply(profiles[idx + 1])
}
RAlt::Send "{RAlt}"                 ; 保留单独按右 Alt 的原生行为（不需要可删）
