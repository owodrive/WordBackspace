using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace WordBackspace;

// All file I/O happens on one background writer thread, so the hot
// per-keystroke paths (the keyboard hook) only pay for a queue push and a
// string format — never a disk open/append/close.
static class Logger
{
    private const int MaxBytes = 1_000_000;

    private static readonly string Dir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WordBackspace");
    private static readonly string LogFile = System.IO.Path.Combine(Dir, "wordbackspace.log");
    private static readonly ConcurrentQueue<string> _queue = new();
    private static readonly ManualResetEventSlim _wake = new(false);
    // Writer thread only (the startup delete happens before the thread starts).
    private static long _size;

    static Logger()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(LogFile)) File.Delete(LogFile);
        }
        catch
        {
        }
        new Thread(WriteLoop) { IsBackground = true, Name = "Logger" }.Start();
    }

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Debug(string msg) => Write("DEBUG", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        // Enqueue before signaling so the writer never sees the event set
        // with an empty queue it hasn't drained yet.
        _queue.Enqueue($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {msg}{Environment.NewLine}");
        _wake.Set();
    }

    private static void WriteLoop()
    {
        while (true)
        {
            _wake.Wait();
            var batch = new StringBuilder();
            while (_queue.TryDequeue(out var line)) batch.Append(line);
            if (batch.Length == 0) continue;
            try
            {
                if (_size > MaxBytes)
                {
                    File.Delete(LogFile);
                    _size = 0;
                }
                File.AppendAllText(LogFile, batch.ToString());
                _size += batch.Length;
            }
            catch
            {
            }
        }
    }
}