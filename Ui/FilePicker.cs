namespace TuiEdit;

/// <summary>Specifies the file manager mode.</summary>
internal enum PickerMode
{
    /// <summary>Selects an existing file.</summary>
    Open,
    /// <summary>Selects a save path (with overwrite confirmation).</summary>
    Save,
}

/// <summary>Represents a directory entry: name (without slash), directory flag, and file size (-1 means directory/unknown).</summary>
internal sealed record PickerEntry(string Name, bool IsDir, long Size = -1)
{
    /// <summary>Gets the display name: directories get a slash (except ..).</summary>
    public string DisplayName =>
        IsDir && Name != ".." && !Name.EndsWith('/') && !Name.EndsWith('\\') ? Name + "/" : Name;
}

/// <summary>Specifies the Enter outcome in the manager.</summary>
internal enum PickerEnterResult
{
    /// <summary>Stays put (empty / unresolved).</summary>
    Stayed,
    /// <summary>Navigates to another directory.</summary>
    Navigated,
    /// <summary>Accepts the path.</summary>
    Accepted,
}

/// <summary>
/// Provides the file manager as a modal window:
/// path plus name, a [.., folders/, files] list, Enter to enter,
/// with overwrite confirmed separately.
/// Behaves as a pure model without console access - covered by unit tests.
/// </summary>
internal sealed class FilePickerState
{
    /// <summary>Gets the mode.</summary>
    public PickerMode Mode { get; }

    /// <summary>Gets the current directory; "" means drives (the caller maps the label via Loc).</summary>
    public string CurrentDir { get; private set; }

    /// <summary>Gets or sets a value that indicates whether hidden files are shown.</summary>
    public bool ShowHidden { get; set; } = true;

    /// <summary>Gets the directory entries: .., folders, files.</summary>
    public List<PickerEntry> Entries { get; private set; } = new();

    /// <summary>Gets the selected entry.</summary>
    public int Selected { get; private set; }

    /// <summary>Gets the first visible entry (scroll).</summary>
    public int Top { get; private set; }

    /// <summary>Gets the name field (in Open it also reflects the highlighted entry).</summary>
    public string Name => _name.Text;

    /// <summary>Gets the cursor in the name field.</summary>
    public int NamePos => _name.Pos;

    /// <summary>Gets the selection anchor in the name field (null means no selection).</summary>
    public int? NameAnchor => _name.Anchor;

    /// <summary>Gets a value that indicates whether a non-empty selection exists.</summary>
    public bool HasNameSelection => _name.HasSelection;

    /// <summary>Gets the selection bounds (a==b means none).</summary>
    public void GetNameSelection(out int a, out int b) => _name.GetSelection(out a, out b);

    /// <summary>Clears the selection.</summary>
    public void ClearNameSelection() => _name.ClearSelection();

    private readonly LineField _name = new();

    /// <summary>Stores the name as left by the last sync (distinguishes manual input).</summary>
    private string _syncedName = string.Empty;

    /// <summary>Gets the directory read error (the BadPath code maps via Loc).</summary>
    public string? Error { get; private set; }

    /// <summary>Gets or sets the localized notice key for the hint row (reset by Refresh).</summary>
    public string? NoticeKey { get; set; }

    /// <param name="mode">The mode.</param>
    /// <param name="startDir">The starting directory ("" means drives on Windows).</param>
    /// <param name="initialName">The initial name (pre-filled for Save).</param>
    public FilePickerState(PickerMode mode, string startDir, string initialName)
    {
        Mode = mode;
        CurrentDir = startDir ?? string.Empty;
        _name.Set(initialName ?? string.Empty);
        _name.End(select: false);
        _syncedName = Name;
        Refresh();
    }

    /// <summary>Refreshes the directory: .., folders, files (case-insensitive sort).</summary>
    public void Refresh()
    {
        Error = null;
        NoticeKey = null;
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
                {
                    if (!ShowHidden && FileKinds.IsHidden(d))
                        continue;
                    dirs.Add(new PickerEntry(BaseName(d), true));
                }
                foreach (string f in Directory.GetFiles(CurrentDir))
                {
                    if (!ShowHidden && FileKinds.IsHidden(f))
                        continue;
                    long size = -1;
                    try
                    {
                        size = new FileInfo(f).Length;
                    }
                    catch
                    {
                    }
                    files.Add(new PickerEntry(Path.GetFileName(f), false, size));
                }
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        static int Order(PickerEntry a, PickerEntry b) =>
            NaturalSort.Compare(a.Name, b.Name);
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

    /// <summary>Gets the highlighted entry or null.</summary>
    public PickerEntry? Highlighted() =>
        Entries.Count == 0 ? null : Entries[Math.Clamp(Selected, 0, Entries.Count - 1)];

    /// <summary>
    /// Moves the highlight. Highlighting a file substitutes its name;
    /// highlighting a folder leaves the name alone (typed input is preserved).
    /// </summary>
    public void MoveHighlight(int delta)
    {
        if (Entries.Count == 0)
            return;
        Selected = Math.Clamp(Selected + delta, 0, Entries.Count - 1);
        PickerEntry h = Entries[Selected];
        // Highlighting a file substitutes its name, but never clobbers typed input.
        // Highlighting a folder never touches the name.
        if ((Mode == PickerMode.Open || !h.IsDir) && Name == _syncedName)
            SetName(EntryBaseName(h));
    }

    /// <summary>Moves to the start of the list.</summary>
    public void GotoFirst() => MoveHighlight(-Entries.Count);

    /// <summary>Moves to the end of the list.</summary>
    public void GotoLast() => MoveHighlight(Entries.Count);

    /// <summary>Keeps the selection visible within maxRows rows.</summary>
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
        _name.Set(name);
        _syncedName = name;
    }

