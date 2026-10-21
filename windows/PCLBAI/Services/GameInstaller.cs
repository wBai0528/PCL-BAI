using System.IO;
using System.Security.Cryptography;
using PCLBAI.Models;

namespace PCLBAI.Services;

/// <summary>下载与校验引擎。</summary>
public sealed class GameInstaller
{
    private readonly HttpService _http;
    private readonly AppSettings _settings;

    public GameInstaller(HttpService http, AppSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    // ================= 校验 =================

    /// <summary>计算文件 SHA1（小写）。</summary>
    public static string ComputeSha1(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var sha = SHA1.Create();
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 校验一个文件：先比大小，再比 SHA1。返回单个结果。
    /// </summary>
    public static VerifyItem VerifyFile(FileEntry entry, string gameFolder)
    {
        var rel = GetRelative(gameFolder, entry.LocalPath);
        try
        {
            var info = new FileInfo(entry.LocalPath);
            if (!info.Exists)
            {
                return new VerifyItem { Entry = entry, State = "Missing", RelativePath = rel };
            }

            if (entry.Size >= 0 && info.Length != entry.Size)
            {
                return new VerifyItem
                {
                    Entry = entry,
                    State = "SizeMismatch",
                    ActualSize = info.Length,
                    RelativePath = rel
                };
            }

            if (string.IsNullOrEmpty(entry.Sha1))
            {
                return new VerifyItem { Entry = entry, State = "OK", ActualSize = info.Length, ActualSha1 = "(无校验值)", RelativePath = rel };
            }

            var actual = ComputeSha1(entry.LocalPath);
            var ok = string.Equals(actual, entry.Sha1, StringComparison.OrdinalIgnoreCase);
            return new VerifyItem
            {
                Entry = entry,
                State = ok ? "OK" : "HashMismatch",
                ActualSize = info.Length,
                ActualSha1 = actual,
                RelativePath = rel
            };
        }
        catch (Exception ex)
        {
            return new VerifyItem { Entry = entry, State = "Error", RelativePath = rel, Message = ex.Message };
        }
    }

    /// <summary>获取相对路径用于界面展示。</summary>
    public static string GetRelative(string gameFolder, string fullPath)
    {
        try
        {
            var rel = Path.GetRelativePath(gameFolder, fullPath);
            return rel.Replace(Path.DirectorySeparatorChar, '/');
        }
        catch
        {
            return fullPath;
        }
    }

    /// <summary>并发校验整个清单。</summary>
    public async Task<VerifyReport> VerifyAsync(
        VersionFileList list,
        string gameFolder,
        IProgress<(int done, int total, VerifyItem current)> progress,
        CancellationToken token)
    {
        var report = new VerifyReport { VersionName = list.VersionName };
        var files = list.AllFiles.ToList();
        var total = files.Count;
        var done = 0;
        var results = new VerifyItem[total];
        var gate = new object();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(2, Math.Min(Environment.ProcessorCount * 2, 16)),
            CancellationToken = token
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, total), options, async (i, ct) =>
        {
            await Task.Yield();
            var item = VerifyFile(files[i], gameFolder);
            results[i] = item;
            int d;
            lock (gate) { d = ++done; }
            progress?.Report((d, total, item));
        }).ConfigureAwait(false);

        foreach (var r in results) if (r != null) report.Items.Add(r);
        return report;
    }

