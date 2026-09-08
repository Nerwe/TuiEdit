namespace TuiEdit;

public sealed record SidebarEntry(string Name, bool IsDir, bool IsHidden = false, bool IsExe = false);

/// <summary>Сайдбар файлов (фиксированная ширина <see cref="Width"/>): плоский список текущей папки (папки первыми, «..» — наверх), подсветка, скролл.</summary>
public sealed class SidebarState
{
    public const int Width = 24;

    /// <summary>Показанная папка (всегда полная).</summary>
    public string CurrentDir { get; private set; } = string.Empty;

    public List<SidebarEntry> Entries { get; } = new();

    public int Selected { get; private set; }

    /// <summary>Первая видимая строка (скролл).</summary>
    public int Top { get; private set; }

    public SidebarState(string root)
    {
        NavigateTo(root);
    }

    /// <summary>Перейти в папку (битая — пустой список, молча).</summary>
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
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string d in dirs) Entries.Add(Classify(d, isDir: true));
            foreach (string f in files) Entries.Add(Classify(f, isDir: false));
        }
        catch
        {
        }
    }

    private static readonly HashSet<string> ExeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".com", ".ps1", ".sh" };

    /// <summary>Классификация записи: скрытая — точка в начале или атрибут Hidden, исполняемая — файл с известным расширением.</summary>
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

    /// <summary>Двинуть подсветку (видимое окно держим через visCount).</summary>
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

    /// <summary>Полный путь подсвеченной строки (null — пусто).</summary>
    public string? SelectedPath =>
        Entries.Count == 0 ? null : Path.Combine(CurrentDir, Entries[Selected].Name);

    public bool SelectedIsDir => Entries.Count > 0 && Entries[Selected].IsDir;

    /// <summary>Enter: по папке — зайти, по файлу — false (открывает редактор).</summary>
    public bool EnterSelected()
    {
        string? path = SelectedPath;
        if (path is null || !SelectedIsDir)
            return false;
        NavigateTo(path);
        return true;
    }
}
