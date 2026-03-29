using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace W32Banish;

/// <summary>
/// Runs as a system tray icon. Installs global low-level hooks:
///   - Keyboard hook  → hides the cursor on any key press
///   - Mouse hook     → shows the cursor again when the mouse moves
/// </summary>
sealed class TrayApp : ApplicationContext
{
    // ── Win32 ────────────────────────────────────────────────────────────────

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]   static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")]   static extern bool   UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]   static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Magnification API — hides/shows the cursor at the DWM compositor level,
    // which works globally across all processes including WSLg windows.
    [DllImport("Magnification.dll")] static extern bool MagInitialize();
    [DllImport("Magnification.dll")] static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] static extern bool MagShowSystemCursor(bool fShowCursor);

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL    = 14;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int WM_MOUSEMOVE   = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    // Virtual key codes for modifier keys
    private const uint VK_LSHIFT   = 0xA0, VK_RSHIFT   = 0xA1;
    private const uint VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    private const uint VK_LMENU   = 0xA4, VK_RMENU   = 0xA5;
    private const uint VK_LWIN    = 0x5B, VK_RWIN    = 0x5C;

    private const string RunKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "W32Banish";

    // ── State ────────────────────────────────────────────────────────────────

    private IntPtr _kbHook;
    private IntPtr _msHook;
    private bool   _hidden;
    private bool   _exited;
    private bool   _paused;
    private POINT  _lastPos;

    // Keep delegates alive to prevent GC collection
    private readonly HookProc _kbProc;
    private readonly HookProc _msProc;

    private readonly NotifyIcon          _tray;
    private readonly ContextMenuStrip    _menu;
    private readonly ToolStripMenuItem   _pauseItem;
    private readonly ToolStripMenuItem   _startupItem;

    // ── Constructor ──────────────────────────────────────────────────────────

    public TrayApp()
    {
        if (!MagInitialize())
            throw new InvalidOperationException("MagInitialize failed.");

        _kbProc = KeyboardHook;
        _msProc = MouseHook;

        _pauseItem  = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
        {
            Checked = IsStartupEnabled(),
        };

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon             = SystemIcons.Application,
            Text             = "W32Banish – running",
            Visible          = true,
            ContextMenuStrip = _menu,
        };

        InstallHooks();
    }

    // ── Hook installation ────────────────────────────────────────────────────

    private void InstallHooks()
    {
        var hMod = GetModuleHandle(null);
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _msHook = SetWindowsHookEx(WH_MOUSE_LL,    _msProc, hMod, 0);

        if (_kbHook == IntPtr.Zero || _msHook == IntPtr.Zero)
            throw new InvalidOperationException("SetWindowsHookEx failed.");
    }

    // ── Hook callbacks ───────────────────────────────────────────────────────

    private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_paused &&
            ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN))
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (!IsModifierKey(kb.vkCode))
                HideCursor();
        }

        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private static bool IsModifierKey(uint vk) =>
        vk is VK_LSHIFT or VK_RSHIFT
          or VK_LCONTROL or VK_RCONTROL
          or VK_LMENU or VK_RMENU
          or VK_LWIN or VK_RWIN;

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_MOUSEMOVE && _hidden)
        {
            var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            // Only restore if the mouse actually moved (ignore synthetic move events)
            if (s.pt.x != _lastPos.x || s.pt.y != _lastPos.y)
                ShowCursorAgain();
        }

        return CallNextHookEx(_msHook, nCode, wParam, lParam);
    }

    // ── Cursor control ───────────────────────────────────────────────────────

    private void HideCursor()
    {
        if (_hidden) return;
        _hidden = true;

        // Record position so we can ignore synthetic WM_MOUSEMOVE at same spot
        var pt = Cursor.Position;
        _lastPos = new POINT { x = pt.X, y = pt.Y };

        MagShowSystemCursor(false);
    }

    private void ShowCursorAgain()
    {
        if (!_hidden) return;
        _hidden = false;

        MagShowSystemCursor(true);
    }

    // ── Pause / Resume ────────────────────────────────────────────────────────

    private void TogglePause()
    {
        _paused = !_paused;
        _pauseItem.Text = _paused ? "Resume" : "Pause";
        _tray.Text = _paused ? "W32Banish – paused" : "W32Banish – running";
        if (_paused) ShowCursorAgain();
    }

    // ── Start with Windows ──────────────────────────────────────────────────

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(AppName) is string;
    }

    private void ToggleStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)!;
        if (IsStartupEnabled())
        {
            key.DeleteValue(AppName, false);
            _startupItem.Checked = false;
        }
        else
        {
            var exe = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(AppName, $"\"{exe}\"");
            _startupItem.Checked = true;
        }
    }

    // ── Cleanup ──────────────────────────────────────────────────────────────

    private void ExitApp()
    {
        if (_exited) return;
        _exited = true;

        ShowCursorAgain();          // always restore before exit
        MagUninitialize();
        UnhookWindowsHookEx(_kbHook);
        UnhookWindowsHookEx(_msHook);
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ExitApp();
        base.Dispose(disposing);
    }
}
