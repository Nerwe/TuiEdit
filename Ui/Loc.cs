using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace TuiEdit;

/// <summary>Provides UI localization: strings from embedded JSON resources (<c>Resources/strings.{lang}.json</c>).</summary>
internal sealed class Loc
{
    public static readonly string[] Supported = ["en", "ru"];

    public string Language { get; }

    private readonly Dictionary<string, string> _map;

    private Loc(string language, Dictionary<string, string> map)
    {
        Language = language;
        _map = map;
    }

    /// <summary>Normalizes the language code (unknown becomes en).</summary>
    public static string Normalize(string? language) => language switch
    {
        "ru" => "ru",
        "en" => "en",
        _ => "en",
    };

    /// <summary>Loads language strings (falls back to the key itself).</summary>
    public static Loc Load(string? language)
    {
        string lang = Normalize(language);
        Dictionary<string, string> map = new();
        try
        {
            using Stream? s = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream($"TuiEdit.Resources.strings.{lang}.json");
            if (s is not null)
                map = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
        }
        catch
        {
        }
        return new Loc(lang, map);
    }

    /// <summary>Gets the string for a key (missing keys return the key itself).</summary>
    public string this[string key] =>
        _map.TryGetValue(key, out string? v) ? v : key;

    public string Format(string key, params object?[] args)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, this[key], args);
        }
        catch (FormatException)
        {
            return this[key];
        }
    }
}
