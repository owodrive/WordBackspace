using System.Text;

namespace WordBackspace;

class Matcher
{
    // Unique dwExtraInfo marker for backspaces we inject ourselves.
    // Our own injected backspaces carry this marker and are skipped by the
    // matcher; all other keys (physical or injected by other tools/tests) are processed.
    private static readonly IntPtr Marker = new(0x00BAD00D);

    // "Up to the last space" deletes at most the word plus this many
    // characters typed before it, so a long run of typing followed by the
    // word cannot turn into a huge backspace burst.
    private const int MaxOvershootChars = 10;

    private readonly WordStore _store;
    private readonly AppSettings _settings;
    private readonly StringBuilder _buffer = new();
    // Characters typed across spaces, reset only by hard delimiters (Enter,
    // Tab). Multi-word words ("oh no") match against this instead of the
    // buffer, which restarts at every space.
    private readonly StringBuilder _history = new();
    // Backspaces from triggered deletions that are still queued in the hook
    // and have not passed through it yet. While > 0, a user key typed now
    // would be delivered interleaved with the pending backspaces and could
    // leave part of the word behind, so it is swallowed per the setting.
    private int _burstRemaining;
    private IntPtr _excludedWindow = IntPtr.Zero;
    // The buffer is used by the hook thread and cleared by ClearBuffer from
    // the UI thread (resume), so every access goes through this lock.
    private readonly object _bufferLock = new();

    public bool Paused { get; set; }
    public IntPtr ExcludedWindow
    {
        get => _excludedWindow;
        set => _excludedWindow = value;
    }

