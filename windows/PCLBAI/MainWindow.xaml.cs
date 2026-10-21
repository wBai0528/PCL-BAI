using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WpfMessageBox = System.Windows.MessageBox;
using PCLBAI.Models;
using PCLBAI.Services;

namespace PCLBAI;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private void Msg(string text, string title = "提示")
        => WpfMessageBox.Show(this, text, title);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HttpService _http = new();
    private readonly GameInstaller _installer;
    private readonly LoaderService _loaderService;
    private readonly LoaderInstaller _loaderInstaller;
    private CancellationTokenSource _cts;
    private bool _busy;

    private readonly ObservableCollection<VersionEntry> _versions = new();
    private readonly ObservableCollection<VerifyItem> _verifyItems = new();
    private readonly ObservableCollection<BugEntry> _bugs = new();
    private readonly ObservableCollection<LoaderVersion> _loaderVersions = new();
    private ListCollectionView _versionView;
    private bool _suppressVersionSelect;

    // ---- 属性绑定 ----
    private string _statusText = "就绪";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    private string _stageText = "";
    public string StageText { get => _stageText; set { _stageText = value; OnPropertyChanged(); } }

    private double _progressValue;
    public double ProgressValue { get => _progressValue; set { _progressValue = value; OnPropertyChanged(); } }

    private string _summary = "";
    public string Summary { get => _summary; set { _summary = value; OnPropertyChanged(); } }

    private string _summaryBrush = "#555555";
    public string SummaryBrush { get => _summaryBrush; set { _summaryBrush = value; OnPropertyChanged(); } }

    private string _gameFolder = "";
    public string GameFolder { get => _gameFolder; set { _gameFolder = value; OnPropertyChanged(); } }

    private string _versionJsonInfo = "";
    public string VersionJsonInfo { get => _versionJsonInfo; set { _versionJsonInfo = value; OnPropertyChanged(); } }

    private string _bugHint = "在左侧选择一个版本，这里会显示该版本的已知问题。";
    public string BugHint { get => _bugHint; set { _bugHint = value; OnPropertyChanged(); } }

    private string _loaderHint = "选择版本后可查看可用模组加载器版本。";
    public string LoaderHint { get => _loaderHint; set { _loaderHint = value; OnPropertyChanged(); } }

    private string _versionCountText = "";
    public string VersionCountText { get => _versionCountText; set { _versionCountText = value; OnPropertyChanged(); } }

    private bool _busyFlag
    {
        get => _busy;
        set
        {
            _busy = value;
            OnPropertyChanged(nameof(IsIdle));
            foreach (var b in AllButtons()) if (b != null) b.IsEnabled = !value;
            if (BtnCancel != null) BtnCancel.IsEnabled = value;
        }
    }

    public bool IsIdle => !_busy;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _installer = new GameInstaller(_http, _settings);
        _loaderService = new LoaderService(_http);
        _loaderInstaller = new LoaderInstaller(_http);

        GameFolder = _settings.GameFolder;
        txtFolder.Text = GameFolder;
        txtThreads.Text = _settings.MaxDownloadThreads.ToString();
        cboSource.SelectedIndex = _settings.DownloadSource;
        chkSkipAssets.IsChecked = _settings.SkipAssetsVerify;

        // 加载器下拉：显示中文短名，值仍是枚举
        var loaders = new[]
        {
            ModLoader.Vanilla, ModLoader.Forge, ModLoader.NeoForge,
            ModLoader.Fabric, ModLoader.Quilt, ModLoader.OptiFine
        };
        cboLoader.ItemsSource = loaders.Select(l => new LoaderChoice(l)).ToList();
        cboLoader.DisplayMemberPath = nameof(LoaderChoice.Label);
        cboLoader.SelectedValuePath = nameof(LoaderChoice.Value);
        cboLoader.SelectedIndex = 0;

        LstVersions.ItemsSource = _versions;
        _versionView = (ListCollectionView)CollectionViewSource.GetDefaultView(_versions);
        _versionView.Filter = FilterVersion;
        _versionView.SortDescriptions.Add(new SortDescription(nameof(VersionEntry.ReleaseTime), ListSortDirection.Descending));

        LstVerify.ItemsSource = _verifyItems;
        LstBugs.ItemsSource = _bugs;
        LstLoader.ItemsSource = _loaderVersions;

        Loaded += MainWindow_Loaded;
    }

    private IEnumerable<System.Windows.Controls.Button> AllButtons()
        => new[] { BtnRefresh, BtnDownload, BtnVerify, BtnOpenFolder, BtnSaveSettings, BtnPickJava };

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshVersionListAsync();
    }

    // ==================== 版本列表筛选 ====================

    /// <summary>类型筛选：正式版 / 快照 必须分开，不能靠版本号猜。</summary>
    private string SelectedTypeFilter => (cboType.SelectedIndex) switch
    {
        1 => "release",
        2 => "snapshot",
        3 => "old",
        _ => null
    };

    private bool FilterVersion(object obj)
    {
        if (obj is not VersionEntry v) return false;

        var type = SelectedTypeFilter;
        if (type == "release" && !v.IsRelease) return false;
        if (type == "snapshot" && v.Type != "snapshot") return false;
        if (type == "old" && v.Type is not ("old_beta" or "old_alpha")) return false;

        var kw = txtSearch?.Text?.Trim();
        if (string.IsNullOrEmpty(kw)) return true;
        return v.Id.Contains(kw, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshVersionListAsync()
    {
        _cts = new CancellationTokenSource();
        _busyFlag = true;
        StatusText = "正在获取版本列表……";
        StageText = "";
        ProgressValue = 0;
        try
        {
            var urls = _settings.DownloadSource == 2
                ? new List<string> { DownloadSource.MojangVersionList }
                : new List<string> { DownloadSource.BmclapiVersionList, DownloadSource.MojangVersionList };

            List<VersionEntry> list = null;
            Exception lastError = null;
            foreach (var url in urls)
            {
                try
                {
                    using var doc = await _http.GetJsonWithFallbackAsync(new[] { url }, _cts.Token);
                    list = VersionParser.ParseManifest(doc.RootElement);
                    if (list.Count > 0) break;
                }
                catch (Exception ex) { lastError = ex; }
            }

            if (list == null || list.Count == 0)
                throw new Exception("无法获取版本列表", lastError);

            // 明确统计正式版与快照数量，界面上直接可见
            var releaseCount = list.Count(v => v.IsRelease);
            var snapshotCount = list.Count(v => v.Type == "snapshot");

            var current = (LstVersions.SelectedItem as VersionEntry)?.Id;
            _suppressVersionSelect = true;
            _versions.Clear();
            foreach (var v in list) _versions.Add(v);
            _versionView.Refresh();
            _suppressVersionSelect = false;

            VersionCountText = $"共 {list.Count} 个（正式版 {releaseCount} / 快照 {snapshotCount}）";

            var target = current ?? _settings.LastVersion;
            var sel = string.IsNullOrEmpty(target) ? null : _versions.FirstOrDefault(v => v.Id == target);
            sel ??= _versions.FirstOrDefault(v => v.IsRelease);
            if (sel != null)
            {
                var idx = 0;
                foreach (var item in _versionView)
                {
                    if (ReferenceEquals(item, sel)) { LstVersions.SelectedIndex = idx; break; }
                    idx++;
                }
            }
            StatusText = "版本列表已更新。";
        }
        catch (OperationCanceledException) { StatusText = "已取消"; }
        catch (Exception ex) { StatusText = "获取版本列表失败：" + ex.Message; }
        finally { _busyFlag = false; }
    }

    private void CboType_SelectionChanged(object sender, SelectionChangedEventArgs e) => _versionView?.Refresh();
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => _versionView?.Refresh();

    // ==================== 版本选择 ====================

    private VersionEntry SelectedVersion => LstVersions.SelectedItem as VersionEntry;
    private ModLoader SelectedLoader =>
        cboLoader.SelectedItem is LoaderChoice c ? c.Value : ModLoader.Vanilla;

    private async void LstVersions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressVersionSelect) return;
        var v = SelectedVersion;
        if (v == null) return;

        _settings.LastVersion = v.Id;
        _settings.Save();

        RefreshBugs();
        await RefreshLoaderVersionsAsync();

        var jsonPath = Path.Combine(_settings.GameFolder, "versions", v.Id, v.Id + ".json");
        VersionJsonInfo = File.Exists(jsonPath)
            ? $"本地已有版本文件：{jsonPath}"
            : "本地尚未下载该版本。";
    }

    private async void CboLoader_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshBugs();
        await RefreshLoaderVersionsAsync();
    }

    /// <summary>刷新右侧问题列表。</summary>
    private void RefreshBugs()
    {
        _bugs.Clear();
        var v = SelectedVersion;
        if (v == null) { BugHint = "在左侧选择一个版本，这里会显示该版本的已知问题。"; return; }

        var loader = SelectedLoader;
        var items = BugDatabase.Query(v.Id, v.Type, loader);
        foreach (var b in items) _bugs.Add(b);

        var typeName = v.TypeName;
        BugHint = items.Count == 0
            ? $"「{v.Id}」（{typeName}，{loader.ShortName()}）暂无记录，可参考同家族相邻版本。"
            : $"「{v.Id}」（{typeName}，大版本 {v.MajorTag}，{loader.ShortName()}）共 {items.Count} 条。";
    }

    /// <summary>刷新加载器版本。快照不提供，直接提示。</summary>
    private async Task RefreshLoaderVersionsAsync()
    {
        _loaderVersions.Clear();
        var v = SelectedVersion;
        var loader = SelectedLoader;

        if (v == null || !loader.IsModded())
        {
            LoaderHint = v == null ? "选择版本后可查看可用模组加载器版本。" : "当前为原版，选择加载器后可查询对应版本。";
            return;
        }

        if (!loader.IsAvailable(v.MajorTag, v.IsRelease))
        {
            LoaderHint = v.IsRelease
                ? $"{loader.ShortName()} 不支持 Minecraft {v.MajorTag}。"
                : $"「{v.Id}」是{v.TypeName}，加载器只跟进正式版，快照/远古版本不提供。";
            return;
        }

        LoaderHint = $"正在获取 {loader.ShortName()} 版本……";
        try
        {
            var list = await _loaderService.GetVersionsAsync(loader, v.MajorTag, CancellationToken.None);
            foreach (var item in list) _loaderVersions.Add(item);
            LoaderHint = list.Count == 0
                ? $"未查到 {loader.ShortName()} 对应 {v.MajorTag} 的版本。"
                : $"{loader.ShortName()} 对 {v.MajorTag} 可用 {list.Count} 个版本，双击可安装。";
        }
        catch (Exception ex)
        {
            LoaderHint = $"获取 {loader.ShortName()} 版本失败：{ex.Message}";
        }
    }

    private void LstBugs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstBugs.SelectedItem is not BugEntry b)
        {
            TxtBugDetail.Text = "在上方列表中选择一条问题，可查看详细说明。";
            return;
        }
        TxtBugDetail.Text = $"【{b.Title}】\n严重程度：{b.LevelName}    适用加载器：{b.LoaderTags}\n\n" +
                            $"■ 现象\n{b.DescText}\n\n" +
                            $"■ 原因\n{b.CauseText}\n\n" +
                            $"■ 解决\n{b.FixText}";
    }

    // ==================== 加载器安装 ====================

    private async void LstLoader_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (LstLoader.SelectedItem is not LoaderVersion lv) return;
        var v = SelectedVersion;
        if (v == null) return;

        if (lv.Loader != ModLoader.Forge && lv.Loader != ModLoader.NeoForge)
        {
            Msg($"{lv.Loader.ShortName()} {lv.Version} 需手动安装，本程序只提供自动安装 Forge / NeoForge。\n\n" +
                $"Fabric 安装器：https://fabricmc.net/use/installer/", "提示");
            return;
        }

        var ok = WpfMessageBox.Show(this,
            $"将安装 {lv.Loader.ShortName()} {lv.Version} 到：\n{_settings.GameFolder}\n\n" +
            "安装过程中会自动下载依赖，请勿关闭窗口。是否继续？",
            "确认安装", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;

        var javaPath = _settings.JavaPath;
        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
        {
            Msg("尚未配置 Java 路径。请先在顶部「Java 路径」里指定 java.exe 的位置。", "缺少 Java");
            return;
        }

        _cts = new CancellationTokenSource();
        _busyFlag = true;
        ProgressValue = 0;
        try
        {
            var log = new Progress<string>(s => StatusText = s);
            await _loaderInstaller.InstallAsync(lv.Loader, v.MajorTag, lv.Version,
                _settings.GameFolder, javaPath, log, _cts.Token);
            StatusText = $"{lv.Loader.ShortName()} {lv.Version} 安装完成。";
            ProgressValue = 100;
            Msg($"安装完成。\n\n新版本目录一般在 versions\\{v.MajorTag}-{lv.Loader.ShortName().ToLower()} 下，" +
                $"可用「验证游戏文件」检查依赖是否下全。", "完成");
        }
        catch (OperationCanceledException) { StatusText = "安装已取消。"; }
        catch (Exception ex)
        {
            StatusText = "安装失败：" + ex.Message;
            Msg("安装失败：\n" + ex.Message, "错误");
        }
        finally { _busyFlag = false; }
    }

    // ==================== 按钮 ====================

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshVersionListAsync();

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        StatusText = "正在取消……";
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(GameFolder);
            System.Diagnostics.Process.Start("explorer.exe", GameFolder);
        }
        catch (Exception ex) { Msg("打开目录失败：" + ex.Message, "错误"); }
    }

    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.GameFolder = txtFolder.Text.Trim();
        if (int.TryParse(txtThreads.Text.Trim(), out var t)) _settings.MaxDownloadThreads = Math.Clamp(t, 1, 64);
        _settings.DownloadSource = cboSource.SelectedIndex;
        _settings.SkipAssetsVerify = chkSkipAssets.IsChecked == true;
        _settings.JavaPath = txtJava.Text.Trim();
        _settings.Save();
        GameFolder = _settings.GameFolder;
        StatusText = "设置已保存。";
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        var v = SelectedVersion;
        if (v == null) { Msg("请先在左侧选择要下载的版本。"); return; }

        using var dlg = new FolderBrowserDialog();
        if (!dlg.ShowFolderSelection(this, "选择游戏目录（.minecraft 所在位置）", GameFolder)) return;

        _settings.GameFolder = dlg.SelectedPath;
        _settings.Save();
        GameFolder = dlg.SelectedPath;
        txtFolder.Text = dlg.SelectedPath;

        _cts = new CancellationTokenSource();
        _busyFlag = true;
        ProgressValue = 0;
        try
        {
            var progress = new Progress<(string stage, int done, int total, string file)>(p =>
            {
                StageText = p.stage;
                if (p.total > 0) ProgressValue = p.done * 100.0 / p.total;
                StatusText = $"{p.stage} {p.done}/{p.total}  {p.file}";
            });
            var log = new Progress<string>(s => StatusText = s);

            await _installer.DownloadVersionAsync(_settings.GameFolder, v.Id, progress, log, _cts.Token);
            ProgressValue = 100;
            StatusText = $"「{v.Id}」下载完成。";
            VersionJsonInfo = "下载完成，可点「验证游戏文件」确认完整性。";
        }
        catch (OperationCanceledException) { StatusText = "下载已取消。"; }
        catch (Exception ex)
        {
            StatusText = "下载失败：" + ex.Message;
            Msg("下载失败：\n" + ex.Message, "错误");
        }
        finally { _busyFlag = false; }
    }

    private async void BtnVerify_Click(object sender, RoutedEventArgs e)
    {
        var v = SelectedVersion;
        if (v == null) { Msg("请先在左侧选择要验证的版本。"); return; }

        var gameFolder = txtFolder.Text.Trim();
        if (!Directory.Exists(gameFolder)) { Msg("游戏目录不存在：" + gameFolder); return; }

        _settings.GameFolder = gameFolder;
        _settings.Save();
        GameFolder = gameFolder;

        var jsonPath = Path.Combine(gameFolder, "versions", v.Id, v.Id + ".json");
        if (!File.Exists(jsonPath)) { Msg($"尚未下载版本 {v.Id}，请先点「下载」。"); return; }

        _cts = new CancellationTokenSource();
        _busyFlag = true;
        _verifyItems.Clear();
        Summary = "";
        ProgressValue = 0;
        try
        {
            StatusText = "正在解析版本文件清单……";
            var jsonBytes = await File.ReadAllBytesAsync(jsonPath, _cts.Token);
            byte[] indexBytes = null;

            if (!_settings.SkipAssetsVerify)
            {
                string indexId = null, indexUrl = null;
                using (var doc = System.Text.Json.JsonDocument.Parse(jsonBytes))
                {
                    if (doc.RootElement.TryGetProperty("assetIndex", out var ai) &&
                        ai.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        indexId = VersionParser.GetString(ai, "id");
                        indexUrl = VersionParser.GetString(ai, "url");
                    }
                }

                if (!string.IsNullOrEmpty(indexId))
                {
                    var indexPath = Path.Combine(gameFolder, "assets", "indexes", indexId + ".json");
                    if (File.Exists(indexPath))
                    {
                        try { indexBytes = await File.ReadAllBytesAsync(indexPath, _cts.Token); } catch { }
                    }
                    if (indexBytes == null && !string.IsNullOrEmpty(indexUrl))
                    {
                        StatusText = "本地缺少资源索引，正在下载索引后校验……";
                        indexBytes = await _http.GetBytesWithFallbackAsync(
                            DownloadSource.LauncherOrMeta(indexUrl, _settings.DownloadSource), _cts.Token);
                        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
                        await File.WriteAllBytesAsync(indexPath, indexBytes, _cts.Token);
                    }
                }
            }

            var list = VersionParser.Build(gameFolder, v.Id, jsonBytes,
                skipAssets: _settings.SkipAssetsVerify, assetsIndexBytes: indexBytes);

            StatusText = $"开始校验，共 {list.Count} 个文件……";
            var progress = new Progress<(int done, int total, VerifyItem current)>(p =>
            {
                if (p.total > 0) ProgressValue = p.done * 100.0 / p.total;
                StageText = $"校验 {p.done}/{p.total}";
                StatusText = $"{p.current.StateName}：{p.current.RelativePath}";
                _verifyItems.Add(p.current);
            });

            var report = await _installer.VerifyAsync(list, gameFolder, progress, _cts.Token);
            ProgressValue = 100;
            Summary = report.Summary;
            SummaryBrush = report.BadCount == 0 ? "#2B8A3E" : "#C92A2A";
            StatusText = "校验完成：" + report.Summary;

            if (report.BadCount > 0)
            {
                var ordered = report.Items.OrderBy(i => i.IsOk).ToList();
                _verifyItems.Clear();
                foreach (var i in ordered) _verifyItems.Add(i);
            }
        }
        catch (OperationCanceledException) { StatusText = "校验已取消。"; }
        catch (Exception ex)
        {
            StatusText = "校验失败：" + ex.Message;
            Msg("校验失败：\n" + ex.Message, "错误");
        }
        finally { _busyFlag = false; }
    }

    private void LstVerify_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (LstVerify.SelectedItem is not VerifyItem item) return;
        Msg($"{item.Entry.Group} | {item.Entry.DisplayName}\n" +
            $"状态：{item.StateName}\n路径：{item.RelativePath}\n{item.Detail}\n\n" +
            $"下载地址：{item.Entry.Url}", "文件详情");
    }

    private void BtnPickJava_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "Java 可执行文件|java.exe|所有文件|*.*"
        };
        if (dlg.ShowDialog() == true) txtJava.Text = dlg.FileName;
    }

    // ==================== INotifyPropertyChanged ====================

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _http.Dispose();
        base.OnClosed(e);
    }
}

