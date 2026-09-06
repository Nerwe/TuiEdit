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

    /// <summary>Привести к допустимым значениям.</summary>
    public void Normalize()
    {
        Theme = Theme is "light" or "dark" ? Theme : "dark";
        Language = Loc.Normalize(Language);
    }
}