    public Matcher(WordStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    // Returns true if the key should be consumed (swallowed) so it never reaches the foreground app.
    public bool HandleKey(NativeMethods.KBDLLHOOKSTRUCT k)
    {
        if (k.dwExtraInfo == Marker)
        {
            // Our own injected backspace. Count it down; when this reaches
            // zero the burst has fully passed through the hook and user keys
            // are normal again.
            if (_burstRemaining > 0) _burstRemaining--;
            return false;
        }

        uint vk = k.vkCode;

        // A user key arriving while the burst's backspaces are still queued
        // would interleave with them and leave part of the word behind.
        // Swallow it per the setting instead of letting it through.
        if (_burstRemaining > 0)
        {
            var mode = _settings.SwallowMode;
            bool isEnter = vk == 0x0D;
            if (mode == BurstSwallowMode.AllKeys || (mode == BurstSwallowMode.EnterOnly && isEnter))
            {
                Logger.Debug($"swallowed vk 0x{vk:X} during burst (mode={mode})");
                return true;
            }
        }

        bool ctrl = (NativeMethods.GetKeyState(0x11) & 0x8000) != 0;
        bool alt = (NativeMethods.GetKeyState(0x12) & 0x8000) != 0;
        // Shift XOR CapsLock is the effective case for letter keys.
        bool shift = ((NativeMethods.GetKeyState(0x10) & 0x8000) != 0)
                   ^ ((NativeMethods.GetKeyState(0x14) & 0x0001) != 0);

        if (_excludedWindow != IntPtr.Zero
            && NativeMethods.GetForegroundWindow() == _excludedWindow
            && NativeMethods.IsWindowVisible(_excludedWindow))
        {
            return false;
        }

if (Paused)
    {
        Logger.Debug("paused, key ignored");
        return false;
    }

    if (ctrl || alt)
        {
            if (vk == 0x08)
            {
                lock (_bufferLock)
                {
                    _buffer.Clear();
                    _history.Clear();
                }
                Logger.Info("Ctrl+Backspace -> buffer cleared");
            }
            return false;
        }

        switch (vk)
        {
            case 0x08: // Backspace
            case 0x2E: // Delete
                lock (_bufferLock)
                {
                    if (_buffer.Length > 0) _buffer.Remove(_buffer.Length - 1, 1);
                    if (_history.Length > 0) _history.Remove(_history.Length - 1, 1);
                    Logger.Debug($"vk 0x{vk:X} -> buffer='{_buffer}'");
                }
                return false;
        }

        switch (vk)
        {
            case 0x09: // Tab
            case 0x0D: // Enter
                lock (_bufferLock)
                {
                    _buffer.Clear();
                    _history.Clear();
                }
                Logger.Debug($"delimiter vk 0x{vk:X} -> buffer cleared");
                return false;

            case 0x20: // Space
                lock (_bufferLock)
                {
                    _buffer.Clear();
                    _history.Append(' ');
                }
                Logger.Debug($"delimiter vk 0x{vk:X} -> buffer cleared");
                return false;

            case 0xBA: // \
            case 0xBB: // ]
            case 0xBC: // ,
            case 0xBD: // -
            case 0xBE: // [
            case 0xBF: // ;
            case 0xC0: // `
            case 0xDB: // =
            case 0xDD: // '
            case 0xDE: // .
            case 0xDF: // /
                lock (_bufferLock) _buffer.Clear();
                Logger.Debug($"delimiter vk 0x{vk:X} -> buffer cleared");
                return false;
        }

        char? c = TryGetChar(vk, (k.flags & 0x01) != 0, shift);
        if (c == null)
        {
            Logger.Debug($"ignored vk 0x{vk:X}");
            return false;
        }

        lock (_bufferLock)
        {
            _buffer.Append(c.Value);
            _history.Append(c.Value);
            string s = _buffer.ToString();
            string h = _history.ToString();
            string sLower = s.ToLowerInvariant();
            string hLower = h.ToLowerInvariant();
            Logger.Debug($"typed '{c.Value}' -> buffer='{s}'");

            // The longest winning match deletes the most text, so a word like
            // "oh no" beats a single word like "no" for the same input.
            Word? best = null;
            int bestCount = 0;
            foreach (var w in _store.Words)
            {
                string buf = w.CaseSensitive ? s : sLower;
                string hist = w.CaseSensitive ? h : hLower;
                string word = w.CaseSensitive ? w.Text : w.Lower;
                bool hit;
                int count;
                if (word.Contains(' '))
                {
                    // Multi-word words match the history, which spans spaces.
                    // "exact word" additionally requires the match to start at
                    // the beginning of the history or right after a typed space.
                    // Both deletion modes remove the word itself (the last space
                    // before it is the word's own boundary).
                    hit = hist.EndsWith(word, StringComparison.Ordinal)
                         && (!w.ExactOnly || hist.Length == word.Length || hist[hist.Length - word.Length - 1] == ' ');
                    count = word.Length;
                }
                else
                {
                    hit = w.ExactOnly ? buf == word : buf.EndsWith(word, StringComparison.Ordinal);
                    // "word only" deletes just the word; "up to the last space"
                    // deletes everything typed since the last space (the whole
                    // buffer, which always contains the word at this point),
                    // capped at word + MaxOvershootChars (see above).
                    count = w.WordOnly ? word.Length : Math.Min(buf.Length, word.Length + MaxOvershootChars);
                }
                if (hit && count > bestCount)
                {
                    best = w;
                    bestCount = count;
                }
            }
            if (best != null)
            {
                Logger.Info($"MATCH word='{best.Text}' exactOnly={best.ExactOnly} buffer='{s}' history='{h}' -> {bestCount} backspaces (deferred)");
                // The burst deletes only bestCount characters, so anything
                // earlier in the buffer (a capped "up to the last space"
                // deletion) stays in the target app and must stay in the
                // buffer so later matches keep mirroring it.
                if (_buffer.Length > bestCount) _buffer.Remove(0, _buffer.Length - bestCount);
                else _buffer.Clear();
                if (_history.Length > bestCount) _history.Remove(0, _history.Length - bestCount);
                else _history.Clear();
                // These backspaces are still queued (not yet through the hook)
                // until they pass; count them so user keys typed in the meantime
                // are swallowed per the setting.
                _burstRemaining += bestCount;
                // Defer the actual key injection until after this hook callback returns,
                // so the triggering character is already queued in the target window's
                // input queue before our backspaces are.
                DeletionRequested?.Invoke(bestCount);
                return false;
            }
            return false;
        }
    }

    // Wired to the keyboard hook's deferred-deletion path.
    public Action<int>? DeletionRequested { get; set; }

// Runs on the deletion worker thread (the hook thread stays free so it
// keeps servicing the input queue and the burst's own backspaces pass
// through the proc without hitting the low-level hook timeout). Keys typed
// while a deletion is pumping are still processed into the buffer, and a
// nested match simply queues another deletion that runs after this one.
    public void ExecuteDeletion(int count)
    {
        if (count <= 0) return;
        int delay = _settings.DeletionDelayMs;
        NativeMethods.timeBeginPeriod(1);
        try
        {
            for (int i = 0; i < count; i++)
            {
                NativeMethods.keybd_event((byte)NativeMethods.VK_BACK, 0, 0, Marker);
                NativeMethods.keybd_event((byte)NativeMethods.VK_BACK, 0, NativeMethods.KEYEVENTF_KEYUP, Marker);
                if (delay > 0) Thread.Sleep(delay);
            }
        }
        finally
        {
            NativeMethods.timeEndPeriod(1);
        }
        Logger.Info($"deletion finished ({count} backspaces)");
    }

    public void ClearBuffer()
    {
        lock (_bufferLock)
        {
            _buffer.Clear();
            _history.Clear();
        }
    }

    private static char? TryGetChar(uint vk, bool extended, bool shift)
    {
        if (vk >= 0x41 && vk <= 0x5A) return (char)((shift ? 'A' : 'a') + (vk - 0x41));
        if (vk >= 0x30 && vk <= 0x39) return (char)('0' + (vk - 0x30));
        if (extended && vk >= 0x60 && vk <= 0x69) return (char)('0' + (vk - 0x60));
        return null;
    }
}