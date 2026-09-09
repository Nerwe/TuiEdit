namespace TuiEdit;

/// <summary>Строка таблицы: точное совпадение клавиши (AnyShift — шифт не важен).</summary>
internal readonly record struct KeyRow(
    ConsoleKey? Key, bool Alt, bool Ctrl, bool Shift, bool AnyShift, char? KeyChar, EditorCommand Command)
{
    public bool Matches(ConsoleKeyInfo k, bool alt, bool ctrl, bool shift)
    {
        if (KeyChar is not null)
        {
            if (k.KeyChar != KeyChar || Key is not null && k.Key != Key)
                return false;
        }
        else if (k.Key != Key)
        {
            return false;
        }
        return alt == Alt && ctrl == Ctrl && (AnyShift || shift == Shift);
    }
}

/// <summary>
/// Преобразует <see cref="System.ConsoleKeyInfo"/> в <see cref="EditorCommand"/>.
/// </summary>
/// <remarks>
/// Ctrl+S доходит до приложения благодаря сырому режиму ввода
/// (<see cref="Terminal.TryEnableRawInput"/>): без него conhost перехватывает
/// его как паузу вывода XOFF (см. microsoft/terminal#809).
/// Дефолты — таблица ниже (порядок = приоритет); keybindings.json
/// компилируется поверх: замена ключей команды, null — отвязка.
/// </remarks>
public static class KeyMap
{
    private static List<KeyRow> _active = DefaultRows();
    private static readonly HashSet<EditorCommand> _customized = new();

    private static KeyRow R(ConsoleKey key, EditorCommand cmd,
        bool alt = false, bool ctrl = false, bool shift = false, bool anyShift = true) =>
        new(key, alt, ctrl, shift, anyShift, null, cmd);

