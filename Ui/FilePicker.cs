namespace TuiEdit;

/// <summary>Режим файлового менеджера.</summary>
public enum PickerMode
{
    /// <summary>Выбор существующего файла.</summary>
    Open,
    /// <summary>Выбор пути для сохранения (с подтверждением перезаписи).</summary>
    Save,
}

/// <summary>Запись каталога: имя (без слэша) и признак папки.</summary>
public sealed record PickerEntry(string Name, bool IsDir)
{
    /// <summary>Имя для показа: папки со слэшем (кроме ..).</summary>
    public string DisplayName =>
        IsDir && Name != ".." && !Name.EndsWith('/') && !Name.EndsWith('\\') ? Name + "/" : Name;
}

/// <summary>Исход Enter в менеджере.</summary>
public enum PickerEnterResult
{
    /// <summary>Остаёмся (пусто / не разрешилось).</summary>
    Stayed,
    /// <summary>Перешли в другой каталог.</summary>
    Navigated,
    /// <summary>Путь выбран.</summary>
    Accepted,
}

/// <summary>
/// Файловый менеджер модальным окном, по мотивам
/// <c>draw_filepicker.rs</c> из MS Edit (microsoft/edit):
/// путь + имя, список [.., папки/, файлы], вход по Enter,
/// перезапись подтверждается отдельно.
/// Чистая модель без консоли — покрывается unit-тестами.
/// </summary>
public sealed class FilePickerState
{
    /// <summary>Режим.</summary>
    public PickerMode Mode { get; }

    /// <summary>Текущий каталог; "" — диски (подпись маппит вызывающий через Loc).</summary>
    public string CurrentDir { get; private set; }

    /// <summary>Записи каталога: .., папки, файлы.</summary>
    public List<PickerEntry> Entries { get; private set; } = new();

    /// <summary>Выбранная запись.</summary>
    public int Selected { get; private set; }

    /// <summary>Первая видимая запись (скролл).</summary>
    public int Top { get; private set; }

    /// <summary>Поле имени (в Open — и выбор подсвеченного).</summary>
    public string Name { get; private set; }

    /// <summary>Курсор в поле имени.</summary>
    public int NamePos { get; private set; }

    /// <summary>Якорь выделения в поле имени (null — нет выделения).</summary>
    public int? NameAnchor { get; private set; }

    /// <summary>Есть ли невырожденное выделение.</summary>
    public bool HasNameSelection => NameAnchor is int a && a != NamePos;

    /// <summary>Границы выделения (a==b — нет).</summary>
    public void GetNameSelection(out int a, out int b)
    {
        int anchor = NameAnchor ?? NamePos;
        a = Math.Min(anchor, NamePos);
        b = Math.Max(anchor, NamePos);
    }

    /// <summary>Снять выделение.</summary>
    public void ClearNameSelection() => NameAnchor = null;

    /// <summary>Имя как его оставил последний синк (для отличия ручного ввода).</summary>
    private string _syncedName = string.Empty;

    /// <summary>Ошибка чтения каталога (код BadPath маппится через Loc).</summary>
    public string? Error { get; private set; }

    /// <param name="mode">Режим.</param>
    /// <param name="startDir">Стартовый каталог ("" — диски на Windows).</param>
    /// <param name="initialName">Начальное имя (для Save — предзаполнено).</param>
    public FilePickerState(PickerMode mode, string startDir, string initialName)
    {
        Mode = mode;
        CurrentDir = startDir ?? string.Empty;
        Name = initialName ?? string.Empty;
        NamePos = Name.Length;
        _syncedName = Name;
        Refresh();
    }