/// <summary>加载器下拉项（显示中文，携带枚举值）。</summary>
public sealed class LoaderChoice
{
    public LoaderChoice(ModLoader value) => Value = value;
    public ModLoader Value { get; }
    public string Label => Value.ShortName();
}

/// <summary>
/// 文件夹选择框：调用 Win32 的 SHBrowseForFolder。
/// 不用 System.Windows.Forms.FolderBrowserDialog，是为了移除 WinForms 依赖——
/// 有 WinForms 时 dotnet 不允许裁剪（NETSDK1175），体积会翻倍。
/// </summary>
public sealed class FolderBrowserDialog : IDisposable
{
    public string SelectedPath { get; private set; }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, System.Text.StringBuilder pszPath);

    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public string pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;
    private const uint BIF_EDITBOX = 0x0010;

    public bool ShowFolderSelection(Window owner, string title, string initial)
    {
        var bi = new BROWSEINFO
        {
            hwndOwner = new System.Windows.Interop.WindowInteropHelper(owner).Handle,
            lpszTitle = title,
            ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_EDITBOX
        };

        var pidl = SHBrowseForFolder(ref bi);
        if (pidl == IntPtr.Zero) return false;

        try
        {
            var sb = new System.Text.StringBuilder(260);
            if (!SHGetPathFromIDList(pidl, sb)) return false;
            SelectedPath = sb.ToString();
            return !string.IsNullOrWhiteSpace(SelectedPath);
        }
        finally
        {
            CoTaskMemFree(pidl);
        }
    }

    public void Dispose() { }
}
