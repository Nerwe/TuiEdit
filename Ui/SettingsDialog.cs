using System.Globalization;

namespace TuiEdit;

/// <summary>Диалог настроек: значения применяются и сохраняются сразу при листании.</summary>
internal sealed class SettingsDialog : Dialog
{
    private readonly SettingsDialogState _state = new();
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly Action _onChanged;

    public SettingsDialog(AppSettings settings, SettingsStore store, Action onChanged)
    {
        _settings = settings;
        _store = store;
        _onChanged = onChanged;
    }

    protected override string GetTitle(Loc loc) => loc["settings.title"];

    protected override DialogBox? Measure(int screenW, int screenH, Loc loc)
    {
        var (labels, values, title) = Rows(loc);
        return MeasureOptions(screenW, screenH, title, labels, values);
    }

    private (string[] labels, string[] values, string title) Rows(Loc loc)
    {
        string title = loc["settings.title"];
        string[] labels = [loc["settings.theme"], loc["settings.lang"],
            loc["settings.shownumbers"], loc["settings.wordwrap"], loc["settings.whitespace"],
            loc["settings.ruler"],
            loc["settings.backup"], loc["settings.guides"], loc["settings.session"],
            loc["settings.mouse"]];
        string langName = loc.Language == "en" ? "English" : "Русский";
        string themeName = ThemeCatalog.DisplayName(loc, _settings.Theme);
        string[] values = [themeName, langName,
            OnOff(loc, _settings.ShowLineNumbers), OnOff(loc, _settings.WordWrap),
            OnOff(loc, _settings.ShowWhitespace),
            _settings.RulerColumn == 0 ? loc["settings.off"] : _settings.RulerColumn.ToString(CultureInfo.InvariantCulture),
            OnOff(loc, _settings.BackupOnSave),
            OnOff(loc, _settings.ShowIndentGuides), OnOff(loc, _settings.RestoreSession),
            MouseName(loc, _settings.Mouse)];
        return (labels, values, title);
    }

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        var (labels, values, _) = Rows(loc);
        DrawOptionRows(screen, theme, box, labels, values, _state.Row);
    }

    private static string OnOff(Loc loc, bool v) => v ? loc["settings.on"] : loc["settings.off"];

    private static string MouseName(Loc loc, MouseLevel level) => level switch
    {
        MouseLevel.Basic => loc["settings.mouse.basic"],
        MouseLevel.Drag => loc["settings.mouse.drag"],
        MouseLevel.Motion => loc["settings.mouse.motion"],
        _ => loc["settings.mouse.off"],
    };

    /// <summary>Клик по строке: выбрать и шагнуть (+1), как стрелка вправо.</summary>
    public override bool HandleClick(int x, int y, int screenW, int screenH, Loc loc)
    {
        DialogBox? box = Measure(screenW, screenH, loc);
        if (box is null)
            return false;
        DialogBox b = box.Value;
        if (x < b.X0 || x >= b.X0 + b.W || y < b.Y0 || y >= b.Y0 + b.H)
            return false;
        var (labels, _, _) = Rows(loc);
        int row = y - (b.Y0 + 1); // строки опций — зеркало DrawOptionRows
        if (row < 0 || row >= labels.Length)
            return true;
        _state.MoveTo(row);
        CycleSetting(1);
        return true;
    }

    public override void HandleKey(ConsoleKeyInfo key)
    {
        var k = key;
        if ((k.Modifiers & ConsoleModifiers.Control) != 0)
            return;
        switch (k.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Enter:
                Closed = true; // изменения сохраняются сразу при листании
                return;
            case ConsoleKey.UpArrow: _state.Move(-1); break;
            case ConsoleKey.DownArrow: _state.Move(1); break;
            case ConsoleKey.Home: _state.Move(-SettingsDialogState.RowCount); break;
            case ConsoleKey.End: _state.Move(SettingsDialogState.RowCount); break;
            case ConsoleKey.LeftArrow: CycleSetting(-1); break;
            case ConsoleKey.RightArrow: CycleSetting(1); break;
        }
    }

    private void CycleSetting(int dir)
    {
        switch (_state.Row)
        {
            case 0:
                List<string> names = ThemeCatalog.Names(_settings);
                int cur = names.FindIndex(n =>
                    string.Equals(n, _settings.Theme, StringComparison.OrdinalIgnoreCase));
                if (cur < 0)
                    cur = 0;
                _settings.Theme = names[SettingsDialogState.Cycle(cur, names.Count, dir)];
                break;
            case 1:
                int li = SettingsDialogState.Cycle(
                    Array.IndexOf(Loc.Supported, _settings.Language), Loc.Supported.Length, dir);
                _settings.Language = Loc.Supported[li];
                break;
            case 2:
                _settings.ShowLineNumbers = !_settings.ShowLineNumbers;
                break;
            case 3:
                _settings.WordWrap = !_settings.WordWrap;
                break;
            case 4:
                _settings.ShowWhitespace = !_settings.ShowWhitespace;
                break;
            case 5:
                int[] steps = [0, 80, 100, 120];
                int ri = Array.IndexOf(steps, _settings.RulerColumn);
                if (ri < 0)
                    ri = dir >= 0 ? -1 : 0;
                _settings.RulerColumn = steps[SettingsDialogState.Cycle(ri, steps.Length, dir)];
                break;
            case 6:
                _settings.BackupOnSave = !_settings.BackupOnSave;
                break;
            case 7:
                _settings.ShowIndentGuides = !_settings.ShowIndentGuides;
                break;
            case 8:
                _settings.RestoreSession = !_settings.RestoreSession;
                break;
            default:
                _settings.Mouse = (MouseLevel)SettingsDialogState.Cycle((int)_settings.Mouse, 4, dir);
                break;
        }
        _store.Save(_settings);
        _onChanged();
    }
}
