namespace TuiEdit;

/// <summary>File operation failure kind (the UI maps it to a message).</summary>
internal enum FileOpError
{
    /// <summary>No failure.</summary>
    None,
    /// <summary>Target already exists.</summary>
    AlreadyExists,
    /// <summary>Source is missing.</summary>
    NotFound,
    /// <summary>Name or path is not usable.</summary>
    InvalidName,
    /// <summary>OS refused (permissions, lock).</summary>
    AccessDenied,
    /// <summary>Anything else.</summary>
    Unknown,
}

/// <summary>File operation outcome: success flag, error kind, resulting path, item count.</summary>
internal sealed record FileOpResult(bool Ok, FileOpError Error, string? Path, int Items);

/// <summary>
/// Pure file operations for the sidebar (and anywhere else): create, rename,
/// recursive delete with counting. Never throws — failures come back as
/// <see cref="FileOpError"/> for the UI to phrase.
/// </summary>
internal static class FileOps
{
    /// <summary>Cap for recursive counting (huge trees degrade gracefully).</summary>
    private const int MaxCount = 100000;

    /// <summary>Creates an empty file (parents included); returns the full path.</summary>
    /// <param name="path">The desired file path.</param>
    public static FileOpResult CreateFile(string path)
    {
        string? full = FullPath(path);
        if (full is null)
            return Fail(FileOpError.InvalidName);
        try
        {
            if (File.Exists(full) || Directory.Exists(full))
                return Fail(FileOpError.AlreadyExists);
            string? dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir))
                return Fail(FileOpError.InvalidName);
            Directory.CreateDirectory(dir);
            using (File.Create(full))
            {
            }
            return new FileOpResult(true, FileOpError.None, full, 1);
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(FileOpError.AccessDenied);
        }
        catch
        {
            return Fail(FileOpError.Unknown);
        }
    }

    /// <summary>Creates a directory with parents (mkdir -p); returns the full path.</summary>
    /// <param name="path">The desired directory path.</param>
    public static FileOpResult CreateDirectory(string path)
    {
        string? full = FullPath(path);
        if (full is null)
            return Fail(FileOpError.InvalidName);
        try
        {
            if (Directory.Exists(full))
                return Fail(FileOpError.AlreadyExists);
            if (File.Exists(full))
                return Fail(FileOpError.AlreadyExists);
            Directory.CreateDirectory(full);
            return new FileOpResult(true, FileOpError.None, full, 1);
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(FileOpError.AccessDenied);
        }
        catch
        {
            return Fail(FileOpError.Unknown);
        }
    }

    /// <summary>Renames within the same directory; returns the new full path.</summary>
    /// <param name="oldPath">The existing full path.</param>
    /// <param name="newName">The new bare name (no separators).</param>
    public static FileOpResult Rename(string oldPath, string newName)
    {
        string? oldFull = FullPath(oldPath);
        if (oldFull is null)
            return Fail(FileOpError.InvalidName);
        if (!ValidName(newName))
            return Fail(FileOpError.InvalidName);
        try
        {
            bool wasDir = Directory.Exists(oldFull);
            if (!wasDir && !File.Exists(oldFull))
                return Fail(FileOpError.NotFound);
            string? dir = Path.GetDirectoryName(oldFull);
            if (string.IsNullOrEmpty(dir))
                return Fail(FileOpError.InvalidName);
            string target = Path.Combine(dir, newName.Trim());
            bool same = target.Equals(oldFull, StringComparison.OrdinalIgnoreCase);
            if (!same && (File.Exists(target) || Directory.Exists(target)))
                return Fail(FileOpError.AlreadyExists);
            if (wasDir)
                Directory.Move(oldFull, target);
            else
                File.Move(oldFull, target);
            return new FileOpResult(true, FileOpError.None, target, 1);
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(FileOpError.AccessDenied);
        }
        catch
        {
            return Fail(FileOpError.Unknown);
        }
    }

    /// <summary>Deletes a file or a directory tree; counts items first.</summary>
    /// <param name="path">The full path to delete.</param>
    public static FileOpResult Delete(string path)
    {
        string? full = FullPath(path);
        if (full is null)
            return Fail(FileOpError.InvalidName);
        try
        {
            if (File.Exists(full))
            {
                File.Delete(full);
                return new FileOpResult(true, FileOpError.None, full, 1);
            }
            if (!Directory.Exists(full))
                return Fail(FileOpError.NotFound);
            int items = CountItems(full);
            Directory.Delete(full, recursive: true);
            return new FileOpResult(true, FileOpError.None, full, items);
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(FileOpError.AccessDenied);
        }
        catch
        {
            return Fail(FileOpError.Unknown);
        }
    }

    /// <summary>Counts entries for a delete confirmation (files count 1, missing 0).</summary>
    /// <param name="path">The full path to count.</param>
    public static int CountItems(string path)
    {
        string? full = FullPath(path);
        if (full is null)
            return 0;
        try
        {
            if (File.Exists(full))
                return 1;
            if (!Directory.Exists(full))
                return 0;
            int count = 0;
            var dirs = new Stack<string>();
            dirs.Push(full);
            while (dirs.Count > 0 && count < MaxCount)
            {
                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(dirs.Pop());
                }
                catch
                {
                    continue;
                }
                foreach (string e in entries)
                {
                    count++;
                    if (count >= MaxCount)
                        break;
                    try
                    {
                        if (Directory.Exists(e))
                            dirs.Push(e);
                    }
                    catch
                    {
                    }
                }
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Whether a rename/create name is a usable bare name.</summary>
    /// <param name="name">The candidate name.</param>
    public static bool ValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        string trimmed = name.Trim();
        if (trimmed is "." or "..")
            return false;
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        return trimmed.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
    }

    private static string? FullPath(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static FileOpResult Fail(FileOpError error) =>
        new(false, error, null, 0);
}
