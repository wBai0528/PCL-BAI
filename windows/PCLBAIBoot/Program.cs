using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace PCLBAIBoot;

/// <summary>
/// PCL-BAI 引导器。
///
/// 结构：
///   - 主程序（约 120 KB）以资源形式内嵌在本 exe 里，首次运行直接释放；
///   - .NET 运行时（约 32 MB）首次运行时联网下载并解压到本地缓存；
///   - 之后每次双击直接启动。
///
/// 引导器自身是自包含 + 裁剪的，不依赖本机 .NET。
/// </summary>
internal static class Program
{
    private const string AppName = "PCL-BAI";
    private const string MainExeName = "PCLBAI.exe";
    private const string RuntimeMark = ".runtime-ok";

    /// <summary>
    /// 与内嵌主程序匹配的 .NET 运行时版本。
    /// 主程序是 WPF 应用，依赖 Microsoft.WindowsDesktop.App，
    /// 必须下 Desktop Runtime（不是基础版 NETCore.App），否则主窗口起不来。
    /// </summary>
    private const string RuntimeVersion = "8.0.32";

    private static readonly string[] RuntimeUrls =
    {
        $"https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/{RuntimeVersion}/windowsdesktop-runtime-{RuntimeVersion}-win-x64.zip",
        $"https://dotnetcli.azureedge.net/dotnet/WindowsDesktop/{RuntimeVersion}/windowsdesktop-runtime-{RuntimeVersion}-win-x64.zip"
    };

    private static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCL-BAI");

    private static string MainExe => Path.Combine(Root, MainExeName);
    private static string RuntimeDir => Path.Combine(Root, "runtime");
    private static string MarkPath => Path.Combine(Root, RuntimeMark);
    private static string ZipPath => Path.Combine(Root, "dotnet-runtime.zip");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x0;
    private const uint MB_ICONERROR = 0x10;
    private const uint MB_YESNO = 0x4;
    private const uint MB_ICONQUESTION = 0x20;
    private const int IDYES = 6;

    private static void Error(string msg, string title = AppName)
        => MessageBoxW(IntPtr.Zero, msg, title, MB_OK | MB_ICONERROR);

    private static int Main(string[] args)
    {
        var quiet = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

        try
        {
            Directory.CreateDirectory(Root);

            var needApp = !File.Exists(MainExe);
            var needRuntime = !RuntimeReady();

            // 先释放内嵌主程序（体积极小，不依赖网络）
            if (needApp || needRuntime) ExtractEmbeddedApp();

            // 只有缺运行时时才联网下载
            if (needRuntime && !Prepare(quiet)) return 0; // 用户取消

            return Launch();
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Error("启动失败：" + ex.Message + "\n\n安装目录：" + Root);
            return 1;
        }
    }

    private static bool RuntimeReady()
    {
        if (!File.Exists(MarkPath)) return false;

        // WPF 主程序需要 WindowsDesktop.App，两个都要在才算就绪
        var core = Path.Combine(RuntimeDir, "shared", "Microsoft.NETCore.App");
        var desktop = Path.Combine(RuntimeDir, "shared", "Microsoft.WindowsDesktop.App");
        return Directory.Exists(core) && Directory.Exists(desktop);
    }

    /// <summary>首次运行：下载运行时并释放主程序。</summary>
    private static bool Prepare(bool quiet)
    {
        if (!quiet)
        {
            var answer = MessageBoxW(IntPtr.Zero,
                $"首次运行需要下载 .NET 运行时（约 32 MB）。\n\n安装位置：\n{Root}\n\n是否继续？",
                AppName, MB_YESNO | MB_ICONQUESTION);
            if (answer != IDYES) return false;
        }

        var ui = new Thread(() => ProgressWindow.Show(AppName, "准备下载……")) { IsBackground = true };
        if (OperatingSystem.IsWindows()) ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        Thread.Sleep(250);

        string usedUrl = null;
        Exception last = null;

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PCLBAI-Boot/1.1");

            foreach (var url in RuntimeUrls)
            {
                try
                {
                    Download(http, url, ZipPath);
                    usedUrl = url;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }
        }

        if (usedUrl == null)
        {
            ProgressWindow.Close();
            Error("下载 .NET 运行时失败，请检查网络后重试。\n\n" + (last?.Message ?? ""));
            return false;
        }

        ProgressWindow.SetProgress(90, "正在解压运行时……");
        Directory.CreateDirectory(RuntimeDir);
        ExtractZip(ZipPath, RuntimeDir);

        ProgressWindow.SetProgress(97, "正在释放主程序……");
        try { File.Delete(ZipPath); } catch { }
        File.WriteAllText(MarkPath, usedUrl);

        ProgressWindow.SetProgress(100, "准备完成，正在启动……");
        Thread.Sleep(350);
        ProgressWindow.Close();
        return true;
    }

    /// <summary>把内嵌的主程序包释放到安装目录。</summary>
    private static void ExtractEmbeddedApp()
    {
        var asm = typeof(Program).Assembly;
        const string resName = "app.zip";

        using var stream = asm.GetManifestResourceStream(resName);
        if (stream == null) throw new InvalidOperationException("内置主程序资源缺失：" + resName);

        var temp = Path.Combine(Root, "app.tmp.zip");
        using (var fs = File.Create(temp)) stream.CopyTo(fs);

        ExtractZip(temp, Root);
        try { File.Delete(temp); } catch { }
    }

    private static void Download(HttpClient http, string url, string dest)
    {
        ProgressWindow.SetProgress(0, "正在连接下载服务器……");

        using var resp = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1;
        using var src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long done = 0;
        var lastReport = DateTime.UtcNow;

        while (true)
        {
            var read = src.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            dst.Write(buffer, 0, read);
            done += read;

            if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 120)
            {
                lastReport = DateTime.UtcNow;
                var pct = total > 0 ? (int)(done * 85 / total) : 0;
                var mb = done / 1024.0 / 1024;
                var tip = total > 0
                    ? $"正在下载 {mb:0.0} / {total / 1024.0 / 1024:0.0} MB"
                    : $"正在下载 {mb:0.0} MB";
                ProgressWindow.SetProgress(pct, tip);
            }
        }

        dst.Flush();
    }

    private static void ExtractZip(string zipPath, string outDir)
    {
        var rootFull = Path.GetFullPath(outDir);
        if (!rootFull.EndsWith(Path.DirectorySeparatorChar)) rootFull += Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var target = Path.GetFullPath(Path.Combine(outDir, entry.FullName));
            // 防目录穿越
            if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static int Launch()
    {
        if (!File.Exists(MainExe))
        {
            Error("找不到 " + MainExeName + "，请删除该目录后重新运行：\n" + Root);
            return 2;
        }

        var psi = new ProcessStartInfo
        {
            FileName = MainExe,
            WorkingDirectory = Root,
            UseShellExecute = false
        };
        // 让主程序使用引导器下载的那套运行时。
        // DOTNET_ROOT 必须指向包含 shared\ 的目录，即 RuntimeDir 本身。
        psi.Environment["DOTNET_ROOT"] = RuntimeDir;
        psi.Environment["DOTNET_ROOT_X64"] = RuntimeDir;

        Process.Start(psi);
        return 0;
    }
}
