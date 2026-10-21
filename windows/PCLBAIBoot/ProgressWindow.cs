using System.Runtime.InteropServices;

namespace PCLBAIBoot;

/// <summary>
/// 纯 Win32 进度窗口。
/// 用原生 API 而不依赖 WinForms/WPF：那两者会阻止 IL 裁剪（NETSDK1175），
/// 裁剪后引导器才能压到 10 MB 量级。
/// </summary>
internal static class ProgressWindow
{
    private const int WS_OVERLAPPED = 0x00000000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CHILD = 0x40000000;
    private const int WS_EX_DLGMODALFRAME = 0x00000001;
    private const uint PBM_SETPOS = 0x0402;
    private const uint PBM_SETRANGE32 = 0x0406;
    private const uint SS_LEFT = 0x00000000;
    private const uint CS_DROPSHADOW = 0x00020000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName,
        int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int SetWindowTextW(IntPtr hWnd, string text);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("comctl32.dll")] private static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    [StructLayout(LayoutKind.Sequential)]
    private struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }

    private static IntPtr _hwnd;
    private static IntPtr _progress;
    private static IntPtr _label;
    private static WndProcDelegate _proc; // 防止委托被 GC 回收

    private const int WM_CLOSE = 0x0010;

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // 下载中不允许关闭，避免留下半成品
        if (msg == WM_CLOSE) return IntPtr.Zero;
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>创建并显示进度窗口（须在 STA 线程中调用）。</summary>
    public static void Show(string title, string initialStatus)
    {
        var icc = new INITCOMMONCONTROLSEX
        {
            dwSize = Marshal.SizeOf<INITCOMMONCONTROLSEX>(),
            dwICC = 0x000000FF
        };
        try { InitCommonControlsEx(ref icc); } catch { }

        var hInst = GetModuleHandleW(null);

        _proc = WndProc;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            style = CS_DROPSHADOW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInst,
            hbrBackground = IntPtr.Zero,
            lpszClassName = "PCLBAIBootProgress"
        };
        RegisterClassEx(ref wc);

        const int W = 460, H = 150;
        var screenW = GetSystemMetrics(0);
        var screenH = GetSystemMetrics(1);

        _hwnd = CreateWindowEx(WS_EX_DLGMODALFRAME, "PCLBAIBootProgress", title,
            WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_VISIBLE,
            (screenW - W) / 2, (screenH - H) / 2, W, H,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero) return;

        _label = CreateWindowEx(0, "STATIC", initialStatus, WS_CHILD | WS_VISIBLE | (int)SS_LEFT,
            22, 22, W - 60, 24, _hwnd, IntPtr.Zero, hInst, IntPtr.Zero);

        _progress = CreateWindowEx(0, "msctls_progress32", "", WS_CHILD | WS_VISIBLE,
            22, 58, W - 60, 20, _hwnd, IntPtr.Zero, hInst, IntPtr.Zero);

        if (_progress != IntPtr.Zero)
            SendMessageW(_progress, PBM_SETRANGE32, 0, 100);

        ShowWindow(_hwnd, 1);
        UpdateWindow(_hwnd);
    }

    /// <summary>更新进度与状态文字，可从任意线程调用。</summary>
    public static void SetProgress(int percent, string status)
    {
        if (_progress != IntPtr.Zero)
            SendMessageW(_progress, PBM_SETPOS, Math.Clamp(percent, 0, 100), IntPtr.Zero);
        if (_label != IntPtr.Zero && status != null)
            SetWindowTextW(_label, status);
    }

    public static void Close()
    {
        var h = _hwnd;
        _hwnd = IntPtr.Zero;
        _progress = IntPtr.Zero;
        _label = IntPtr.Zero;
        if (h != IntPtr.Zero) DestroyWindow(h);
    }
}
