namespace TuiEdit;

/// <summary>Provides the settings dialog: values apply and save immediately while cycling.</summary>
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
        string[] labels = new string[SettingsModel.Count];
        string[] values = new string[SettingsModel.Count];
        for (int i = 0; i < SettingsModel.Count; i++)
        {
            labels[i] = SettingsModel.Label(i, loc);
            values[i] = SettingsModel.Value(i, _settings, loc);
        }
        return (labels, values, loc["settings.title"]);
    }

    protected override void DrawContent(Screen screen, Theme theme, Loc loc, Rgb fg, Rgb bg, DialogBox box)
    {
        var (labels, values, _) = Rows(loc);
        DrawOptionRows(screen, theme, box, labels, values, _state.Row);
    }

    /// <summary>Handles a row click: selects and steps (+1), like RightArrow.</summary>
    public override bool HandleClick(int x, int y, int screenW, int screenH, Loc loc)
    {
        DialogBox? box = Measure(screenW, screenH, loc);
        if (box is null)
            return false;
        DialogBox b = box.Value;
        if (x < b.X0 || x >= b.X0 + b.W || y < b.Y0 || y >= b.Y0 + b.H)
            return false;
        var (labels, _, _) = Rows(loc);
        int row = y - (b.Y0 + 1); // Option rows mirror DrawOptionRows
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
                Closed = true; // Changes save immediately while cycling
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
        SettingsModel.Cycle(_settings, _state.Row, dir);
        _store.Save(_settings);
        _onChanged();
    }
}
