using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AudioPilot.Models;

namespace AudioPilot.Core;

/// <summary>
/// profiles.json 的读写。配置文件放在 exe 同目录，便于整包拷到别的电脑。
/// 写入采用「临时文件 + 原子替换」，避免异常退出时把配置写坏。
/// </summary>
internal sealed class ConfigStore
{
    /// <summary>单文件发布时 Environment.ProcessPath 仍是用户看到的那个 exe。</summary>
    public static readonly string AppDirectory =
        Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // 配置文件统一用 camelCase，手工编辑更直观
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 让中文分组名在 JSON 里保持可读，而不是变成 \uXXXX
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ConfigPath => Path.Combine(AppDirectory, "profiles.json");

    /// <summary>读取配置；文件不存在时生成默认配置，损坏时备份并返回默认配置。</summary>
    public AppConfig Load(out string? warning)
    {
        warning = null;

        if (!File.Exists(ConfigPath))
        {
            var fresh = AppConfig.CreateDefault();
            Save(fresh);
            warning = "首次运行，已生成默认配置，请在设置窗口绑定设备。";
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config is null || config.Profiles.Count == 0)
            {
                warning = "profiles.json 中没有有效分组，已载入默认配置。";
                return AppConfig.CreateDefault();
            }
            return config;
        }
        catch (Exception ex)
        {
            var backup = Path.Combine(AppDirectory, "profiles.bad.json");
            try { File.Copy(ConfigPath, backup, overwrite: true); } catch { /* 忽略备份失败 */ }
            warning = $"配置读取失败（{ex.Message}），已备份为 profiles.bad.json 并载入默认配置。";
            return AppConfig.CreateDefault();
        }
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, ConfigPath, overwrite: true);
    }
}
