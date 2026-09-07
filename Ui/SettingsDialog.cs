namespace TuiEdit;

/// <summary>
/// Диалог настроек: состояние — <see cref="SettingsDialogState"/>,
/// значения применяются и сохраняются сразу при листании.
/// Рамка, центрирование и цикл — из базового <see cref="Dialog"/>.
/// </summary>
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
        if (screenW < 20 || screenH < 5)
            return null;
        var (labels, values, title) = Rows(loc);
        int inner = 0;
        for (int i = 0; i < SettingsDialogState.RowCount; i++)
            inner = Math.Max(inner, labels[i].Length + values[i].Length + 8);
        int boxW = Math.Min(Math.Max(inner + 2, title.Length + 6), screenW);
        int boxH = SettingsDialogState.RowCount + 2; // заголовок + строки + низ
        int x0 = Math.Max(0, (screenW - boxW) / 2);
        int y0 = Math.Max(0, (screenH - boxH) / 2);
        if (y0 + boxH > screenH)
            return null;
        return new DialogBox(x0, y0, boxW, boxH);
    }

    private (string[] labels, string[] values, string title) Rows(Loc loc)
    {
        string title = loc["settings.title"];
        string[] labels = [loc["settings.theme"], loc["settings.lang"],
            loc["settings.matchcase"], loc["settings.wholeword"],
            loc["settings.shownumbers"], loc["settings.wordwrap"],
            loc["settings.backup"], loc["settings.useregex"]];
        string langName = loc.Language == "en" ? "English" : "Русский";
        string themeName = _settings.Theme == "light" ? loc["settings.light"] : loc["settings.dark"];
        string[] values = [themeName, langName,
            OnOff(loc, _settings.SearchMatchCase), OnOff(loc, _settings.SearchWholeWord),
            OnOff(loc, _settings.ShowLineNumbers), OnOff(loc, _settings.WordWrap),
            OnOff(loc, _settings.BackupOnSave), OnOff(loc, _settings.SearchUseRegex)];
        return (labels, values, title);
    }

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        // Точный размер окна — как раньше: по самой длинной строке.
        var (labels, values, _) = Rows(loc);
        Theme t = theme;
        for (int i = 0; i < SettingsDialogState.RowCount; i++)
        {
            string cell = $" {labels[i]}: < {values[i]} >";
            int inner = box.W - 2;
            if (cell.Length > inner)
                cell = cell[..inner];
            int y = box.Y0 + 1 + i;
            if (i == _state.Row)
                screen.Text(box.X0, y, "│" + cell.PadRight(inner) + "│", t.ButtonSelFg, t.ButtonSelBg);
            else
                screen.Text(box.X0, y, "│" + cell.PadRight(inner) + "│", t.ModalFg, t.ModalBg);
        }
    }

    private static string OnOff(Loc loc, bool v) => v ? loc["settings.on"] : loc["settings.off"];

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

    /// <summary>Листание значения настройки с применением и сохранением (тогглы — переворот).</summary>
    private void CycleSetting(int dir)
    {
        switch (_state.Row)
        {
            case 0:
                int ti = SettingsDialogState.Cycle(
                    Array.IndexOf(Themes.Names, _settings.Theme), Themes.Names.Length, dir);
                _settings.Theme = Themes.Names[ti];
                break;
            case 1:
                int li = SettingsDialogState.Cycle(
                    Array.IndexOf(Loc.Supported, _settings.Language), Loc.Supported.Length, dir);
                _settings.Language = Loc.Supported[li];
                break;
            case 2:
                _settings.SearchMatchCase = !_settings.SearchMatchCase;
                break;
            case 3:
                _settings.SearchWholeWord = !_settings.SearchWholeWord;
                break;
            case 4:
                _settings.ShowLineNumbers = !_settings.ShowLineNumbers;
                break;
            case 5:
                _settings.WordWrap = !_settings.WordWrap;
                break;
            case 6:
                _settings.BackupOnSave = !_settings.BackupOnSave;
                break;
            default:
                _settings.SearchUseRegex = !_settings.SearchUseRegex;
                break;
        }
        _store.Save(_settings);
        _onChanged();
    }
}
