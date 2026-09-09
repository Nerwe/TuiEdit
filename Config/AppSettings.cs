namespace TuiEdit;

/// <summary>
/// Пользовательские настройки (хранятся в JSON, см. <see cref="SettingsStore"/>).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Тема: dark | light | своя из Themes.</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Грамматика подсветки: auto (по расширению) или имя языка.</summary>
    public string Grammar { get; set; } = "auto";

    /// <summary>Язык: en | ru.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Поиск/замена: учитывать регистр.</summary>
    public bool SearchMatchCase { get; set; } = true;

    /// <summary>Поиск/замена: только целые слова.</summary>
    public bool SearchWholeWord { get; set; }

    /// <summary>Поиск/замена: регулярное выражение.</summary>
    public bool SearchUseRegex { get; set; }

    /// <summary>Показывать номера строк (гуттер).</summary>
    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Вертикальные направляющие на уровнях отступа.</summary>
    public bool ShowIndentGuides { get; set; } = true;

    /// <summary>Показывать пробелы и табы точками и стрелками.</summary>
    public bool ShowWhitespace { get; set; }

    /// <summary>Подсвечиваемая колонка-ограничитель (0 — выкл).</summary>
    public int RulerColumn { get; set; }

    /// <summary>Мягкий перенос длинных строк.</summary>
    public bool WordWrap { get; set; }

    /// <summary>Копия .bak при сохранении.</summary>
    public bool BackupOnSave { get; set; }

    /// <summary>Открывать при старте вкладки прошлой сессии.</summary>
    public bool RestoreSession { get; set; }

    /// <summary>Мышь: уровень захвата (выкл по умолчанию — не все терминалы корректны).</summary>
    public MouseLevel Mouse { get; set; }

    /// <summary>Старый флаг мыши (до уровней): только миграция в <see cref="Normalize"/>.</summary>
    public bool EnableMouse { get; set; }

    /// <summary>Вкладки прошлой сессии (путь + курсор).</summary>
    public List<SessionTab> SessionTabs { get; set; } = new();

    /// <summary>Недавние файлы (новые сверху).</summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>Пользовательские темы (см. ThemeScheme).</summary>
    public List<ThemeScheme> Themes { get; set; } = new();

    /// <summary>Максимум недавних файлов.</summary>
    public const int MaxRecentFiles = 20;

    /// <summary>Максимум вкладок сессии.</summary>
    public const int MaxSessionTabs = 20;

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
        if (!ThemeCatalog.Contains(this, Theme))
            Theme = "dark";
        Themes.RemoveAll(s => string.IsNullOrWhiteSpace(s.Name));
        foreach (ThemeScheme s in Themes)
            s.Colors ??= new();
        if (string.IsNullOrWhiteSpace(Grammar))
            Grammar = "auto";
        Language = Loc.Normalize(Language);
        if (!Enum.IsDefined(Mouse))
            Mouse = MouseLevel.Off;
        if (EnableMouse && Mouse == MouseLevel.Off)
            Mouse = MouseLevel.Basic; // миграция со старого флага
        EnableMouse = false;
    }
}

/// <summary>Вкладка прошлой сессии: путь и позиция курсора.</summary>
public sealed record SessionTab(string Path, int Row, int Col);
