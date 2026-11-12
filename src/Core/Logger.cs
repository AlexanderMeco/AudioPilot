using System.Text;

namespace AudioPilot.Core;

/// <summary>极简文本日志：写到 exe 同目录 logs\yyyy-MM-dd.log。</summary>
internal sealed class Logger
{
    private const int KeepDays = 30;

    private readonly string _directory;
    private readonly object _sync = new();

    public Logger(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        CleanupOldLogs();
    }

    /// <summary>常驻程序按天生成日志文件，启动时只保留最近 30 天，避免无限堆积。</summary>
    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-KeepDays);
            foreach (var file in Directory.GetFiles(_directory, "*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
        }
        catch
        {
            // 清理失败（例如磁盘只读）不能影响程序启动。
        }
    }

    public string DirectoryPath => _directory;

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_sync)
        {
            try
            {
                var file = Path.Combine(_directory, $"{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 日志失败不能影响主流程（例如磁盘只读）。
            }
        }
    }
}
