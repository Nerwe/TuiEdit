namespace TuiEdit;

/// <summary>Classified directory entry: name plus kind flags for display.</summary>
internal sealed record FileKind(string Name, bool IsDir, bool IsHidden, bool IsExe);

/// <summary>
/// File kind classification shared by the sidebar, the manager, and the tree:
/// hidden means a leading dot or the Hidden attribute, executable means
/// a file with a known extension. Never throws.
/// </summary>
internal static class FileKinds
{
    private static readonly HashSet<string> ExeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".com", ".ps1", ".sh" };

    /// <summary>Classifies an entry by its full path and directory flag.</summary>
    /// <param name="fullPath">The full entry path.</param>
    /// <param name="isDir">Whether the entry is a directory.</param>
    public static FileKind Classify(string fullPath, bool isDir)
    {
        string name = string.Empty;
        try
        {
            name = Path.GetFileName(fullPath);
        }
        catch
        {
        }
        bool exe = false;
        try
        {
            exe = !isDir && ExeExtensions.Contains(Path.GetExtension(name));
        }
        catch
        {
        }
        return new FileKind(name, isDir, IsHidden(fullPath), exe);
    }

    /// <summary>Whether the entry is hidden (leading dot or Hidden attribute).</summary>
    /// <param name="fullPath">The full entry path.</param>
    public static bool IsHidden(string fullPath)
    {
        try
        {
            if (Path.GetFileName(fullPath).StartsWith('.'))
                return true;
        }
        catch
        {
            return false;
        }
        try
        {
            return (File.GetAttributes(fullPath) & FileAttributes.Hidden) != 0;
        }
        catch
        {
            return false;
        }
    }
}
