namespace TuiEdit;

/// <summary>
/// Пользовательские настройки (хранятся в JSON, см. <see cref="SettingsStore"/>).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Тема: dark | light.</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Язык: en | ru.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Поиск/замена: учитывать регистр.</summary>
    public bool SearchMatchCase { get; set; } = true;

    /// <summary>Поиск/замена: только целые слова.</summary>
    public bool SearchWholeWord { get; set; } = false;

    /// <summary>Поиск/замена: регулярное выражение.</summary>
    public bool SearchUseRegex { get; set; } = false;

    /// <summary>Показывать номера строк (гуттер).</summary>
    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Мягкий перенос длинных строк.</summary>
    public bool WordWrap { get; set; } = false;

    /// <summary>Копия .bak при сохранении.</summary>
    public bool BackupOnSave { get; set; } = false;

    /// <summary>Недавние файлы (новые сверху).</summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>Максимум недавних файлов.</summary>
    public const int MaxRecentFiles = 20;

    /// <summary>Отметить файл недавним (вверх, без дублей, с обрезкой).</summary>
    public void TouchRecent(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }
        StringComparison cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        RecentFiles.RemoveAll(p => string.Equals(p, full, cmp));
        RecentFiles.Insert(0, full);
        if (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }

    /// <summary>Убрать из недавних несуществующие файлы.</summary>
    public void PruneRecent() => RecentFiles.RemoveAll(p => !File.Exists(p));

    /// <summary>Привести к допустимым значениям.</summary>
    public void Normalize()
    {
        Theme = Theme is "light" or "dark" ? Theme : "dark";
        Language = Loc.Normalize(Language);
    }
}
