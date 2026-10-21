#if SELFTEST
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PCLBAI;

/// <summary>
/// 界面截图工具：用 WPF 自身的 RenderTargetBitmap 渲染主窗口。
/// 用于无桌面会话下验证界面（PrintWindow 抓不到 WPF 的 GPU 合成内容）。
/// </summary>
public static class UiSnapshot
{
    public static void Run(string outPath)
    {
        var app = new App();
        app.InitializeComponent();

        var win = new MainWindow
        {
            Width = 1280,
            Height = 800,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false
        };
        win.Show();

        // 等版本列表加载完（网络请求 + 数据绑定）
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var ticks = 0;
        timer.Tick += (_, _) =>
        {
            ticks++;
            if (ticks < 12) return;

            // 截图时优先选中一个有 Bug 条目的常见版本，便于验证面板
            try
            {
                var want = Environment.GetEnvironmentVariable("PCLBAI_SNAPSHOT_VERSION");
                if (!string.IsNullOrEmpty(want))
                {
                    var lst = win.FindName("LstVersions") as System.Windows.Controls.ListBox;
                    if (lst != null)
                    {
                        foreach (var item in lst.Items)
                        {
                            if (item is Models.VersionEntry ve && ve.Id == want) { lst.SelectedItem = item; break; }
                        }
                    }
                }
            }
            catch { /* 截图失败不影响结果 */ }

            if (ticks < 16) return;

            timer.Stop();
            try
            {
                Save(win, outPath);
                if (File.Exists(outPath)) Console.WriteLine("截图已保存：" + outPath);
                else Console.WriteLine("截图失败：文件未生成");
            }
            catch (Exception ex)
            {
                Console.WriteLine("截图异常：" + ex);
            }
            finally
            {
                app.Shutdown();
            }
        };
        timer.Start();

        app.Run();
    }

    private static void Save(Window win, string path)
    {
        var root = (FrameworkElement)win.Content;
        root.UpdateLayout();
        win.UpdateLayout();

        var w = (int)Math.Ceiling(root.ActualWidth);
        var h = (int)Math.Ceiling(root.ActualHeight);
        if (w <= 0 || h <= 0) throw new InvalidOperationException($"窗口尺寸无效：{w}x{h}");

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
#endif
