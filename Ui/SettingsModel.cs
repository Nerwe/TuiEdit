using System.Globalization;

namespace TuiEdit;

/// <summary>
/// Строки настроек: подписи, значения, листание. Общее ядро для
/// <see cref="SettingsDialog"/> и палитры команд (порядок строк — контракт).
/// </summary>
internal static class SettingsModel
{
    public const int Count = 13;

    public const int MouseRow = 9;

    public const int CopyOnSelectRow = 10;

    public const int AutoPairsRow = 11;

    public const int GitGutterRow = 12;

    public static string Label(int row, Loc loc) => row switch
    {
        0 => loc["settings.theme"],
        1 => loc["settings.lang"],
        2 => loc["settings.shownumbers"],
        3 => loc["settings.wordwrap"],
        4 => loc["settings.whitespace"],
        5 => loc["settings.ruler"],
        6 => loc["settings.backup"],
        7 => loc["settings.guides"],
        8 => loc["settings.session"],
        9 => loc["settings.mouse"],
        10 => loc["settings.copyselect"],
        11 => loc["settings.autopairs"],
        _ => loc["settings.gitgutter"],
    };

    public static string Value(int row, AppSettings settings, Loc loc) => row switch
    {
        0 => ThemeCatalog.DisplayName(loc, settings.Theme),
        1 => loc.Language == "en" ? "English" : "Русский",
        2 => OnOff(loc, settings.ShowLineNumbers),
        3 => OnOff(loc, settings.WordWrap),
        4 => OnOff(loc, settings.ShowWhitespace),
        5 => settings.RulerColumn == 0
            ? loc["settings.off"]
            : settings.RulerColumn.ToString(CultureInfo.InvariantCulture),
        6 => OnOff(loc, settings.BackupOnSave),
        7 => OnOff(loc, settings.ShowIndentGuides),
        8 => OnOff(loc, settings.RestoreSession),
        9 => MouseName(loc, settings.Mouse),
        10 => OnOff(loc, settings.CopyOnSelect),
        11 => OnOff(loc, settings.AutoPairs),
        _ => OnOff(loc, settings.GitGutter),
    };

    /// <summary>Шагнуть значение строки (dir +1/-1); сохранение — на вызывающем.</summary>
    public static void Cycle(AppSettings settings, int row, int dir)
    {
        switch (row)
        {
            case 0:
                List<string> names = ThemeCatalog.Names(settings);
                int cur = names.FindIndex(n =>
                    string.Equals(n, settings.Theme, StringComparison.OrdinalIgnoreCase));
                if (cur < 0)
                    cur = 0;
                settings.Theme = names[SettingsDialogState.Cycle(cur, names.Count, dir)];
                break;
            case 1:
                int li = SettingsDialogState.Cycle(
                    Array.IndexOf(Loc.Supported, settings.Language), Loc.Supported.Length, dir);
                settings.Language = Loc.Supported[li];
                break;
            case 2:
                settings.ShowLineNumbers = !settings.ShowLineNumbers;
                break;
            case 3:
                settings.WordWrap = !settings.WordWrap;
                break;
            case 4:
                settings.ShowWhitespace = !settings.ShowWhitespace;
                break;
            case 5:
                int[] steps = [0, 80, 100, 120];
                int ri = Array.IndexOf(steps, settings.RulerColumn);
                if (ri < 0)
                    ri = dir >= 0 ? -1 : 0;
                settings.RulerColumn = steps[SettingsDialogState.Cycle(ri, steps.Length, dir)];
                break;
            case 6:
                settings.BackupOnSave = !settings.BackupOnSave;
                break;
            case 7:
                settings.ShowIndentGuides = !settings.ShowIndentGuides;
                break;
            case 8:
                settings.RestoreSession = !settings.RestoreSession;
                break;
            case 9:
                settings.Mouse = (MouseLevel)SettingsDialogState.Cycle((int)settings.Mouse, 4, dir);
                break;
            case 10:
                settings.CopyOnSelect = !settings.CopyOnSelect;
                break;
            case 11:
                settings.AutoPairs = !settings.AutoPairs;
                break;
            default:
                settings.GitGutter = !settings.GitGutter;
                break;
        }
    }

    internal static string OnOff(Loc loc, bool v) => v ? loc["settings.on"] : loc["settings.off"];

    internal static string MouseName(Loc loc, MouseLevel level) => level switch
    {
        MouseLevel.Basic => loc["settings.mouse.basic"],
        MouseLevel.Drag => loc["settings.mouse.drag"],
        MouseLevel.Motion => loc["settings.mouse.motion"],
        _ => loc["settings.mouse.off"],
    };
}
