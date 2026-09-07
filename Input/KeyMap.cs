namespace TuiEdit;

/// <summary>
/// Преобразует <see cref="System.ConsoleKeyInfo"/> в <see cref="EditorCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Сохранение — Ctrl+S, сохранить как — Ctrl+Shift+S (как в MS Edit).
/// Ctrl+S доходит до приложения благодаря сырому режиму ввода
/// (<see cref="Terminal.TryEnableRawInput"/>): без него conhost перехватывает
/// его как паузу вывода XOFF (см. microsoft/terminal#809).
/// </para>
/// <para>Метод чистый (без обращений к консоли) — тестируется напрямую.</para>
/// </remarks>
public static class KeyMap
{
    /// <summary>
    /// Возвращает команду для нажатия клавиши.
    /// </summary>
    /// <param name="key">Информация о нажатии (см. <see cref="System.ConsoleKeyInfo"/>).</param>
    /// <returns>Команда редактора или <see cref="EditorCommand.None"/>.</returns>
    public static EditorCommand Map(ConsoleKeyInfo key)
    {
        bool alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;

        // Alt+буква открывает меню (как в MS Edit: Alt+F/E/V/H).
        // Alt+↑/↓ двигает строку/блок (как move line в VS Code).
        // Alt+N/Z — тогглы номеров строк и переноса (Z как в VS Code).
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
                _ => EditorCommand.None,
            };
        }
        if (alt)
            return EditorCommand.None; // прочие Alt-комбинации не используем

        if ((key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            // Ctrl+Shift+S — сохранить как (как в MS Edit); остальным Shift не важен.
            if (key.Key == ConsoleKey.S && (key.Modifiers & ConsoleModifiers.Shift) != 0)
                return EditorCommand.SaveAs;
            return key.Key switch
            {
                ConsoleKey.S => EditorCommand.Save,
                ConsoleKey.O => EditorCommand.SaveAs,
                ConsoleKey.Q => EditorCommand.Quit,
                ConsoleKey.N => EditorCommand.NewFile,
                ConsoleKey.F => EditorCommand.Find,
                ConsoleKey.H => EditorCommand.Replace, // как замена в MS Edit
                ConsoleKey.G => EditorCommand.GoToLine,
                ConsoleKey.K => EditorCommand.CutLine,
                ConsoleKey.U => EditorCommand.Paste,
                ConsoleKey.V => EditorCommand.Paste, // дублируем nano ^U
                ConsoleKey.C => EditorCommand.CopyLine,
                ConsoleKey.D => EditorCommand.DuplicateLine,
                ConsoleKey.Z => EditorCommand.Undo,
                ConsoleKey.Y => EditorCommand.Redo,
                ConsoleKey.A => EditorCommand.SelectAll, // как в MS Edit (Home — клавишей Home)
                ConsoleKey.E => EditorCommand.GoEnd,
                ConsoleKey.Home => EditorCommand.GoDocStart,
                ConsoleKey.End => EditorCommand.GoDocEnd,
                ConsoleKey.LeftArrow => EditorCommand.WordLeft,
                ConsoleKey.RightArrow => EditorCommand.WordRight,
                ConsoleKey.Backspace => EditorCommand.DelWordBefore,
                ConsoleKey.Delete => EditorCommand.DelWordAfter,
                _ => EditorCommand.None,
            };
        }

        // Shift+F3 — поиск назад (модификатор виден в ConsoleKeyInfo).
        if (key.Key == ConsoleKey.F3 && (key.Modifiers & ConsoleModifiers.Shift) != 0)
            return EditorCommand.FindPrev;

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
            ConsoleKey.F10 => EditorCommand.ToggleMenu, // фокус на меню-бар, как в MS Edit
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
