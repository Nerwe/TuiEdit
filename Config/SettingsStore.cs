using System.Text.Json;

namespace TuiEdit;

/// <summary>
/// Хранилище настроек в JSON
/// (<c>%APPDATA%\TuiEdit\settings.json</c>, на Linux — <c>~/.config/TuiEdit</c>).
/// </summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Путь к файлу.</summary>
    public string Path { get; } = path;

    /// <summary>Путь по умолчанию для платформы.</summary>
    public static string DefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TuiEdit", "settings.json");

    /// <summary>Загрузить (нет/битый — умолчания).</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path));
                if (s is not null)
                {
                    s.Normalize();
                    return s;
                }
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    /// <summary>Сохранить (тихо игнорирует ошибки IO).</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
        }
    }
}