    /// <summary>Inserts input into the name field (over the selection).</summary>
    public void InsertName(string text) => _name.Insert(text);

    /// <summary>Deletes with Backspace over text only; does nothing at the field start (up is Alt+Left).</summary>
    public void Backspace() => _name.Backspace();

    /// <summary>Deletes in the name field (selection first).</summary>
    public void DeleteChar() => _name.DeleteChar();

    /// <summary>Deletes the word before/after the cursor (like Ctrl+BS/Del in the editor).</summary>
    public void DeleteNameWord(int dir) => _name.DeleteWord(dir);

    /// <summary>Moves with arrows in the name field (select moves with selection).</summary>
    public void MoveNameCursor(int delta, bool select) => _name.Move(delta, select);

    /// <summary>Moves to the start of the field (select moves with selection).</summary>
    public void HomeName(bool select) => _name.Home(select);

    /// <summary>Moves to the end of the field (select moves with selection).</summary>
    public void EndName(bool select) => _name.End(select);

    /// <summary>Moves by words like Ctrl+arrows in the editor (select moves with selection).</summary>
    public void MoveNameWord(int dir, bool select) => _name.MoveWord(dir, select);

    /// <summary>Moves up: to the parent or to drives (Windows).</summary>
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

    /// <summary>Navigates to a directory ("" means drives).</summary>
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
        _name.ClearSelection(); // Context changed
        Refresh();
    }

    /// <summary>Resolves the full path from a name (absolute stays as-is).</summary>
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
    /// Enters the highlighted directory (for Alt+Right): .. goes up, a folder goes inside,
    /// a file does nothing (unlike Enter, it never accepts a path).
    /// </summary>
    /// <returns><see langword="true"/> if navigation happened; otherwise, <see langword="false"/>.</returns>
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
    /// Handles Enter: navigates into the folder under the cursor (keeps the name);
    /// otherwise resolves the name: navigates into a directory, accepts a file path.
    /// At the drives root Enter navigates to the drive.
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
            SetName(h.Name); // Substitutes the file name
        }
        string? full = ResolveName(Name);
        if (full is null)
            return (PickerEnterResult.Stayed, null);
        if (Directory.Exists(full))
        {
            NavigateTo(full);
            _name.Set(string.Empty);
            return (PickerEnterResult.Navigated, null);
        }
        return (PickerEnterResult.Accepted, full);
    }

    /// <summary>Handles Enter at the drives root: prefers the typed path, otherwise uses the highlight.</summary>
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
        // "C:" is a relative drive path... normalizes to the drive root.
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

    /// <summary>Creates a subfolder from the name: ok / empty / exists / error (the error goes to Error).</summary>
    public string MakeDir(string? name)
    {
        string? full = ResolveName(name ?? Name);
        if (full is null)
            return "empty";
        try
        {
            if (Directory.Exists(full) || File.Exists(full))
                return "exists";
            Directory.CreateDirectory(full);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return "error";
        }
        Refresh();
        return "ok";
    }

    /// <summary>Gets the delete target: the highlighted entry (not .., not the drives root).</summary>
    public string? DeleteTarget()
    {
        if (CurrentDir == "")
            return null;
        PickerEntry? h = Highlighted();
        if (h is null || h.Name == "..")
            return null;
        try
        {
            return Path.Combine(CurrentDir, EntryBaseName(h));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Counts items for confirm (a file counts as 1; stops at 1001).</summary>
    public static int CountItems(string path)
    {
        try
        {
            if (File.Exists(path))
                return 1;
            if (!Directory.Exists(path))
                return 0;
            int n = 0;
            foreach (string _ in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
                if (++n > 1000)
                    return 1001;
            return n;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Deletes a file/directory recursively (refreshes; the error goes to Error).</summary>
    public bool DeletePath(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return false;
        }
        Refresh();
        return true;
    }
}
