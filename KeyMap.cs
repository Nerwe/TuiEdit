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
        if ((key.Modifiers & ConsoleModifiers.Alt) != 0)
            return EditorCommand.None; // Alt-комбинации не используем

        if ((key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            return key.Key switch
            {
                ConsoleKey.S => EditorCommand.Save,
                ConsoleKey.O => EditorCommand.SaveAs,
                ConsoleKey.Q => EditorCommand.Quit,
                ConsoleKey.F => EditorCommand.Find,
                ConsoleKey.G => EditorCommand.GoToLine,
                ConsoleKey.K => EditorCommand.CutLine,
                ConsoleKey.U => EditorCommand.Paste,
                ConsoleKey.V => EditorCommand.Paste, // дублируем nano ^U
                ConsoleKey.C => EditorCommand.CopyLine,
                ConsoleKey.Z => EditorCommand.Undo,
                ConsoleKey.Y => EditorCommand.Redo,
                ConsoleKey.A => EditorCommand.GoHome, // как в nano
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
            ConsoleKey.Escape => EditorCommand.None,
            ConsoleKey.Enter => EditorCommand.InsertEnter,
            ConsoleKey.Backspace => EditorCommand.InsertBackspace,
            ConsoleKey.Delete => EditorCommand.InsertDelete,
            ConsoleKey.Tab => EditorCommand.InsertTab,
            _ => char.IsControl(key.KeyChar) ? EditorCommand.None : EditorCommand.InsertChar,
        };
    }
}
