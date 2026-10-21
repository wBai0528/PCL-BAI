using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using PCLBAI.Models;

namespace PCLBAI.Services;

/// <summary>模组加载器。与手机版保持同一套定义。</summary>
public enum ModLoader
{
    Vanilla,
    Forge,
    NeoForge,
    Fabric,
    Quilt,
    OptiFine,
    LiteLoader
}

public static class ModLoaderInfo
{
    public static string ShortName(this ModLoader l) => l switch
    {
        ModLoader.Vanilla => "原版",
        ModLoader.Forge => "Forge",
        ModLoader.NeoForge => "NeoForge",
        ModLoader.Fabric => "Fabric",
        ModLoader.Quilt => "Quilt",
        ModLoader.OptiFine => "OptiFine",
        ModLoader.LiteLoader => "LiteLoader",
        _ => l.ToString()
    };

    public static bool IsModded(this ModLoader l) => l != ModLoader.Vanilla;

    /// <summary>该正式版理论上是否提供此加载器。快照一律不支持。</summary>
    public static bool IsAvailable(this ModLoader l, string mcVersion, bool isRelease)
    {
        if (!isRelease) return false;
        if (!double.TryParse(mcVersion.Replace("1.", ""), out var v)) return false;
        return l switch
        {
            ModLoader.Forge => v >= 1.0,
            ModLoader.NeoForge => v >= 20.2,
            ModLoader.Fabric => v >= 14.0,
            ModLoader.Quilt => v >= 14.0,
            ModLoader.OptiFine => v >= 6.0,
            ModLoader.LiteLoader => v <= 12.2,
            _ => true
        };
    }
}

/// <summary>一个加载器版本。</summary>
public sealed class LoaderVersion
{
    public ModLoader Loader { get; init; }
    public string Version { get; init; }
    public string McVersion { get; init; }
    public bool IsRecommended { get; init; }
    public string Note { get; init; }

    public string Display => IsRecommended ? $"{Version}  ({Note ?? "推荐"})" : $"{Version}  ({Note ?? "最新"})";
    public override string ToString() => Display;
}

/// <summary>加载器版本查询。接口与手机版一致。</summary>
public sealed class LoaderService
{
    private readonly HttpService _http;

    public LoaderService(HttpService http) => _http = http;

    /// <summary>查询某加载器的可用版本。</summary>
    public async Task<List<LoaderVersion>> GetVersionsAsync(ModLoader loader, string mcVersion, CancellationToken token)
    {
        return loader switch
        {
            ModLoader.Forge => await ForgeAsync(mcVersion, token).ConfigureAwait(false),
            ModLoader.NeoForge => await NeoForgeAsync(mcVersion, token).ConfigureAwait(false),
            ModLoader.Fabric => await FabricAsync(token).ConfigureAwait(false),
            _ => new List<LoaderVersion>()
        };
    }

    private async Task<List<LoaderVersion>> ForgeAsync(string mcVersion, CancellationToken token)
    {
        var urls = new[]
        {
            "https://bmclapi2.bangbang93.com/forge/promotions",
            "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json"
        };
        var json = await _http.GetStringWithFallbackAsync(urls, token).ConfigureAwait(false);
        var list = new List<LoaderVersion>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("promos", out var promos)) return list;

        var rec = promos.TryGetProperty(mcVersion + "-recommended", out var r) ? r.GetString() : null;
        var latest = promos.TryGetProperty(mcVersion + "-latest", out var l) ? l.GetString() : null;

