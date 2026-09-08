namespace TuiEdit;

/// <summary>Команда редактора, полученная из нажатия клавиши или меню.</summary>
public enum EditorCommand
{
    None,
    MoveLeft, MoveRight, MoveUp, MoveDown,
    GoHome, GoEnd,
    GoDocStart, GoDocEnd,
    PageUp, PageDown,
    WordLeft, WordRight,
    DelWordBefore, DelWordAfter,
    /// <summary>
    /// Сохранить. Если файл ещё не назван — редактор должен запросить имя
    /// (то же поведение, что и <see cref="SaveAs"/>).
    /// </summary>
    Save,
    SaveAs,
    SaveAll,
    FileFormat,
    TrimTrailing,
    /// <summary>Выход (с подтверждением при несохранённых изменениях).</summary>
    Quit,
    NewFile,
    OpenFile,
    OpenRecent,
    About,
    Help,
    Settings,
    ToggleMenu,
    OpenMenuFile, OpenMenuEdit, OpenMenuHelp,
    Find,
    FindNext,
    FindPrev,
    Replace,
    GoToLine,
    CutLine, CopyLine, Paste,
    DuplicateLine,
    ToggleComment,
    GoBracketMatch,
    MoveLineUp, MoveLineDown,
    Undo, Redo,
    InsertEnter, InsertBackspace, InsertDelete, InsertTab, InsertChar,
    Unindent,
    SelectAll,
    ToggleLineNumbers,
    ToggleWrap,
    ToggleSidebar,
    NewTab,
    /// <summary>Закрыть вкладку (грязную — через диалог).</summary>
    CloseTab,
    /// <summary>Следующая/предыдущая вкладка (Ctrl+Tab там, где терминал пропускает).</summary>
    NextTab, PrevTab,
    GoTabNumber,
    ListTabs,
    SplitPane,
    NextPane, PrevPane,
    GoPaneNumber,
}