    /// <summary>Дефолтные биндинги (бывшие свитчи — порядок сохранён).</summary>
    private static List<KeyRow> DefaultRows() => new()
    {
        // Ctrl+Shift особые (раньше букв — как в коде).
        new KeyRow(ConsoleKey.S, false, true, true, false, null, EditorCommand.SaveAs),
        new KeyRow(ConsoleKey.F, false, true, true, false, null, EditorCommand.Grep),
        // Ctrl+/ приходит как Divide или 0x1F (зависит от терминала и раскладки).
        R(ConsoleKey.Divide, EditorCommand.ToggleComment, ctrl: true),
        new KeyRow(null, false, true, false, true, '\x1F', EditorCommand.ToggleComment),
        // Ctrl+Tab перехватывает Windows Terminal — основной путь Ctrl+PgDn/PgUp.
        new KeyRow(ConsoleKey.Tab, false, true, true, false, null, EditorCommand.PrevTab),
        new KeyRow(ConsoleKey.Tab, false, true, false, false, null, EditorCommand.NextTab),
        R(ConsoleKey.Spacebar, EditorCommand.CompleteWord, ctrl: true),
        // Ctrl+буквы/стрелки.
        R(ConsoleKey.S, EditorCommand.Save, ctrl: true),
        R(ConsoleKey.O, EditorCommand.SaveAs, ctrl: true),
        R(ConsoleKey.Q, EditorCommand.Quit, ctrl: true),
        R(ConsoleKey.N, EditorCommand.NewFile, ctrl: true),
        R(ConsoleKey.F, EditorCommand.Find, ctrl: true),
        R(ConsoleKey.H, EditorCommand.Replace, ctrl: true),
        R(ConsoleKey.G, EditorCommand.GoToLine, ctrl: true),
        R(ConsoleKey.K, EditorCommand.CutLine, ctrl: true),
        R(ConsoleKey.U, EditorCommand.Paste, ctrl: true),
        R(ConsoleKey.V, EditorCommand.Paste, ctrl: true),
        R(ConsoleKey.C, EditorCommand.CopyLine, ctrl: true),
        R(ConsoleKey.D, EditorCommand.DuplicateLine, ctrl: true),
        R(ConsoleKey.Z, EditorCommand.Undo, ctrl: true),
        R(ConsoleKey.Y, EditorCommand.Redo, ctrl: true),
        R(ConsoleKey.A, EditorCommand.SelectAll, ctrl: true),
        R(ConsoleKey.B, EditorCommand.ToggleSidebar, ctrl: true),
        R(ConsoleKey.T, EditorCommand.NewTab, ctrl: true),
        R(ConsoleKey.W, EditorCommand.CloseTab, ctrl: true),
        R(ConsoleKey.P, EditorCommand.ListTabs, ctrl: true),
        R(ConsoleKey.D1, EditorCommand.GoPaneNumber, ctrl: true),
        R(ConsoleKey.D2, EditorCommand.GoPaneNumber, ctrl: true),
        R(ConsoleKey.D3, EditorCommand.GoPaneNumber, ctrl: true),
        R(ConsoleKey.D4, EditorCommand.GoPaneNumber, ctrl: true),
        R(ConsoleKey.D5, EditorCommand.GoPaneNumber, ctrl: true),
        R(ConsoleKey.D6, EditorCommand.GoPaneNumber, ctrl: true),
        R(ConsoleKey.D7, EditorCommand.GoPaneNumber, ctrl: true),
        R(ConsoleKey.D8, EditorCommand.GoPaneNumber, ctrl: true),
        R(ConsoleKey.D9, EditorCommand.GoPaneNumber, ctrl: true),
        R(ConsoleKey.E, EditorCommand.GoEnd, ctrl: true),
        R(ConsoleKey.Home, EditorCommand.GoDocStart, ctrl: true),
        R(ConsoleKey.End, EditorCommand.GoDocEnd, ctrl: true),
        R(ConsoleKey.LeftArrow, EditorCommand.WordLeft, ctrl: true),
        R(ConsoleKey.RightArrow, EditorCommand.WordRight, ctrl: true),
        R(ConsoleKey.PageDown, EditorCommand.NextTab, ctrl: true), // вкладки (WT-friendly)
        R(ConsoleKey.PageUp, EditorCommand.PrevTab, ctrl: true),
        R(ConsoleKey.Backspace, EditorCommand.DelWordBefore, ctrl: true),
        R(ConsoleKey.Delete, EditorCommand.DelWordAfter, ctrl: true),
        // Alt (шифт игнорировался — AnyShift).
        R(ConsoleKey.F, EditorCommand.OpenMenuFile, alt: true),
        R(ConsoleKey.E, EditorCommand.OpenMenuEdit, alt: true),
        R(ConsoleKey.H, EditorCommand.OpenMenuHelp, alt: true),
        R(ConsoleKey.N, EditorCommand.ToggleLineNumbers, alt: true),
        R(ConsoleKey.Z, EditorCommand.ToggleWrap, alt: true),
        R(ConsoleKey.OemPeriod, EditorCommand.ToggleWhitespace, alt: true),
        R(ConsoleKey.OemMinus, EditorCommand.ToggleFold, alt: true),
        R(ConsoleKey.UpArrow, EditorCommand.MoveLineUp, alt: true),
        R(ConsoleKey.DownArrow, EditorCommand.MoveLineDown, alt: true),
        R(ConsoleKey.D1, EditorCommand.GoTabNumber, alt: true),
        R(ConsoleKey.D2, EditorCommand.GoTabNumber, alt: true),
        R(ConsoleKey.D3, EditorCommand.GoTabNumber, alt: true),
        R(ConsoleKey.D4, EditorCommand.GoTabNumber, alt: true),
        R(ConsoleKey.D5, EditorCommand.GoTabNumber, alt: true),
        R(ConsoleKey.D6, EditorCommand.GoTabNumber, alt: true),
        R(ConsoleKey.D7, EditorCommand.GoTabNumber, alt: true),
        R(ConsoleKey.D8, EditorCommand.GoTabNumber, alt: true),
        R(ConsoleKey.D9, EditorCommand.GoTabNumber, alt: true),
        R(ConsoleKey.D0, EditorCommand.GoTabNumber, alt: true), // десятая вкладка
        R(ConsoleKey.S, EditorCommand.SplitPane, alt: true), // разделить вид
        R(ConsoleKey.O, EditorCommand.QuickOpen, alt: true), // быстрый переход к файлу
        R(ConsoleKey.Oem6, EditorCommand.GoBracketMatch, alt: true), // Alt+] — парная скобка
        // Shift+F-клавиши (точный шифт).
        // Shift+F3 — поиск назад, Shift+F6 — предыдущая панель, Shift+F2 — закладка.
        new KeyRow(ConsoleKey.F3, false, false, true, false, null, EditorCommand.FindPrev),
        new KeyRow(ConsoleKey.F2, false, false, true, false, null, EditorCommand.NextBookmark),
        new KeyRow(ConsoleKey.F6, false, false, true, false, null, EditorCommand.PrevPane),
        // Голые клавиши.
        R(ConsoleKey.LeftArrow, EditorCommand.MoveLeft),
        R(ConsoleKey.RightArrow, EditorCommand.MoveRight),
        R(ConsoleKey.UpArrow, EditorCommand.MoveUp),
        R(ConsoleKey.DownArrow, EditorCommand.MoveDown),
        R(ConsoleKey.Home, EditorCommand.GoHome),
        R(ConsoleKey.End, EditorCommand.GoEnd),
        R(ConsoleKey.PageUp, EditorCommand.PageUp),
        R(ConsoleKey.PageDown, EditorCommand.PageDown),
        R(ConsoleKey.F3, EditorCommand.FindNext),
        R(ConsoleKey.F1, EditorCommand.Help),
        R(ConsoleKey.F2, EditorCommand.ToggleBookmark),
        R(ConsoleKey.F4, EditorCommand.DocStats),
        R(ConsoleKey.F9, EditorCommand.FileFormat),
        R(ConsoleKey.F6, EditorCommand.NextPane),
        R(ConsoleKey.F5, EditorCommand.CommandPalette),
        R(ConsoleKey.F12, EditorCommand.CommandLine),
        R(ConsoleKey.F10, EditorCommand.ToggleMenu),
        R(ConsoleKey.Escape, EditorCommand.None),
        R(ConsoleKey.Enter, EditorCommand.InsertEnter),
        R(ConsoleKey.Backspace, EditorCommand.InsertBackspace),
        R(ConsoleKey.Delete, EditorCommand.InsertDelete),
        new KeyRow(ConsoleKey.Tab, false, false, true, false, null, EditorCommand.Unindent),
        new KeyRow(ConsoleKey.Tab, false, false, false, false, null, EditorCommand.InsertTab),
    };

