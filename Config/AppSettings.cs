namespace TuiEdit;

/// <summary>
/// Пользовательские настройки (хранятся в JSON, см. <see cref="SettingsStore"/>).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Тема: dark | light.</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Язык: ru | en.</summary>
    public string Language { get; set; } = "ru";

    /// <summary>Поиск/замена: учитывать регистр.</summary>
    public bool SearchMatchCase { get; set; } = true;

    /// <summary>Поиск/замена: только целые слова.</summary>
    public bool SearchWholeWord { get; set; } = false;

    /// <summary>Показывать номера строк (гуттер).</summary>
    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Мягкий перенос длинных строк.</summary>
    public bool WordWrap { get; set; } = false;

    /// <summary>Привести к допустимым значениям.</summary>
    public void Normalize()
    {
        Theme = Theme is "light" or "dark" ? Theme : "dark";
        Language = Loc.Normalize(Language);
    }
}
