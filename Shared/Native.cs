using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LuneVers;

/// The few Windows calls WPF doesn't expose.
public static class Native
{
    const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    public static readonly IntPtr HWND_TOPMOST = new(-1), HWND_BOTTOM = new(1);
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// Lets clicks pass through the window and keeps it out of Alt+Tab.
    public static void MakeClickThrough(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    /// Turns click-through on or off (off lets the window take clicks and keys).
    public static void SetClickThrough(IntPtr hwnd, bool on)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style = on ? style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE : style & ~(long)(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    /// Fades a whole window: done by the compositor, so it costs nothing.
    public static void SetAlpha(IntPtr hwnd, byte alpha) => SetLayeredWindowAttributes(hwnd, 0, alpha, 0x2);

    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    /// Keeps the display awake (true) or lets it sleep as usual (false).
    public static void KeepAwake(bool on) => SetThreadExecutionState(on ? 0x80000002u : 0x80000000u);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    /// A display, in physical pixels, with its scale (1 = 96 dpi).
    public sealed record Monitor(RECT Bounds, RECT Work, bool Primary, string Device, double Scale)
    {
        public string Name => Device.Replace(@"\\.\DISPLAY", "Display ");
    }

    public static List<Monitor> Monitors()
    {
        var list = new List<Monitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
            if (GetMonitorInfo(m, ref info))
            {
                double scale = 1;
                try { if (GetDpiForMonitor(m, 0, out var dx, out _) == 0 && dx > 0) scale = dx / 96.0; } catch { }
                list.Add(new Monitor(info.rcMonitor, info.rcWork, (info.dwFlags & 1) != 0, info.szDevice, scale));
            }
            return true;
        }, IntPtr.Zero);
        return list.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.Left).ToList();
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    /// Frosted glass behind the window (Windows 11). Returns false on Windows 10.
    public static bool Acrylic(IntPtr hwnd)
    {
        try
        {
            int dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));          // dark title bar and glass
            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));         // rounded corners
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            int acrylic = 3;                                                 // DWMSBT_TRANSIENTWINDOW
            return Environment.OSVersion.Version.Build >= 22621 && DwmSetWindowAttribute(hwnd, 38, ref acrylic, sizeof(int)) == 0;
        }
        catch { return false; }
    }
}

/// "Start with Windows", through the user's Run key (no admin needed).
public static class Startup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsOn(string name)
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue(name) is string; } catch { return false; }
    }
    public static void Set(string name, bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (on && Environment.ProcessPath is { } path) k.SetValue(name, $"\"{path}\" --background");
            else k.DeleteValue(name, false);
        }
        catch { }
    }
}

/// Settings saved as JSON in %APPDATA%\<app>.
public static class Store
{
    static readonly System.Text.Json.JsonSerializerOptions Options = new() { WriteIndented = true };
    static string File(string app) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), app, "settings.json");

    public static T Load<T>(string app) where T : new()
    {
        try
        {
            var path = File(app);
            if (System.IO.File.Exists(path)) return System.Text.Json.JsonSerializer.Deserialize<T>(System.IO.File.ReadAllText(path), Options) ?? new T();
        }
        catch { }
        return new T();
    }

    public static void Save<T>(string app, T value)
    {
        try
        {
            var path = File(app);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(value, Options));
        }
        catch { }
    }
}

/// Keeps a window on the desktop: above the wallpaper and icons, below apps.
public static class DesktopLayer
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string? name);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    const uint GW_HWNDPREV = 3;

    static string Class(IntPtr h) { var sb = new System.Text.StringBuilder(64); GetClassName(h, sb, 64); return sb.ToString(); }

    public static void Place(IntPtr hwnd)
    {
        var desktop = FindWindow("Progman", null);
        if (desktop == IntPtr.Zero) { Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); return; }
        // The desktop can be several windows (wallpaper and icons) stacked at the
        // bottom. Find the highest of them and sit just above it.
        var top = desktop;
        for (int i = 0; i < 16; i++)
        {
            var prev = GetWindow(top, GW_HWNDPREV);
            if (prev == IntPtr.Zero || prev == hwnd) break;
            var cls = Class(prev);
            if (cls != "WorkerW" && cls != "Progman") break;
            top = prev;
        }
        var above = GetWindow(top, GW_HWNDPREV);
        if (above == hwnd) return;
        // A window is placed *below* the one named, so name the window above the desktop.
        Native.SetWindowPos(hwnd, above == IntPtr.Zero ? Native.HWND_BOTTOM : above, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}
