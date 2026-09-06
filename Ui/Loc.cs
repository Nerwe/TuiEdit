using System.Reflection;
using System.Text.Json;

namespace TuiEdit;

/// <summary>
/// Локализация UI: строки из встроенных JSON-ресурсов
/// (<c>Resources/strings.{lang}.json</c>).
/// </summary>
public sealed class Loc
{
    /// <summary>Поддерживаемые языки.</summary>
    public static readonly string[] Supported = ["ru", "en"];

    /// <summary>Текущий язык.</summary>
    public string Language { get; }

    private readonly Dictionary<string, string> _map;

    private Loc(string language, Dictionary<string, string> map)
    {
        Language = language;
        _map = map;
    }

    /// <summary>Нормализация кода языка (неизвестный — ru).</summary>
    public static string Normalize(string? language) => language switch
    {
        "en" => "en",
        "ru" => "ru",
        _ => "ru",
    };

    /// <summary>Загрузить строки языка (fallback — сам ключ).</summary>
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

    /// <summary>Строка по ключу (нет — сам ключ).</summary>
    public string this[string key] =>
        _map.TryGetValue(key, out string? v) ? v : key;

    /// <summary>Форматированная строка по ключу.</summary>
    public string Format(string key, params object?[] args)
    {
        try
        {
            return string.Format(this[key], args);
        }
        catch (FormatException)
        {
            return this[key];
        }
    }
}
