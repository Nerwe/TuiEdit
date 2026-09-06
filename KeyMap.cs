namespace TuiEdit;

/// <summary>
/// Команда редактора, полученная из нажатия клавиши.
/// Чистая модель без зависимостей от <see cref="System.Console"/> — покрывается unit-тестами.
/// </summary>
public enum EditorCommand
{
    /// <summary>Нажатие не привязано ни к какому действию.</summary>
    None,
    /// <summary>Движение курсора.</summary>
    MoveLeft, MoveRight, MoveUp, MoveDown,
    /// <summary>Переход в начало/конец строки.</summary>
    GoHome, GoEnd,
    /// <summary>Переход в начало/конец документа.</summary>
    GoDocStart, GoDocEnd,
    /// <summary>Пролистывание на страницу.</summary>
    PageUp, PageDown,
    /// <summary>Движение по словам (Ctrl+стрелки).</summary>
    WordLeft, WordRight,
    /// <summary>Удаление слова (Ctrl+Backspace/Delete).</summary>
    DelWordBefore, DelWordAfter,
    /// <summary>
    /// Сохранить. Если файл ещё не назван — редактор должен запросить имя
    /// (то же поведение, что и <see cref="SaveAs"/>).
    /// </summary>
    Save,
    /// <summary>Сохранить как (запросить имя всегда).</summary>
    SaveAs,
    /// <summary>Выход (с подтверждением при несохранённых изменениях).</summary>
    Quit,
    /// <summary>Новый документ (с проверкой несохранённых изменений).</summary>
    NewFile,
    /// <summary>Открыть файл (только из меню, своей клавиши нет).</summary>
    OpenFile,
    /// <summary>О программе (только из меню).</summary>
    About,
    /// <summary>Показать/скрыть меню-бар (F10).</summary>
    ToggleMenu,
    /// <summary>Раскрыть конкретное меню (Alt+буква).</summary>
    OpenMenuFile, OpenMenuEdit, OpenMenuHelp,
    /// <summary>Поиск.</summary>
    Find,
    /// <summary>Следующее вхождение поиска (F3).</summary>
    FindNext,
    /// <summary>Переход к строке.</summary>
    GoToLine,
    /// <summary>Вырезать/копировать/вставить строку.</summary>
    CutLine, CopyLine, Paste,
    /// <summary>Отмена/возврат правки.</summary>
    Undo, Redo,
    /// <summary>Вставка: Enter, Backspace, Delete, Tab, печатный символ.</summary>
    InsertEnter, InsertBackspace, InsertDelete, InsertTab, InsertChar,
    /// <summary>Убрать отступ (Shift+Tab).</summary>
    Unindent,
    /// <summary>Выделить всё (Ctrl+A).</summary>
    SelectAll,
}

/// <summary>
/// Преобразует <see cref="System.ConsoleKeyInfo"/> в <see cref="EditorCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// F2 дублирует Ctrl+S как команда сохранения: в консолях Windows Ctrl+S
/// обрабатывается хостом как XOFF (пауза вывода, см. microsoft/terminal#809)
/// и может не доходить до приложения, а F2 доходит всегда.
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
        if (alt && !ctrl)
        {
            return key.Key switch
            {
                ConsoleKey.F => EditorCommand.OpenMenuFile,
                ConsoleKey.E => EditorCommand.OpenMenuEdit,
                ConsoleKey.H => EditorCommand.OpenMenuHelp,
                _ => EditorCommand.None,
            };
        }
        if (alt)
            return EditorCommand.None; // прочие Alt-комбинации не используем

        if ((key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            return key.Key switch
            {
                ConsoleKey.S => EditorCommand.Save,
                ConsoleKey.O => EditorCommand.SaveAs,
                ConsoleKey.Q => EditorCommand.Quit,
                ConsoleKey.N => EditorCommand.NewFile,
                ConsoleKey.F => EditorCommand.Find,
                ConsoleKey.G => EditorCommand.GoToLine,
                ConsoleKey.K => EditorCommand.CutLine,
                ConsoleKey.U => EditorCommand.Paste,
                ConsoleKey.V => EditorCommand.Paste, // дублируем nano ^U
                ConsoleKey.C => EditorCommand.CopyLine,
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
            ConsoleKey.F2 => EditorCommand.Save, // дубль ^S, не перехватывается терминалом
            ConsoleKey.F3 => EditorCommand.FindNext,
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
