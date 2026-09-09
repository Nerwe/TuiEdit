using System.Globalization;

namespace TuiEdit;

/// <summary>
/// Stores versioned copies of saved files in a central folder (keeps the project clean).
/// Names files sha1(path)_timestamp.bak; keeps up to 5 recent copies per file and prunes copies older than 7 days.
/// </summary>
internal sealed class BackupStore(string dir)
{
    public const int MaxPerFile = 5;

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    public string Dir { get; } = dir;

    // Uses a monotonic counter in the name: a name freed by rotation must not be reused
    // within the same second, otherwise rotation would delete the fresh copy as the "oldest".
    private static long _seq;

    public static string DefaultDir(string settingsPath)
    {
        try
        {
            string? d = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(d))
                return Path.Combine(d, "backups");
        }
        catch
        {
        }
        return Path.Combine(Path.GetTempPath(), "TuiEdit", "backups");
    }

    /// <summary>Saves a copy of the file bytes before overwriting plus rotation. Swallows errors silently.</summary>
    public void Write(string target, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string key = DraftStore.KeyFor(target);
            string stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            long n = Interlocked.Increment(ref _seq);
            string path = Path.Combine(Dir, $"{key}_{stamp}_{n:D10}.bak");
            int dup = 0;
            while (File.Exists(path))
                path = Path.Combine(Dir, $"{key}_{stamp}_{n:D10}_{++dup:D4}.bak");
            File.WriteAllBytes(path, bytes);
            Rotate(key);
        }
        catch
        {
        }
    }

    /// <summary>Discards copies older than the limit (for all files). Swallows errors silently.</summary>
    public void PruneAll()
    {
        string[] files;
        try
        {
            if (!Directory.Exists(Dir))
                return;
            files = Directory.GetFiles(Dir, "*.bak");
        }
        catch
        {
            return;
        }
        DateTime cutoff = DateTime.Now - MaxAge;
        foreach (string f in files)
        {
            try
            {
                if (File.GetLastWriteTime(f) < cutoff)
                    File.Delete(f);
            }
            catch
            {
            }
        }
    }

    private void Rotate(string key)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(Dir, key + "_*.bak");
        }
        catch
        {
            return;
        }
        Array.Sort(files, StringComparer.Ordinal);
        DateTime cutoff = DateTime.Now - MaxAge;
        var keep = new List<string>(files.Length);
        foreach (string f in files)
        {
            try
            {
                if (File.GetLastWriteTime(f) < cutoff)
                    File.Delete(f);
                else
                    keep.Add(f);
            }
            catch
            {
            }
        }
        for (int i = 0; i + MaxPerFile < keep.Count; i++)
        {
            try
            {
                File.Delete(keep[i]);
            }
            catch
            {
            }
        }
    }
}
