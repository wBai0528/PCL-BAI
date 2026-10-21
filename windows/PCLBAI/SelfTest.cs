#if SELFTEST
using System.IO;
using System.Text;
using PCLBAI.Models;
using PCLBAI.Services;

namespace PCLBAI;

/// <summary>
/// 无人值守自检：用 -p:SelfTest=true 编译后运行 PCLBAI.exe，结果写入控制台与 self-test.log。
/// 覆盖：版本清单获取、版本 Json 解析（支持库 / natives / 资源索引）、小文件实际下载、SHA1 校验、知识库匹配。
/// </summary>
public static class SelfTest
{
    private static readonly StringBuilder Log = new();

    [STAThread]
    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 无控制台时忽略 */ }

        // --snapshot <路径>：只做界面截图，不跑网络自检
        if (args.Length >= 2 && args[0] == "--snapshot")
        {
            UiSnapshot.Run(args[1]);
            return 0;
        }

        var exit = 0;
        try
        {
            RunAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Write("致命错误：" + ex);
            exit = 1;
        }

        Console.Write(Log.ToString());
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.log"), Log.ToString(), Encoding.UTF8); } catch { }
        return exit;
    }

    private static void Write(string s)
    {
        Log.AppendLine(s);
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        Write($"[{(ok ? "通过" : "失败")}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  → " + detail)}");
        if (!ok) Environment.ExitCode = 1;
    }

    private static async Task RunAsync()
    {
        var settings = new AppSettings { DownloadSource = 0, MaxDownloadThreads = 8 };
        var http = new HttpService();
        var installer = new GameInstaller(http, settings);

        // ---------- 1. 版本清单 ----------
        Write("=== 1. 版本清单 ===");
        List<VersionEntry> versions = null;
        foreach (var url in new[] { DownloadSource.BmclapiVersionList, DownloadSource.MojangVersionList })
        {
            try
            {
                using var doc = await http.GetJsonWithFallbackAsync(new[] { url }, CancellationToken.None);
                versions = VersionParser.ParseManifest(doc.RootElement);
                Write($"  来源 {url} → {versions.Count} 个版本");
                if (versions.Count > 0) break;
            }
            catch (Exception ex)
            {
                Write($"  来源 {url} 失败：{ex.Message}");
            }
        }
        Check("版本清单可获取", versions is { Count: > 100 }, versions == null ? "无数据" : $"{versions.Count} 条");
        if (versions == null) return;

        var latest = versions.FirstOrDefault(v => v.IsRelease);
        Check("能定位最新正式版", latest != null, latest?.Id);

        // ---------- 2. 版本 Json 解析 ----------
        Write("=== 2. 版本 Json 解析 ===");
        var target = latest;
        var jsonBytes = await http.GetBytesWithFallbackAsync(
            DownloadSource.LauncherOrMeta(target.Url, settings.DownloadSource), CancellationToken.None);
        Check("版本 Json 可下载", jsonBytes.Length > 1000, $"{jsonBytes.Length} 字节");

        var tmp = Path.Combine(Path.GetTempPath(), "PCLBAI-SelfTest");
        var prelim = VersionParser.Build(tmp, target.Id, jsonBytes, skipAssets: true, assetsIndexBytes: Array.Empty<byte>());
        Check("能解析出客户端 jar 信息", prelim.Client != null && !string.IsNullOrEmpty(prelim.Client.Sha1), prelim.Client?.Display);
        Check("能解析出支持库列表", prelim.Files.Count > 5, $"{prelim.Files.Count} 个支持库");
        Check("skipAssets 时确实跳过资源", prelim.AssetIndex == null);

        // 版本 Json 里的资源索引地址
        string indexUrl = null;
        using (var d = System.Text.Json.JsonDocument.Parse(jsonBytes))
        {
            if (d.RootElement.TryGetProperty("assetIndex", out var ai))
                indexUrl = VersionParser.GetString(ai, "url");
        }
        Check("能读到资源索引地址", !string.IsNullOrEmpty(indexUrl), indexUrl);

        // ---------- 3. 实际下载 + SHA1 校验 ----------
        Write("=== 3. 实际下载与 SHA1 校验 ===");
        var libDir = Path.Combine(tmp, "libraries");
        var list = VersionParser.Build(tmp, target.Id, jsonBytes, librariesRoot: libDir, skipAssets: true,
            assetsIndexBytes: Array.Empty<byte>());
        var sample = list.Files.FirstOrDefault(f => f.Size > 0 && f.Size < 300_000)
                     ?? list.Files.FirstOrDefault();
        if (sample != null)
        {
            var urls = DownloadSource.Library(sample.Url, settings.DownloadSource);
            Write($"  样本文件：{sample.DisplayName}（期望 {VerifyItem.SizeText(sample.Size)}，{sample.Sha1}）");
            Write($"  候选地址：{string.Join(" , ", urls)}");
            await http.DownloadFileAsync(urls, sample.LocalPath, sample.Size, CancellationToken.None);
            var result = GameInstaller.VerifyFile(sample, tmp);
            Check("下载后 SHA1 校验一致", result.IsOk, result.Detail);
        }
        else
        {
            Check("找到可下载的样本文件", false, "无合适样本");
        }

        // 故意破坏再校验，确认能检出
        if (sample != null && File.Exists(sample.LocalPath))
        {
            await File.AppendAllTextAsync(sample.LocalPath, "corrupted");
            var result2 = GameInstaller.VerifyFile(sample, tmp);
            Check("篡改后能检出异常", !result2.IsOk, result2.StateName);
        }

        // 资源索引下载 + 解析（先走正式清单构建，拿到 AssetIndex 条目）
        if (indexUrl != null)
        {
            var idxBytes = await http.GetBytesWithFallbackAsync(
                DownloadSource.LauncherOrMeta(indexUrl, settings.DownloadSource), CancellationToken.None);
            var assets = VersionParser.ParseAssets(tmp, idxBytes, "idx");
            Check("资源索引可解析", assets.Count > 0, $"{assets.Count} 个资源文件");

            var full = VersionParser.Build(tmp, target.Id, jsonBytes, librariesRoot: libDir,
                skipAssets: false, assetsIndexBytes: idxBytes);
            Check("完整清单包含客户端+支持库+资源", full.Count > assets.Count, $"共 {full.Count} 个文件");

            var a = assets.FirstOrDefault(x => x.Size > 0 && x.Size < 200_000);
            if (a != null)
            {
                await http.DownloadFileAsync(DownloadSource.Assets(a.Url, settings.DownloadSource), a.LocalPath, a.Size, CancellationToken.None);
                var r = GameInstaller.VerifyFile(a, tmp);
                Check("资源文件下载后 SHA1 一致", r.IsOk, r.Detail);
            }
        }

        // ---------- 4. 版本类型区分（正式版 / 快照） ----------
        Write("=== 4. 版本类型区分 ===");
        var releases = versions.Where(v => v.IsRelease).ToList();
        var snapshots = versions.Where(v => v.Type == "snapshot").ToList();
        var olds = versions.Where(v => v.Type is "old_beta" or "old_alpha").ToList();
        Write($"  正式版 {releases.Count} / 快照 {snapshots.Count} / 远古 {olds.Count}");
        Check("正式版数量合理", releases.Count > 50, $"{releases.Count} 个");
        Check("快照数量合理", snapshots.Count > 50, $"{snapshots.Count} 个");
        Check("两者不重叠", !releases.Select(r => r.Id).Intersect(snapshots.Select(s => s.Id)).Any());
        // 关键回归：24w14a 这类快照绝不能被当成正式版
        var snapSample = snapshots.FirstOrDefault(s => s.Id.Contains('w'));
        Check("快照样本被正确归类", snapSample != null && !snapSample.IsRelease, snapSample?.Id);
        var relSample = releases.First();
        Check("正式版样本被正确归类", relSample.IsRelease && relSample.TypeName == "正式版", $"{relSample.Id} → {relSample.TypeName}");

        // ---------- 5. 加载器 ----------
        Write("=== 5. 加载器版本查询 ===");
        var loaderSvc = new LoaderService(http);
        var forge = await loaderSvc.GetVersionsAsync(ModLoader.Forge, "1.20.1", CancellationToken.None);
        Write($"  Forge 1.20.1 → {string.Join(" | ", forge.Select(f => f.Display))}");
        Check("能查到 Forge 1.20.1 版本", forge.Count >= 1);

        var neoforge = await loaderSvc.GetVersionsAsync(ModLoader.NeoForge, "1.21.1", CancellationToken.None);
        Write($"  NeoForge 1.21.1 → {string.Join(" | ", neoforge.Select(f => f.Display))}");
        Check("能查到 NeoForge 1.21.1 版本", neoforge.Count >= 1, $"{neoforge.Count} 个");

        var fabric = await loaderSvc.GetVersionsAsync(ModLoader.Fabric, "1.20.1", CancellationToken.None);
        Write($"  Fabric loader → {string.Join(" | ", fabric.Select(f => f.Display))}");
        Check("能查到 Fabric Loader 版本", fabric.Count >= 1);

        // 快照不应提供加载器
        Check("快照不提供 Forge", !ModLoader.Forge.IsAvailable("1.21", false));
        Check("正式版提供 Forge", ModLoader.Forge.IsAvailable("1.21", true));
        Check("NeoForge 不支持 1.20.1", !ModLoader.NeoForge.IsAvailable("1.20.1", true));

        // ---------- 6. Bug 知识库 ----------
        Write("=== 6. Bug 知识库 ===");
        Write($"  共 {BugDatabase.TotalCount} 条");
        foreach (var (v, t, l) in new (string, string, ModLoader)[]
        {
            ("1.21.4", "release", ModLoader.Vanilla),
            ("1.21.4", "release", ModLoader.Forge),
            ("1.20.1", "release", ModLoader.Forge),
            ("1.20.5", "release", ModLoader.Vanilla),
            ("1.19.2", "release", ModLoader.Vanilla),
            ("1.8.9", "release", ModLoader.Vanilla),
            ("24w14a", "snapshot", ModLoader.Vanilla),
            ("24w14a", "snapshot", ModLoader.Forge),
            ("b1.7.3", "old_beta", ModLoader.Vanilla),
        })
        {
            var items = BugDatabase.Query(v, t, l);
            Write($"  {v} [{t}/{l.ShortName()}] → {items.Count} 条：{string.Join("；", items.Select(i => i.Title))}");
        }

        Check("知识库条目充足", BugDatabase.TotalCount >= 30, $"{BugDatabase.TotalCount} 条");
        Check("1.21.4 正式版有若干问题", BugDatabase.Query("1.21.4", "release", ModLoader.Vanilla).Count >= 3);
        Check("1.21.4 + Forge 能拿到 Forge 专属条目",
            BugDatabase.Query("1.21.4", "release", ModLoader.Forge).Any(i => i.Loaders.Contains(ModLoader.Forge)));
        Check("1.20.5 命中数据组件条目",
            BugDatabase.Query("1.20.5", "release", ModLoader.Vanilla).Any(i => i.Title.Contains("数据组件")));

        var snapBugs = BugDatabase.Query("24w14a", "snapshot", ModLoader.Vanilla);
        Check("快照只命中快照条目", snapBugs.Count > 0 && snapBugs.All(i => i.TypeFilter == "snapshot"),
            $"{snapBugs.Count} 条");
        Check("快照不再混入正式版条目",
            !snapBugs.Any(i => i.Title.Contains("Java 21，用 Java 17")));

        var oldBugs = BugDatabase.Query("b1.7.3", "old_beta", ModLoader.Vanilla);
        Check("远古版本命中远古条目", oldBugs.Count > 0 && oldBugs.All(i => i.TypeFilter == "old_beta"),
            $"{oldBugs.Count} 条");

        Write("=== 自检结束 ===");
    }
}
#endif