    /// <summary>Применить сырые оверрайды (порядок файла, last-wins; null — отвязка).</summary>
    public static void SetOverrides(Dictionary<string, string?> raw)
    {
        List<KeyRow> rows = DefaultRows();
        _customized.Clear();
        foreach ((string name, string? notation) in raw)
        {
            if (!Enum.TryParse<EditorCommand>(name, ignoreCase: true, out EditorCommand cmd)
                || cmd == EditorCommand.None)
                continue;
            KeyStroke? stroke = notation is null ? null : KeyBindings.Parse(notation);
            if (notation is not null && stroke is null)
                continue; // мусор в нотации — команду не трогаем
            _customized.Add(cmd);
            rows.RemoveAll(r => r.Command == cmd);
            if (stroke is null)
                continue;
            KeyStroke s = stroke.Value;
            rows.RemoveAll(r => r.Key == s.Key && r.Alt == s.Alt && r.Ctrl == s.Ctrl
                && (r.AnyShift || r.Shift == s.Shift));
            rows.Add(new KeyRow(s.Key, s.Alt, s.Ctrl, s.Shift, false, null, cmd));
        }
        _active = rows;
    }

    /// <summary>Сбросить к дефолтам (для тестов).</summary>
    public static void ResetToDefaults()
    {
        _active = DefaultRows();
        _customized.Clear();
    }

    /// <summary>Подсказка меню для команды: null — нет оверрайда (брать литерал), "" — отвязана.</summary>
    public static string? HintFor(EditorCommand cmd)
    {
        if (!_customized.Contains(cmd))
            return null;
        foreach (KeyRow r in _active)
            if (r.Command == cmd && r.Key is not null && r.KeyChar is null)
                return KeyBindings.Format(new KeyStroke(r.Key.Value, r.Alt, r.Ctrl, r.Shift));
        return string.Empty;
    }

    public static EditorCommand Map(ConsoleKeyInfo key)
    {
        bool alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        if (alt && ctrl)
            return EditorCommand.None;
        foreach (KeyRow r in _active)
            if (r.Matches(key, alt, ctrl, shift))
                return r.Command;
        if (alt || ctrl)
            return EditorCommand.None;
        return char.IsControl(key.KeyChar) ? EditorCommand.None : EditorCommand.InsertChar;
    }
}
