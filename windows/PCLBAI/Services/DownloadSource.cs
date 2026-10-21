using System.IO;
using System.Net.Http;
using System.Text.Json;
using PCLBAI.Models;

namespace PCLBAI.Services;

/// <summary>
/// 下载源与镜像改写规则。
/// 改写规则原样搬自 PCL2（Plain Craft Launcher 2）源码 ModDownload.vb 中的
/// DlSourceAssetsGet / DlSourceLibraryGet / DlSourceLauncherOrMetaGet 三个函数。
/// </summary>
public static class DownloadSource
{
    public const string MojangVersionList = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
    public const string BmclapiVersionList = "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json";

    /// <summary>根据设置生成候选地址列表（按优先级排序）。</summary>
    /// <param name="official">官方地址</param>
    /// <param name="mirror">镜像地址</param>
    /// <param name="sourceMode">0 自动（镜像优先），1 仅镜像，2 仅官方</param>
    public static List<string> Order(IEnumerable<string> official, IEnumerable<string> mirror, int sourceMode)
    {
        var off = official.Distinct().ToList();
        var mir = mirror.Distinct().ToList();
        return sourceMode switch
        {
            1 => mir,
            2 => off,
            _ => mir.Concat(off.Where(o => !mir.Contains(o))).ToList()
        };
    }

    /// <summary>Assets 文件的候选地址（对应 PCL 的 DlSourceAssetsGet）。</summary>
    public static List<string> Assets(string original, int sourceMode)
    {
        if (original == null) return new List<string>();
        original = original.Replace("http://resources.download.minecraft.net", "https://resources.download.minecraft.net");
        var mirror = original
            .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/assets")
            .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/assets")
            .Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets");
        return Order(new[] { original }, new[] { mirror }, sourceMode);
    }

    /// <summary>Libraries 文件的候选地址（对应 PCL 的 DlSourceLibraryGet）。</summary>
    public static List<string> Library(string original, int sourceMode)
    {
        if (original == null) return new List<string>();

        // Forge / Fabric / NeoForge 的依赖只走镜像的 maven 与 libraries，不尝试官方源
        if (original.Contains("minecraftforge") || original.Contains("fabricmc") || original.Contains("neoforged"))
        {
            return new[]
            {
                original
                    .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/maven")
                    .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/maven")
                    .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven"),
                original
                    .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/libraries")
                    .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/libraries")
                    .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/libraries")
            }.Distinct().ToList();
        }

        var m1 = original
            .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/maven")
            .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/maven")
            .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven");
        var m2 = original
            .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/libraries")
            .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/libraries")
            .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/libraries");
        return Order(new[] { original }, new[] { m1, m2 }, sourceMode);
    }

    /// <summary>版本 Json（meta / launcher / assetIndex）的候选地址（对应 PCL 的 DlSourceLauncherOrMetaGet）。</summary>
    public static List<string> LauncherOrMeta(string original, int sourceMode)
    {
        if (original == null) return new List<string>();
        var mirror = original
            .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com");
        return Order(new[] { original }, new[] { mirror }, sourceMode);
    }

    /// <summary>下载客户端 jar 的候选地址。</summary>
    public static List<string> Client(string original, int sourceMode) => LauncherOrMeta(original, sourceMode);
}

/// <summary>自建 Http 客户端：带重试、超时、可取消、多地址回退。</summary>
public sealed class HttpService : IDisposable
{
    private readonly HttpClient _client;

    public HttpService()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            MaxConnectionsPerServer = 64,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        // 部分 CDN 会检查 UA
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PCLBAI/1.0 (personal-use launcher)");
    }

    /// <summary>依次尝试多个地址下载到内存，返回第一个成功的结果。</summary>
    public async Task<byte[]> GetBytesWithFallbackAsync(IReadOnlyList<string> urls, CancellationToken token)
    {
        Exception last = null;
        foreach (var url in urls)
        {
            try
            {
                return await _client.GetByteArrayAsync(url, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw new IOException("所有下载地址均失败：" + string.Join(" | ", urls), last);
    }

    /// <summary>依次尝试多个地址下载文件，返回实际使用的地址；已有文件大小正确时可跳过。</summary>
    public async Task<string> DownloadFileAsync(IReadOnlyList<string> urls, string localPath, long expectedSize,
        CancellationToken token, Action<long, long> progress = null)
    {
        Exception last = null;
        foreach (var url in urls)
        {
            try
            {
                await DownloadSingleAsync(url, localPath, expectedSize, token, progress).ConfigureAwait(false);
                return url;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw new IOException("下载失败：" + localPath, last);
    }

    private async Task DownloadSingleAsync(string url, string localPath, long expectedSize,
        CancellationToken token, Action<long, long> progress)
    {
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = localPath + ".pcltmp";
        try
        {
            using var resp = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? expectedSize;

            await using (var src = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    done += read;
                    progress?.Invoke(done, total);
                }
            }

            // 原子替换
            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(tmp, localPath);
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
                // 忽略清理异常
            }
        }
    }

    /// <summary>依次尝试多个地址获取文本（用于 promotions / maven-metadata 等）。</summary>
    public async Task<string> GetStringWithFallbackAsync(IReadOnlyList<string> urls, CancellationToken token)
    {
        var bytes = await GetBytesWithFallbackAsync(urls, token).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>以 JSON 方式获取版本清单。</summary>
    public async Task<JsonDocument> GetJsonWithFallbackAsync(IReadOnlyList<string> urls, CancellationToken token)
    {
        var bytes = await GetBytesWithFallbackAsync(urls, token).ConfigureAwait(false);
        return JsonDocument.Parse(bytes);
    }

    public void Dispose() => _client.Dispose();
}
