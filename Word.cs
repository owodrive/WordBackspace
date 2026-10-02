using System.Text.Json.Serialization;

namespace WordBackspace;

public class Word
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("exactOnly")]
    public bool ExactOnly { get; set; }

    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; set; }

    // true: backspace only the word itself; false: backspace everything
    // typed since the last space (the legacy behavior).
    [JsonPropertyName("wordOnly")]
    public bool WordOnly { get; set; }

    // Lowercased text, kept in sync by the constructors WordStore uses. The
    // matcher matches case-insensitively against it without re-lowercasing
    // the word on every keystroke. Not serialized.
    [JsonIgnore]
    public string Lower = "";

    public Word()
    {
    }

    public Word(string text, bool exactOnly, bool caseSensitive = false, bool wordOnly = false)
    {
        Text = text;
        ExactOnly = exactOnly;
        CaseSensitive = caseSensitive;
        WordOnly = wordOnly;
        Lower = text.ToLowerInvariant();
    }
}