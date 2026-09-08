namespace TuiEdit;

/// <summary>
/// Преобразует <see cref="System.ConsoleKeyInfo"/> в <see cref="EditorCommand"/>.
/// </summary>
/// <remarks>
/// Ctrl+S доходит до приложения благодаря сырому режиму ввода
/// (<see cref="Terminal.TryEnableRawInput"/>): без него conhost перехватывает
/// его как паузу вывода XOFF (см. microsoft/terminal#809).
/// </remarks>
public static class KeyMap
{
    public static EditorCommand Map(ConsoleKeyInfo key)
    {
        bool alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;

        if (alt && !ctrl)
        {
            return key.Key switch
            {
                ConsoleKey.F => EditorCommand.OpenMenuFile,
                ConsoleKey.E => EditorCommand.OpenMenuEdit,
                ConsoleKey.H => EditorCommand.OpenMenuHelp,
                ConsoleKey.N => EditorCommand.ToggleLineNumbers,
                ConsoleKey.Z => EditorCommand.ToggleWrap,
                ConsoleKey.UpArrow => EditorCommand.MoveLineUp,
                ConsoleKey.DownArrow => EditorCommand.MoveLineDown,
                ConsoleKey.D1 => EditorCommand.GoTabNumber,
                ConsoleKey.D2 => EditorCommand.GoTabNumber,
                ConsoleKey.D3 => EditorCommand.GoTabNumber,
                ConsoleKey.D4 => EditorCommand.GoTabNumber,
                ConsoleKey.D5 => EditorCommand.GoTabNumber,
                ConsoleKey.D6 => EditorCommand.GoTabNumber,
                ConsoleKey.D7 => EditorCommand.GoTabNumber,
                ConsoleKey.D8 => EditorCommand.GoTabNumber,
                ConsoleKey.D9 => EditorCommand.GoTabNumber,
                ConsoleKey.D0 => EditorCommand.GoTabNumber, // десятая вкладка
                ConsoleKey.S => EditorCommand.SplitPane, // разделить вид
                ConsoleKey.Oem6 => EditorCommand.GoBracketMatch, // Alt+] — парная скобка
                _ => EditorCommand.None,
            };
        }
        if (alt)
            return EditorCommand.None;

        if ((key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            if (key.Key == ConsoleKey.S && (key.Modifiers & ConsoleModifiers.Shift) != 0)
                return EditorCommand.SaveAs;
            // Ctrl+/ приходит как Divide или 0x1F (зависит от терминала и раскладки).
            if (key.Key == ConsoleKey.Divide || key.KeyChar == '\x1F')
                return EditorCommand.ToggleComment;
            // Ctrl+Tab перехватывает Windows Terminal — основной путь Ctrl+PgDn/PgUp.
            if (key.Key == ConsoleKey.Tab)
                return (key.Modifiers & ConsoleModifiers.Shift) != 0
                    ? EditorCommand.PrevTab : EditorCommand.NextTab;
            return key.Key switch
            {
                ConsoleKey.S => EditorCommand.Save,
                ConsoleKey.O => EditorCommand.SaveAs,
                ConsoleKey.Q => EditorCommand.Quit,
                ConsoleKey.N => EditorCommand.NewFile,
                ConsoleKey.F => EditorCommand.Find,
                ConsoleKey.H => EditorCommand.Replace,
                ConsoleKey.G => EditorCommand.GoToLine,
                ConsoleKey.K => EditorCommand.CutLine,
                ConsoleKey.U => EditorCommand.Paste,
                ConsoleKey.V => EditorCommand.Paste,
                ConsoleKey.C => EditorCommand.CopyLine,
                ConsoleKey.D => EditorCommand.DuplicateLine,
                ConsoleKey.Z => EditorCommand.Undo,
                ConsoleKey.Y => EditorCommand.Redo,
                ConsoleKey.A => EditorCommand.SelectAll,
                ConsoleKey.B => EditorCommand.ToggleSidebar,
                ConsoleKey.T => EditorCommand.NewTab,
                ConsoleKey.W => EditorCommand.CloseTab,
                ConsoleKey.P => EditorCommand.ListTabs,
                ConsoleKey.D1 => EditorCommand.GoPaneNumber,
                ConsoleKey.D2 => EditorCommand.GoPaneNumber,
                ConsoleKey.D3 => EditorCommand.GoPaneNumber,
                ConsoleKey.D4 => EditorCommand.GoPaneNumber,
                ConsoleKey.D5 => EditorCommand.GoPaneNumber,
                ConsoleKey.D6 => EditorCommand.GoPaneNumber,
                ConsoleKey.D7 => EditorCommand.GoPaneNumber,
                ConsoleKey.D8 => EditorCommand.GoPaneNumber,
                ConsoleKey.D9 => EditorCommand.GoPaneNumber,
                ConsoleKey.E => EditorCommand.GoEnd,
                ConsoleKey.Home => EditorCommand.GoDocStart,
                ConsoleKey.End => EditorCommand.GoDocEnd,
                ConsoleKey.LeftArrow => EditorCommand.WordLeft,
                ConsoleKey.RightArrow => EditorCommand.WordRight,
                ConsoleKey.PageDown => EditorCommand.NextTab, // вкладки (WT-friendly)
                ConsoleKey.PageUp => EditorCommand.PrevTab,
                ConsoleKey.Backspace => EditorCommand.DelWordBefore,
                ConsoleKey.Delete => EditorCommand.DelWordAfter,
                _ => EditorCommand.None,
            };
        }

        // Shift+F3 — поиск назад, Shift+F6 — предыдущая панель.
        if (key.Key == ConsoleKey.F3 && (key.Modifiers & ConsoleModifiers.Shift) != 0)
            return EditorCommand.FindPrev;
        if (key.Key == ConsoleKey.F6 && (key.Modifiers & ConsoleModifiers.Shift) != 0)
            return EditorCommand.PrevPane;

        return key.Key switch
        {
            ConsoleKey.LeftArrow => EditorCommand.MoveLeft,
            ConsoleKey.RightArrow => EditorCommand.MoveRight,
            ConsoleKey.UpArrow => EditorCommand.MoveUp,
            ConsoleKey.DownArrow => EditorCommand.MoveDown,
            ConsoleKey.Home => EditorCommand.GoHome,
            ConsoleKey.End => EditorCommand.GoEnd,
            ConsoleKey.PageUp => EditorCommand.PageUp,
            ConsoleKey.PageDown => EditorCommand.PageDown,
            ConsoleKey.F3 => EditorCommand.FindNext,
            ConsoleKey.F1 => EditorCommand.Help,
            ConsoleKey.F9 => EditorCommand.FileFormat,
            ConsoleKey.F6 => EditorCommand.NextPane,
            ConsoleKey.F10 => EditorCommand.ToggleMenu,
            ConsoleKey.Escape => EditorCommand.None,
            ConsoleKey.Enter => EditorCommand.InsertEnter,
            ConsoleKey.Backspace => EditorCommand.InsertBackspace,
            ConsoleKey.Delete => EditorCommand.InsertDelete,
            ConsoleKey.Tab => (key.Modifiers & ConsoleModifiers.Shift) != 0
                ? EditorCommand.Unindent
                : EditorCommand.InsertTab,
            _ => char.IsControl(key.KeyChar) ? EditorCommand.None : EditorCommand.InsertChar,
        };
    }
}