        if (!string.IsNullOrEmpty(rec))
            list.Add(new LoaderVersion { Loader = ModLoader.Forge, Version = rec, McVersion = mcVersion, IsRecommended = true, Note = "推荐" });
        if (!string.IsNullOrEmpty(latest) && latest != rec)
            list.Add(new LoaderVersion { Loader = ModLoader.Forge, Version = latest, McVersion = mcVersion, IsRecommended = false, Note = "最新" });
        return list;
    }

    private async Task<List<LoaderVersion>> NeoForgeAsync(string mcVersion, CancellationToken token)
    {
        var urls = new[]
        {
            "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/maven-metadata.xml",
            "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml"
        };
        var xml = await _http.GetStringWithFallbackAsync(urls, token).ConfigureAwait(false);
        var prefix = mcVersion.StartsWith("1.") ? mcVersion[2..] + "." : mcVersion + ".";

        var list = new List<LoaderVersion>();
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var v in doc.Descendants("version"))
            {
                var ver = v.Value.Trim();
                if (!ver.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var beta = ver.Contains("beta", StringComparison.OrdinalIgnoreCase);
                list.Add(new LoaderVersion
                {
                    Loader = ModLoader.NeoForge,
                    Version = ver,
                    McVersion = mcVersion,
                    IsRecommended = !beta,
                    Note = beta ? "Beta" : "稳定"
                });
            }
        }
        catch
        {
            // XML 解析失败返回空，界面会提示
        }

        return list
            .OrderByDescending(x => x.IsRecommended)
            .ThenByDescending(x => x.Version)
            .Take(6)
            .ToList();
    }

    private async Task<List<LoaderVersion>> FabricAsync(CancellationToken token)
    {
        var urls = new[] { "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader" };
        var json = await _http.GetStringWithFallbackAsync(urls, token).ConfigureAwait(false);
        var list = new List<LoaderVersion>();
        using var doc = JsonDocument.Parse(json);

        // 该接口直接返回数组（不是 { "loader": [...] }），两种形态都要兼容
        JsonElement arr;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            arr = doc.RootElement;
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                 doc.RootElement.TryGetProperty("loader", out var inner) &&
                 inner.ValueKind == JsonValueKind.Array)
        {
            arr = inner;
        }
        else
        {
            return list;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var ver = item.TryGetProperty("version", out var vp) ? vp.GetString() : null;
            if (string.IsNullOrEmpty(ver)) continue;
            var stable = item.TryGetProperty("stable", out var sp) && sp.ValueKind == JsonValueKind.True;
            list.Add(new LoaderVersion
            {
                Loader = ModLoader.Fabric,
                Version = ver,
                IsRecommended = stable,
                Note = stable ? "稳定" : "测试"
            });
        }

        return list.Where(x => x.IsRecommended).Take(3)
            .Concat(list.Where(x => !x.IsRecommended).Take(2))
            .ToList();
    }
}

/// <summary>
/// Forge / NeoForge 安装器。
/// 走 BMCLAPI 镜像下载 installer jar，再以 --installClient 方式无人值守安装到指定游戏目录。
/// </summary>
public sealed class LoaderInstaller
{
    private readonly HttpService _http;

    public LoaderInstaller(HttpService http) => _http = http;

    /// <summary>构造 installer jar 的候选下载地址。</summary>
    public static List<string> InstallerUrls(ModLoader loader, string mcVersion, string loaderVersion)
    {
        return loader switch
        {
            ModLoader.Forge => new List<string>
            {
                $"https://bmclapi2.bangbang93.com/forge/download/{loaderVersion}/installer?mcversion={mcVersion}",
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{loaderVersion}/forge-{mcVersion}-{loaderVersion}-installer.jar",
                $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{mcVersion}-{loaderVersion}/forge-{mcVersion}-{loaderVersion}-installer.jar"
            },
            ModLoader.NeoForge => new List<string>
            {
                $"https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar",
                $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar"
            },
            _ => new List<string>()
        };
    }

    /// <summary>
    /// 下载并执行安装器。返回安装日志文本。
    /// </summary>
    public async Task<string> InstallAsync(
        ModLoader loader,
        string mcVersion,
        string loaderVersion,
        string gameFolder,
        string javaPath,
        IProgress<string> log,
        CancellationToken token)
    {
        if (loader != ModLoader.Forge && loader != ModLoader.NeoForge)
            throw new NotSupportedException($"{loader.ShortName()} 不支持在线安装");

        Directory.CreateDirectory(gameFolder);
        var installerDir = Path.Combine(gameFolder, "installers");
        Directory.CreateDirectory(installerDir);

        var fileName = loader == ModLoader.Forge
            ? $"forge-{mcVersion}-{loaderVersion}-installer.jar"
            : $"neoforge-{loaderVersion}-installer.jar";
        var installerPath = Path.Combine(installerDir, fileName);

        log?.Report($"正在下载安装器 {fileName} ……");
        await _http.DownloadFileAsync(InstallerUrls(loader, mcVersion, loaderVersion), installerPath, -1, token)
            .ConfigureAwait(false);

        if (!File.Exists(javaPath))
            throw new FileNotFoundException("找不到 Java，请在设置里指定 java.exe 路径", javaPath);

        log?.Report("正在执行加载器安装（首次会下载大量依赖，请耐心等待）……");
        var args = $"-jar \"{installerPath}\" --installClient \"{gameFolder}\"";
        return await RunJavaAsync(javaPath, args, log, token).ConfigureAwait(false);
    }

    /// <summary>运行 java 并实时回传输出。</summary>
    public static async Task<string> RunJavaAsync(string javaPath, string arguments, IProgress<string> log, CancellationToken token)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        var sb = new System.Text.StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (sb) sb.AppendLine(e.Data);
            log?.Report(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (sb) sb.AppendLine(e.Data);
            log?.Report(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await Task.Run(() =>
        {
            while (!proc.WaitForExit(500))
            {
                token.ThrowIfCancellationRequested();
            }
        }, token).ConfigureAwait(false);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"安装器退出码 {proc.ExitCode}，详见日志");

        return sb.ToString();
    }
}