    /// <summary>后台写文件，避免占用 UI 线程。</summary>
    private static Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken token)
        => Task.Run(() => File.WriteAllBytes(path, bytes), token);

    // ================= 下载 =================

    /// <summary>
    /// 下载版本：先取版本清单拿到 json 地址 → 下载版本 json → 下载索引 → 并发下载全部缺失文件。
    /// </summary>
    public async Task DownloadVersionAsync(
        string gameFolder,
        string versionName,
        IProgress<(string stage, int done, int total, string file)> progress,
        IProgress<string> log,
        CancellationToken token)
    {
        Directory.CreateDirectory(gameFolder);

        // 1. 版本清单
        log?.Report("正在获取版本清单……");
        var manifestUrls = DownloadSource.Order(
            new[] { DownloadSource.MojangVersionList },
            new[] { DownloadSource.BmclapiVersionList },
            _settings.DownloadSource); // 自动模式下镜像优先，失败回落官方

        VersionEntry entry;
        using (var doc = await _http.GetJsonWithFallbackAsync(manifestUrls, token).ConfigureAwait(false))
        {
            var versions = VersionParser.ParseManifest(doc.RootElement);
            entry = versions.FirstOrDefault(v => v.Id == versionName);
        }

        if (entry == null || string.IsNullOrEmpty(entry.Url))
            throw new InvalidOperationException($"版本清单中找不到版本 {versionName}");

        // 2. 版本 json
        var versionDir = Path.Combine(gameFolder, "versions", versionName);
        Directory.CreateDirectory(versionDir);
        var jsonPath = Path.Combine(versionDir, versionName + ".json");

        log?.Report($"正在下载 {versionName}.json……");
        var jsonBytes = await _http.GetBytesWithFallbackAsync(
            DownloadSource.LauncherOrMeta(entry.Url, _settings.DownloadSource), token).ConfigureAwait(false);
        await WriteAllBytesAsync(jsonPath, jsonBytes, token).ConfigureAwait(false);

        // 3. 先用无资源索引的方式构建清单，拿到索引地址
        var prelim = VersionParser.Build(gameFolder, versionName, jsonBytes, skipAssets: false, assetsIndexBytes: Array.Empty<byte>());
        byte[] indexBytes = null;
        if (prelim.AssetIndex != null)
        {
            log?.Report("正在下载资源索引……");
            indexBytes = await _http.GetBytesWithFallbackAsync(
                DownloadSource.LauncherOrMeta(prelim.AssetIndex.Url, _settings.DownloadSource), token).ConfigureAwait(false);
            var indexPath = prelim.AssetIndex.LocalPath;
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            await WriteAllBytesAsync(indexPath, indexBytes, token).ConfigureAwait(false);
        }

        // 4. 正式清单
        var list = VersionParser.Build(gameFolder, versionName, jsonBytes,
            skipAssets: _settings.SkipAssetsVerify, assetsIndexBytes: indexBytes);

        // 5. 挑出需要下载的
        var need = new List<FileEntry>();
        if (list.Client != null) need.Add(list.Client);
        need.AddRange(list.Files);

        var todo = new List<FileEntry>();
        foreach (var f in need)
        {
            var info = new FileInfo(f.LocalPath);
            if (info.Exists && (f.Size < 0 ? info.Length > 0 : info.Length == f.Size)) continue;
            todo.Add(f);
        }

        log?.Report($"共需要 {need.Count} 个文件，其中 {todo.Count} 个需要下载。");
        progress?.Report(("下载", 0, todo.Count, "准备中……"));

        if (todo.Count == 0) return;

        // 6. 并发下载
        var done = 0;
        var failed = new List<string>();
        var gate = new object();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(_settings.MaxDownloadThreads, 1, 64),
            CancellationToken = token
        };

        await Parallel.ForEachAsync(todo, options, async (f, ct) =>
        {
            try
            {
                var urls = f.Group switch
                {
                    "Assets" => DownloadSource.Assets(f.Url, _settings.DownloadSource),
                    "Client" => DownloadSource.Client(f.Url, _settings.DownloadSource),
                    _ => DownloadSource.Library(f.Url, _settings.DownloadSource)
                };
                await _http.DownloadFileAsync(urls, f.LocalPath, f.Size, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lock (gate) failed.Add($"{f.DisplayName}：{ex.Message}");
            }
            finally
            {
                int d;
                lock (gate) d = ++done;
                progress?.Report(("下载", d, todo.Count, f.DisplayName));
            }
        }).ConfigureAwait(false);

        if (failed.Count > 0)
        {
            var msg = string.Join(Environment.NewLine, failed.Take(20));
            if (failed.Count > 20) msg += $"{Environment.NewLine}……另有 {failed.Count - 20} 个文件失败";
            throw new IOException($"有 {failed.Count} 个文件下载失败：{Environment.NewLine}{msg}");
        }
    }
}
