using System.IO;
using System.Text.Json;

namespace PCLBAI.Services;

/// <summary>全局配置，保存在 启动器目录下的 PCLBAI.settings.json。</summary>
public sealed class AppSettings
{
    /// <summary>游戏目录，即 .minecraft 的实际位置。</summary>
    public string GameFolder { get; set; } = "";

    /// <summary>下载源：0 = 自动（镜像优先，失败回落官方），1 = 仅镜像，2 = 仅官方。</summary>
    public int DownloadSource { get; set; }

    /// <summary>下载并发数。</summary>
    public int MaxDownloadThreads { get; set; } = 24;

    /// <summary>校验时跳过资源文件（资产）以加快速度。</summary>
    public bool SkipAssetsVerify { get; set; }

    /// <summary>上次选择的版本。</summary>
    public string LastVersion { get; set; } = "";

    /// <summary>java.exe 路径，用于运行 Forge/NeoForge 安装器。</summary>
    public string JavaPath { get; set; } = "";

    private static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "PCLBAI.settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var text = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<AppSettings>(text);
                if (s != null)
                {
                    s.Normalize();
                    return s;
                }
            }
        }
        catch
        {
            // 配置损坏时静默使用默认值
        }

        var def = new AppSettings();
        def.Normalize();
        return def;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不影响主流程
        }
    }

    private void Normalize()
    {
        if (string.IsNullOrWhiteSpace(GameFolder))
        {
            GameFolder = Path.Combine(AppContext.BaseDirectory, ".minecraft");
        }
        if (MaxDownloadThreads < 1) MaxDownloadThreads = 1;
        if (MaxDownloadThreads > 64) MaxDownloadThreads = 64;
        if (DownloadSource is < 0 or > 2) DownloadSource = 0;
        if (string.IsNullOrWhiteSpace(JavaPath))
        {
            // 尝试从环境变量里猜一个
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(javaHome))
            {
                var guess = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(guess)) JavaPath = guess;
            }
        }
    }
}
