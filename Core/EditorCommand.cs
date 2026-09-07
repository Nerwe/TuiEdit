namespace TuiEdit;

/// <summary>
/// Команда редактора, полученная из нажатия клавиши или меню.
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
    /// <summary>Недавние файлы (только из меню).</summary>
    OpenRecent,
    /// <summary>О программе (только из меню).</summary>
    About,
    /// <summary>Справка по клавишам (F1).</summary>
    Help,
    /// <summary>Настройки (только из меню).</summary>
    Settings,
    /// <summary>Показать/скрыть меню-бар (F10).</summary>
    ToggleMenu,
    /// <summary>Раскрыть конкретное меню (Alt+буква).</summary>
    OpenMenuFile, OpenMenuEdit, OpenMenuHelp,
    /// <summary>Поиск.</summary>
    Find,
    /// <summary>Следующее вхождение поиска (F3).</summary>
    FindNext,
    /// <summary>Предыдущее вхождение поиска (Shift+F3).</summary>
    FindPrev,
    /// <summary>Замена (^H).</summary>
    Replace,
    /// <summary>Переход к строке.</summary>
    GoToLine,
    /// <summary>Вырезать/копировать/вставить строку.</summary>
    CutLine, CopyLine, Paste,
    /// <summary>Дублировать строку/блок ниже (^D).</summary>
    DuplicateLine,
    /// <summary>Переместить строку/блок вверх/вниз (Alt+↑/↓).</summary>
    MoveLineUp, MoveLineDown,
    /// <summary>Отмена/возврат правки.</summary>
    Undo, Redo,
    /// <summary>Вставка: Enter, Backspace, Delete, Tab, печатный символ.</summary>
    InsertEnter, InsertBackspace, InsertDelete, InsertTab, InsertChar,
    /// <summary>Убрать отступ (Shift+Tab).</summary>
    Unindent,
    /// <summary>Выделить всё (Ctrl+A).</summary>
    SelectAll,
    /// <summary>Показать/скрыть номера строк (Alt+N).</summary>
    ToggleLineNumbers,
    /// <summary>Вкл/выкл мягкий перенос строк (Alt+Z, как в VS Code).</summary>
    ToggleWrap,
}
