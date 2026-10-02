using System.IO;
using System.Text.Json;

namespace WordBackspace;

class WordStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly string _path;
    private List<Word> _words = new();
    // Lock-free snapshot the matcher reads on every keystroke: replaced
    // (never mutated) under the lock, so readers never take the lock or pay
    // for a ToList() copy.
    private volatile Word[] _snapshot = Array.Empty<Word>();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _reloadCts;
    private bool _saving;

    public event EventHandler? Changed;

    public WordStore()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WordBackspace");
        Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, "words.json");

        lock (_lock)
        {
            _words = Load();
            if (_words.Count == 0)
            {
                _words = new List<Word> { new("WordBackspace", true, false, true) };
                File.WriteAllText(_path, JsonSerializer.Serialize(_words, JsonOpts));
                Logger.Info($"seeded default words: {string.Join(", ", _words.Select(w => w.Text))}");
            }
            _snapshot = _words.ToArray();
        }

        StartWatcher();
        Logger.Info($"word store ready: {_path} ({_words.Count} words)");
    }

    public IReadOnlyList<Word> Words => _snapshot;

    public void AddOrUpdate(string text, bool exactOnly, bool caseSensitive, bool wordOnly)
    {
        text = Normalize(text);
        if (text.Length == 0) return;

        bool added;
        lock (_lock)
        {
            var existing = FindLocked(text);
            if (existing != null)
            {
                existing.Text = text;
                existing.ExactOnly = exactOnly;
                existing.CaseSensitive = caseSensitive;
                existing.WordOnly = wordOnly;
                added = false;
            }
            else
            {
                _words.Add(new Word(text, exactOnly, caseSensitive, wordOnly));
                added = true;
            }
            _snapshot = _words.ToArray();
            SaveLocked();
        }
        Logger.Info($"{(added ? "added" : "updated")} word '{text}' exactOnly={exactOnly} caseSensitive={caseSensitive} wordOnly={wordOnly}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Words are unique case-insensitively ("Oops" and "oops" are the same
    // entry; the stored text keeps the case it was last entered with).
    private Word? FindLocked(string text)
        => _words.FirstOrDefault(w => string.Equals(w.Text, text, StringComparison.OrdinalIgnoreCase));

    public void Remove(string text)
    {
        lock (_lock)
        {
            var w = FindLocked(text);
            if (w == null) return;
            _words.Remove(w);
            _snapshot = _words.ToArray();
            SaveLocked();
        }
        Logger.Info($"removed word '{text}'");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateFlags(string text, bool exactOnly, bool caseSensitive, bool wordOnly)
    {
        lock (_lock)
        {
            var w = FindLocked(text);
            if (w == null) return;
            w.ExactOnly = exactOnly;
            w.CaseSensitive = caseSensitive;
            w.WordOnly = wordOnly;
            _snapshot = _words.ToArray();
            SaveLocked();
        }
        Logger.Info($"word '{text}' exactOnly={exactOnly} caseSensitive={caseSensitive} wordOnly={wordOnly}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateText(string oldText, string newText, bool exactOnly, bool caseSensitive, bool wordOnly)
    {
        newText = Normalize(newText);
        if (newText.Length == 0)
        {
            Remove(oldText);
            return;
        }

        lock (_lock)
        {
            var w = FindLocked(oldText);
            if (w == null) return;
            w.Text = newText;
            w.Lower = newText.ToLowerInvariant();
            w.ExactOnly = exactOnly;
            w.CaseSensitive = caseSensitive;
            w.WordOnly = wordOnly;
            foreach (var dup in _words.Where(x => !ReferenceEquals(x, w)
                && string.Equals(x.Text, newText, StringComparison.OrdinalIgnoreCase)).ToList())
                _words.Remove(dup);
            _snapshot = _words.ToArray();
            SaveLocked();
        }
        Logger.Info($"renamed word '{oldText}' -> '{newText}' exactOnly={exactOnly} caseSensitive={caseSensitive} wordOnly={wordOnly}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Words may contain spaces ("oh no") and keep the case they were entered
// with. Trim the ends and collapse runs of spaces; the matcher handles
// case sensitivity per word.
    public static string Normalize(string text)
    {
        text = text.Trim();
        if (text.Contains("  "))
            text = string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return text;
    }

    private List<Word> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<Word>();
            var list = JsonSerializer.Deserialize<List<Word>>(File.ReadAllText(_path));
            if (list == null) return new List<Word>();
            return list
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .Select(w => new Word(Normalize(w.Text), w.ExactOnly, w.CaseSensitive, w.WordOnly))
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error($"failed to load words: {ex.Message}");
            return new List<Word>();
        }
    }

    private void SaveLocked()
    {
        try
        {
            _saving = true;
            File.WriteAllText(_path, JsonSerializer.Serialize(_words, JsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Error($"failed to save words: {ex.Message}");
        }
        finally
        {
            _saving = false;
        }
    }

    private void StartWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(
                System.IO.Path.GetDirectoryName(_path)!,
                System.IO.Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };

            void Handler(object? sender, FileSystemEventArgs e)
            {
                if (_saving) return;
                _reloadCts?.Cancel();
                var cts = new CancellationTokenSource();
                _reloadCts = cts;
                Task.Delay(400, cts.Token).ContinueWith(t =>
                {
                    if (t.IsCanceled) return;
                    try
                    {
                        lock (_lock)
                        {
                            _words = Load();
                            _snapshot = _words.ToArray();
                            Logger.Info($"words reloaded from file ({_words.Count} words)");
                        }
                        Changed?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"reload from file failed: {ex.Message}");
                    }
                }, TaskScheduler.Default);
            }

            _watcher.Changed += Handler;
            _watcher.Created += Handler;
            _watcher.Deleted += Handler;
            _watcher.Renamed += Handler;
            _watcher.EnableRaisingEvents = true;
            Logger.Info("file watcher active for words.json");
        }
        catch (Exception ex)
        {
            Logger.Error($"file watcher failed: {ex.Message}");
        }
    }
}