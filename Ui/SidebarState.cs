namespace TuiEdit;

internal sealed record SidebarEntry(string Name, bool IsDir, bool IsHidden = false, bool IsExe = false);

/// <summary>Provides the file sidebar (fixed <see cref="Width"/> width): a flat list of the current folder (folders first, ".." goes up), highlight, scroll.</summary>
internal sealed class SidebarState
{
    public const int Width = 24;

    /// <summary>Gets the shown folder (always full).</summary>
    public string CurrentDir { get; private set; } = string.Empty;

    public List<SidebarEntry> Entries { get; } = new();

    public int Selected { get; private set; }

    /// <summary>Gets the first visible row (scroll).</summary>
    public int Top { get; private set; }

    public SidebarState(string root)
    {
        NavigateTo(root);
    }

    /// <summary>Navigates to a folder (a broken path yields an empty list, silently).</summary>
    public void NavigateTo(string dir)
    {
        Entries.Clear();
        Selected = 0;
        Top = 0;
        string full;
        try
        {
            full = Path.GetFullPath(dir);
        }
        catch
        {
            return;
        }
        CurrentDir = full;
        string? parent = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar));
        if (parent is not null)
            Entries.Add(new SidebarEntry("..", true));
        try
        {
            var dirs = new List<string>();
            var files = new List<string>();
            foreach (string e in Directory.EnumerateFileSystemEntries(full))
            {
                if (Directory.Exists(e)) dirs.Add(e);
                else files.Add(e);
            }
            dirs.Sort(NaturalSort.Comparer);
            files.Sort(NaturalSort.Comparer);
            foreach (string d in dirs) Entries.Add(Classify(d, isDir: true));
            foreach (string f in files) Entries.Add(Classify(f, isDir: false));
        }
        catch
        {
        }
    }

    private static readonly HashSet<string> ExeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".com", ".ps1", ".sh" };

    /// <summary>Classifies an entry: hidden means a leading dot or the Hidden attribute, executable means a file with a known extension.</summary>
    internal static SidebarEntry Classify(string fullPath, bool isDir)
    {
        string name = Path.GetFileName(fullPath);
        bool exe = !isDir && ExeExtensions.Contains(Path.GetExtension(name));
        return new SidebarEntry(name, isDir, IsHidden(fullPath), exe);
    }

    internal static bool IsHidden(string fullPath)
    {
        if (Path.GetFileName(fullPath).StartsWith('.'))
            return true;
        try
        {
            return (File.GetAttributes(fullPath) & FileAttributes.Hidden) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Moves the highlight (keeps the visible window via visCount).</summary>
    public void MoveHighlight(int d, int visCount)
    {
        if (Entries.Count == 0)
            return;
        Selected = Math.Clamp(Selected + d, 0, Entries.Count - 1);
        visCount = Math.Max(1, visCount);
        if (Selected < Top)
            Top = Selected;
        else if (Selected >= Top + visCount)
            Top = Selected - visCount + 1;
    }

    /// <summary>Gets the full path of the highlighted row (null when empty).</summary>
    public string? SelectedPath =>
        Entries.Count == 0 ? null : Path.Combine(CurrentDir, Entries[Selected].Name);

    public bool SelectedIsDir => Entries.Count > 0 && Entries[Selected].IsDir;

    /// <summary>Handles Enter: enters a folder; returns <see langword="true"/> on a folder, otherwise <see langword="false"/> on a file (the editor opens it).</summary>
    public bool EnterSelected()
    {
        string? path = SelectedPath;
        if (path is null || !SelectedIsDir)
            return false;
        NavigateTo(path);
        return true;
    }
}
