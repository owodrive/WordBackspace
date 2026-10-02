using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WordBackspace;

// What happens to keys typed while a word is being deleted. The deletion
// backspaces are injected one at a time on the hook thread; a key the user
// types in that window would otherwise be delivered interleaved with them
// and could leave part of the word behind. Swallowing such keys prevents
// that. EnterOnly swallows just the Enter key (the one that scrambles the
// cursor line); AllKeys swallows everything typed during the burst.
enum BurstSwallowMode
{
    EnterOnly,
    AllKeys,
}

// Global (not per-word) settings, persisted to settings.json next to
// words.json. The value is kept in memory; only the file load/save takes
// the lock, so the matcher can read SwallowMode on every key without cost.
class AppSettings
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly string _path;
    private Payload _data;

    public AppSettings()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WordBackspace");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        lock (_lock)
        {
            _data = Load();
        }
        Logger.Info($"settings ready: {_path} (swallow={SwallowMode})");
    }

    public BurstSwallowMode SwallowMode
    {
        get { lock (_lock) return Parse(_data.SwallowMode); }
    }

    // Delay between the individual backspaces of a deletion, in ms per
    // character. 1 is the default (as fast as the 1 ms timer allows), 0 is
    // as fast as possible, higher values delete more slowly.
    public int DeletionDelayMs
    {
        get { lock (_lock) return _data.DeletionDelayMs; }
    }

    public void SetSwallowMode(BurstSwallowMode mode)
    {
        lock (_lock)
        {
            _data.SwallowMode = mode == BurstSwallowMode.AllKeys ? "allKeys" : "enterOnly";
            Save();
        }
        Logger.Info($"swallow mode = {mode}");
    }

    public void SetDeletionDelayMs(int ms)
    {
        int clamped;
        lock (_lock)
        {
            clamped = Math.Clamp(ms, 0, 500);
            _data.DeletionDelayMs = clamped;
            Save();
        }
        Logger.Info($"deletion delay = {clamped} ms/char");
    }

    private sealed class Payload
    {
        [JsonPropertyName("swallowMode")]
        public string SwallowMode { get; set; } = "allKeys";

        [JsonPropertyName("deletionDelayMs")]
        public int DeletionDelayMs { get; set; } = 1;
    }

    private static BurstSwallowMode Parse(string s)
        => string.Equals(s, "enterOnly", StringComparison.OrdinalIgnoreCase)
            ? BurstSwallowMode.EnterOnly
            : BurstSwallowMode.AllKeys;

    private Payload Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Payload();
            var p = JsonSerializer.Deserialize<Payload>(File.ReadAllText(_path));
            if (p == null) return new Payload();
            p.DeletionDelayMs = Math.Clamp(p.DeletionDelayMs, 0, 500);
            return p;
        }
        catch (Exception ex)
        {
            Logger.Error($"failed to load settings: {ex.Message}");
            return new Payload();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, JsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Error($"failed to save settings: {ex.Message}");
        }
    }
}