    /// <summary>Перечитать каталог: .., папки, файлы (сортировка без учёта регистра).</summary>
    public void Refresh()
    {
        Error = null;
        var up = new List<PickerEntry>();
        var dirs = new List<PickerEntry>();
        var files = new List<PickerEntry>();
        try
        {
            if (CurrentDir == "" && OperatingSystem.IsWindows())
            {
                foreach (string drive in Directory.GetLogicalDrives())
                    dirs.Add(new PickerEntry(drive, true));
            }
            else
            {
                if (CanGoUp())
                    up.Add(new PickerEntry("..", true));
                foreach (string d in Directory.GetDirectories(CurrentDir))
                    dirs.Add(new PickerEntry(BaseName(d), true));
                foreach (string f in Directory.GetFiles(CurrentDir))
                    files.Add(new PickerEntry(Path.GetFileName(f), false));
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        static int Order(PickerEntry a, PickerEntry b)
        {
            int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }
        dirs.Sort(Order);
        files.Sort(Order);
        Entries = up.Concat(dirs).Concat(files).ToList();
        Selected = Math.Clamp(Selected, 0, Math.Max(0, Entries.Count - 1));
        Top = Math.Min(Top, Selected);
    }

    private static string BaseName(string dir) =>
        Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private bool CanGoUp() =>
        CurrentDir != "" && (OperatingSystem.IsWindows() || Path.GetDirectoryName(CurrentDir) is not null);

    /// <summary>Подсвеченная запись или null.</summary>
    public PickerEntry? Highlighted() =>
        Entries.Count == 0 ? null : Entries[Math.Clamp(Selected, 0, Entries.Count - 1)];

    /// <summary>
    /// Движение подсветки. Подсветка файла подставляет имя;
    /// подсветка папки имя не трогает (введённое сохраняется).
    /// </summary>
    public void MoveHighlight(int delta)
    {
        if (Entries.Count == 0)
            return;
        Selected = Math.Clamp(Selected + delta, 0, Entries.Count - 1);
        PickerEntry h = Entries[Selected];
        // Подсветка файла подставляет имя, но введённое руками не затираем.
        // Подсветка папки имя не трогает никогда.
        if ((Mode == PickerMode.Open || !h.IsDir) && Name == _syncedName)
            SetName(EntryBaseName(h));
    }

    /// <summary>В начало / конец списка.</summary>
    public void GotoFirst() => MoveHighlight(-Entries.Count);

    /// <summary>В начало / конец списка.</summary>
    public void GotoLast() => MoveHighlight(Entries.Count);

    /// <summary>Поддержать видимость выбранного при maxRows строках.</summary>
    public void EnsureVisible(int maxRows)
    {
        if (maxRows <= 0)
            return;
        if (Selected < Top)
            Top = Selected;
        else if (Selected >= Top + maxRows)
            Top = Selected - maxRows + 1;
    }

    private static string EntryBaseName(PickerEntry e) =>
        e.Name == ".." ? ".." : e.Name.TrimEnd('/', '\\');

    private void SetName(string name)
    {
        Name = name;
        NamePos = Math.Clamp(NamePos, 0, Name.Length);
        NameAnchor = null;
        _syncedName = name;
    }

    /// <summary>Стереть выделение (true если было).</summary>
    private bool DeleteNameSelection()
    {
        if (!HasNameSelection)
            return false;
        GetNameSelection(out int a, out int b);
        Name = Name.Remove(a, b - a);
        NamePos = a;
        NameAnchor = null;
        return true;
    }

    /// <summary>Ввод в поле имени (поверх выделения).</summary>
    public void InsertName(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        DeleteNameSelection();
        Name = Name.Insert(NamePos, text);
        NamePos += text.Length;
    }

    /// <summary>Backspace только по тексту; в начале поля — ничего (вверх — Alt+←).</summary>
    public void Backspace()
    {
        if (DeleteNameSelection())
            return;
        if (NamePos > 0)
        {
            Name = Name.Remove(NamePos - 1, 1);
            NamePos--;
        }
    }

    /// <summary>Delete в поле имени (сначала выделение).</summary>
    public void DeleteChar()
    {
        if (DeleteNameSelection())
            return;
        if (NamePos < Name.Length)
            Name = Name.Remove(NamePos, 1);
    }

    /// <summary>Удаление слова до/после курсора (как Ctrl+BS/Del в редакторе).</summary>
    public void DeleteNameWord(int dir)
    {
        if (DeleteNameSelection())
            return;
        if (dir < 0)
        {
            int to = WordMotion.Backward(Name, NamePos);
            Name = Name.Remove(to, NamePos - to);
            NamePos = to;
        }
        else
        {
            int to = WordMotion.Forward(Name, NamePos);
            Name = Name.Remove(NamePos, to - NamePos);
        }
    }

    /// <summary>Стрелки в поле имени (select — с выделением).</summary>
    public void MoveNameCursor(int delta, bool select) => MoveNamePos(NamePos + delta, select);

    /// <summary>В начало/конец поля (select — с выделением).</summary>
    public void HomeName(bool select) => MoveNamePos(0, select);

    /// <summary>В начало/конец поля (select — с выделением).</summary>
    public void EndName(bool select) => MoveNamePos(Name.Length, select);

    /// <summary>По словам как Ctrl+стрелки в редакторе (select — с выделением).</summary>
    public void MoveNameWord(int dir, bool select) =>
        MoveNamePos(dir < 0 ? WordMotion.Backward(Name, NamePos) : WordMotion.Forward(Name, NamePos), select);

    private void MoveNamePos(int pos, bool select)
    {
        pos = Math.Clamp(pos, 0, Name.Length);
        if (select)
            NameAnchor ??= NamePos; // якорь в точке старта
        else
            NameAnchor = null;
        NamePos = pos;
        if (NameAnchor == NamePos)
            NameAnchor = null; // схлопнулось
    }

    /// <summary>Вверх: родитель или диски (Windows).</summary>
    public void UpDir()
    {
        if (CurrentDir == "")
            return;
        string? parent = null;
        try
        {
            string trimmed = CurrentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            parent = Path.GetDirectoryName(trimmed);
        }
        catch
        {
            return;
        }
        NavigateTo(parent ?? "");
    }

    /// <summary>Перейти в каталог ("" — диски).</summary>
    public void NavigateTo(string dir)
    {
        try
        {
            CurrentDir = dir == "" ? "" : Path.GetFullPath(dir);
        }
        catch
        {
            Error = "BadPath";
            return;
        }
        Selected = 0;
        Top = 0;
        NameAnchor = null; // контекст сменился
        Refresh();
    }

    /// <summary>Полный путь из имени (абсолютное — как есть).</summary>
    public string? ResolveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        try
        {
            name = name.Trim();
            if (Path.IsPathFullyQualified(name))
                return Path.GetFullPath(name);
            if (CurrentDir == "")
                return null;
            return Path.GetFullPath(Path.Combine(CurrentDir, name));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Вход в подсвеченный каталог (для Alt+→): .. — вверх, папка — внутрь,
    /// файл — ничего (в отличие от Enter, не принимает путь).
    /// </summary>
    /// <returns>true если перешли.</returns>
    public bool EnterDir()
    {
        PickerEntry? h = Highlighted();
        if (h is null || !h.IsDir)
            return false;
        if (h.Name == "..")
            UpDir();
        else
            NavigateDirEntry(h);
        return true;
    }

    /// <summary>
    /// Enter: папка под курсором — перейти (имя сохраняется);
    /// иначе — разрешить имя: каталог — перейти, файл — принять путь.
    /// В корне дисков Enter переходит на диск.
    /// </summary>
    public (PickerEnterResult Result, string? Path) Enter()
    {
        if (CurrentDir == "" && OperatingSystem.IsWindows())
            return EnterFromDrives();
        PickerEntry? h = Highlighted();
        if (h is not null && (h.Name == ".." || h.IsDir))
        {
            if (h.Name == "..")
                UpDir();
            else
                NavigateDirEntry(h);
            return (PickerEnterResult.Navigated, null);
        }
        if (string.IsNullOrEmpty(Name))
        {
            if (h is null)
                return (PickerEnterResult.Stayed, null);
            SetName(h.Name); // файл — подставить имя
        }
        string? full = ResolveName(Name);
        if (full is null)
            return (PickerEnterResult.Stayed, null);
        if (Directory.Exists(full))
        {
            NavigateTo(full);
            Name = string.Empty;
            NamePos = 0;
            NameAnchor = null;
            return (PickerEnterResult.Navigated, null);
        }
        return (PickerEnterResult.Accepted, full);
    }

    /// <summary>Enter в корне дисков: введённый путь — в приоритете, иначе — подсветка.</summary>
    private (PickerEnterResult Result, string? Path) EnterFromDrives()
    {
        if (!string.IsNullOrEmpty(Name))
        {
            string? full = ResolveDriveName(Name);
            if (full is not null)
            {
                if (Directory.Exists(full))
                {
                    NavigateTo(full);
                    return (PickerEnterResult.Navigated, null);
                }
                return (PickerEnterResult.Accepted, full);
            }
        }
        PickerEntry? d = Highlighted();
        if (d is null)
            return (PickerEnterResult.Stayed, null);
        NavigateTo(d.Name);
        return (PickerEnterResult.Navigated, null);
    }

    private static string? ResolveDriveName(string name)
    {
        name = name.Trim();
        // "C:" — диск относительно... нормализуем в корень диска.
        if (name.Length == 2 && name[1] == ':' && char.IsAsciiLetter(name[0]))
            return char.ToUpperInvariant(name[0]) + ":\\";
        try
        {
            if (Path.IsPathFullyQualified(name))
                return Path.GetFullPath(name);
        }
        catch
        {
            return null;
        }
        return null;
    }

    private void NavigateDirEntry(PickerEntry entry)
    {
        if (entry.Name == "..")
        {
            UpDir();
            return;
        }
        try
        {
            NavigateTo(CurrentDir == "" ? entry.Name : Path.Combine(CurrentDir, entry.Name));
        }
        catch
        {
            Error = "BadPath";
        }
    }
}
