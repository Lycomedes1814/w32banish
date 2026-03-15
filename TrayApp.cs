using System.Runtime.InteropServices;

namespace CursorHider;

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
    [DllImport("kernel32.dll")] static extern bool   SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, bool add);

    // Magnification API — hides/shows the cursor at the DWM compositor level,
    // which works globally across all processes including WSLg windows.
    [DllImport("Magnification.dll")] static extern bool MagInitialize();
    [DllImport("Magnification.dll")] static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] static extern bool MagShowSystemCursor(bool fShowCursor);

    private delegate bool ConsoleCtrlDelegate(uint ctrlType);

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL    = 14;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int WM_MOUSEMOVE   = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    // ── State ────────────────────────────────────────────────────────────────

    private IntPtr _kbHook;
    private IntPtr _msHook;
    private bool   _hidden;
    private POINT  _lastPos;

    // Keep delegates alive to prevent GC collection
    private readonly HookProc            _kbProc;
    private readonly HookProc            _msProc;
    private readonly ConsoleCtrlDelegate _ctrlProc;

    private readonly NotifyIcon _tray;

    // ── Constructor ──────────────────────────────────────────────────────────

    public TrayApp()
    {
        MagInitialize();

        _kbProc   = KeyboardHook;
        _msProc   = MouseHook;
        _ctrlProc = CtrlHandler;
        SetConsoleCtrlHandler(_ctrlProc, true);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon    = SystemIcons.Application,
            Text    = "CursorHider – running",
            Visible = true,
            ContextMenuStrip = menu,
        };

        InstallHooks();
    }

    // ── Hook installation ────────────────────────────────────────────────────

    private void InstallHooks()
    {
        var hMod = GetModuleHandle(null);
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _msHook = SetWindowsHookEx(WH_MOUSE_LL,    _msProc, hMod, 0);
    }

    // ── Hook callbacks ───────────────────────────────────────────────────────

    private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
            HideCursor();

        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_MOUSEMOVE && _hidden)
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

    // ── Cleanup ──────────────────────────────────────────────────────────────

    // Ctrl+C / Ctrl+Break / window close — called on a background thread by the OS.
    // Application.Exit() is thread-safe; it posts WM_QUIT which causes Application.Run()
    // to return, WinForms then disposes this ApplicationContext → Dispose → ExitApp.
    private bool CtrlHandler(uint ctrlType)
    {
        Application.Exit();
        return true; // suppress default termination; we handle cleanup via ExitApp
    }

    private void ExitApp()
    {
        ShowCursorAgain();          // always restore before exit
        MagUninitialize();
        UnhookWindowsHookEx(_kbHook);
        UnhookWindowsHookEx(_msHook);
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ExitApp();
        base.Dispose(disposing);
    }
}
