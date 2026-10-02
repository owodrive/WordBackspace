using System.Runtime.InteropServices;

namespace WordBackspace;

class KeyboardHook : IDisposable
{
    private const uint WM_APP_DELETE = 0x8001;

    // Returns true to consume the key (it will not reach the foreground app).
    public delegate bool KeyFilter(NativeMethods.KBDLLHOOKSTRUCT data);

    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private Thread? _thread;
    private Thread? _lastDeletion;
    private uint _threadId;

    public KeyFilter? Filter { get; set; }

    // Invoked (on the hook thread) when a deferred deletion is due.
    public Action<int>? DeletionRequested { get; set; }

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "KeyboardHook" };
        _thread.Start();
    }

    public void RequestDeletion(int count)
    {
        bool ok = NativeMethods.PostThreadMessage(_threadId, WM_APP_DELETE, (IntPtr)count, IntPtr.Zero);
        Logger.Debug($"RequestDeletion count={count} win32ThreadId={_threadId} posted={ok} err={(ok ? 0 : Marshal.GetLastWin32Error())}");
    }

    private void ThreadMain()
    {
        // Win32 thread id of the hook thread (managed id differs on .NET). PostThreadMessage needs this.
        _threadId = NativeMethods.GetCurrentThreadId();

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hookId == IntPtr.Zero)
        {
            Logger.Error($"SetWindowsHookEx failed, error {Marshal.GetLastWin32Error()}");
            return;
        }
        Logger.Info($"keyboard hook installed (win32 thread id {_threadId})");

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_APP_DELETE)
            {
                Logger.Debug($"WM_APP_DELETE received, count={(int)msg.wParam}");
                // Run the burst on a worker thread so this hook thread keeps
                // servicing the input queue promptly. While ExecuteDeletion
                // blocks the hook thread (count * delay ms) every input event
                // waits in the queue, and events that wait longer than the
                // low-level hook timeout (LowLevelHooksTimeout, default
                // 300 ms) are skipped: they reach the app without passing
                // through the proc, unswallowed. Chains after any in-flight
                // burst so nested deletions still run in order.
                int count = (int)msg.wParam;
                Thread? previous = _lastDeletion;
                var t = new Thread(() =>
                {
                    previous?.Join();
                    DeletionRequested?.Invoke(count);
                })
                { IsBackground = true, Name = "Deletion" };
                _lastDeletion = t;
                t.Start();
                continue;
            }
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            Logger.Info("keyboard hook uninstalled");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0
            && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (Filter?.Invoke(data) == true)
                    return (IntPtr)1;
            }
            catch (Exception ex)
            {
                Logger.Error($"hook callback error: {ex}");
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        // PostThreadMessage takes the Win32 thread id (captured in
        // ThreadMain), not the .NET managed thread id, which can coincide
        // with an unrelated thread's id.
        if (_thread is { IsAlive: true } && _threadId != 0)
        {
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}