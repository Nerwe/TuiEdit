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

    /// <summary>Ошибка чтения каталога (null — нет).</summary>
    public string? Error { get; private set; }

    /// <summary>Ожидание подтверждения перезаписи.</summary>
    public bool OverwritePending { get; private set; }

    /// <summary>Путь, перезапись которого подтверждается.</summary>
    public string OverwritePath { get; private set; } = string.Empty;

    /// <param name="mode">Режим.</param>
    /// <param name="startDir">Стартовый каталог ("" — диски на Windows).</param>
    /// <param name="initialName">Начальное имя (для Save — предзаполнено).</param>
    public FilePickerState(PickerMode mode, string startDir, string initialName)
    {
        Mode = mode;
        CurrentDir = startDir ?? string.Empty;
        Name = initialName ?? string.Empty;
        NamePos = Name.Length;
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

    /// <summary>Движение подсветки (в Open — подсветка подставляет имя).</summary>
    public void MoveHighlight(int delta)
    {
        if (Entries.Count == 0)
            return;
        Selected = Math.Clamp(Selected + delta, 0, Entries.Count - 1);
        if (Mode == PickerMode.Open)
            SetName(EntryBaseName(Highlighted()!));
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
    }

    /// <summary>Ввод в поле имени.</summary>
    public void InsertName(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        Name = Name.Insert(NamePos, text);
        NamePos += text.Length;
    }

    /// <summary>Backspace: стирает символ или идёт вверх при пустом имени.</summary>
    public void Backspace()
    {
        if (Name.Length > 0)
        {
            if (NamePos > 0)
            {
                Name = Name.Remove(NamePos - 1, 1);
                NamePos--;
            }
            return;
        }
        UpDir();
    }

    /// <summary>Delete в поле имени.</summary>
    public void DeleteChar()
    {
        if (NamePos < Name.Length)
            Name = Name.Remove(NamePos, 1);
    }

    /// <summary>Стрелки в поле имени.</summary>
    public void MoveNameCursor(int delta) => NamePos = Math.Clamp(NamePos + delta, 0, Name.Length);

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
    /// Enter: пустое имя + подсветка-файл — подставить; каталог — перейти;
    /// файл — принять путь. В корне дисков Enter переходит на диск.
    /// </summary>
    public (PickerEnterResult Result, string? Path) Enter()
    {
        if (CurrentDir == "" && OperatingSystem.IsWindows())
            return EnterFromDrives();
        if (string.IsNullOrEmpty(Name))
        {
            PickerEntry? h = Highlighted();
            if (h is null || h.Name == ".." || !h.IsDir && Mode == PickerMode.Save)
            {
                if (h?.Name == "..")
                {
                    UpDir();
                    return (PickerEnterResult.Navigated, null);
                }
                return (PickerEnterResult.Stayed, null);
            }
            if (h.IsDir)
            {
                NavigateDirEntry(h);
                return (PickerEnterResult.Navigated, null);
            }
            SetName(h.Name);
        }
        string? full = ResolveName(Name);
        if (full is null)
            return (PickerEnterResult.Stayed, null);
        if (Directory.Exists(full))
        {
            NavigateTo(full);
            Name = string.Empty;
            NamePos = 0;
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

    /// <summary>Начать подтверждение перезаписи.</summary>
    public void AskOverwrite(string path)
    {
        OverwritePending = true;
        OverwritePath = path;
    }

    /// <summary>Вернуться к обзору без подтверждения.</summary>
    public void DismissOverwrite()
    {
        OverwritePending = false;
        OverwritePath = string.Empty;
    }
}
