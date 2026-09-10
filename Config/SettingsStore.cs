using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TuiEdit;

/// <summary>
/// Stores settings as JSON
/// (<c>%APPDATA%\TuiEdit\settings.json</c>, on Linux — <c>~/.config/TuiEdit</c>).
/// </summary>
internal sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public string Path { get; } = path;

    public static string DefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TuiEdit", "settings.json");

    /// <summary>
    /// Resolves the effective path: settings.json next to the exe (portable mode), otherwise the default.
    /// </summary>
    public static string ResolvePath()
    {
        try
        {
            string local = System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (File.Exists(local))
                return local;
        }
        catch
        {
        }
        return DefaultPath();
    }

    /// <summary>
    /// Loads settings (missing files yield defaults; corrupt files are preserved
    /// next to the original with a .corrupt suffix, then defaults load).
    /// Never throws.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                string text = File.ReadAllText(Path);
                try
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(text, ReadOptions);
                    if (s is not null)
                    {
                        s.Normalize();
                        return s;
                    }
                }
                catch
                {
                    PreserveCorrupt(text);
                }
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    /// <summary>Saves settings atomically (temp file plus rename). Never throws.</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, Path, overwrite: true);
        }
        catch
        {
        }
    }

    private void PreserveCorrupt(string text)
    {
        try
        {
            File.WriteAllText(Path + ".corrupt", text);
        }
        catch
        {
        }
    }
}

/// <summary>Represents a draft of an unsaved document for recovery after a crash.</summary>
internal sealed record DocDraft(string? File, List<string> Lines, int Row, int Col, DateTime SavedAt);

internal sealed class DraftStore(string dir)
{
    public string Dir { get; } = dir;

    public static string DefaultDir() => System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "TuiEdit", "drafts");

    /// <summary>Computes the draft key: sha1 of the path ("untitled" for unnamed documents).</summary>
    public static string KeyFor(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return "untitled";
        try
        {
            // CA5350: SHA1 here is not cryptography, but a short file-name key for drafts/backups;
            // changing the algorithm would orphan existing files.
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(file)));
#pragma warning restore CA5350
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return "untitled";
        }
    }

    public void Write(string? file, IList<string> lines, int row, int col) =>
        WriteKey(KeyFor(file), file, lines, row, col);

    /// <summary>
    /// Writes a draft under an explicit key (untitled tabs use per-tab keys so
    /// they never overwrite each other). Atomic: temp file plus rename, so a
    /// crash mid-write keeps the previous draft intact.
    /// </summary>
    /// <param name="key">The draft file key (no extension).</param>
    /// <param name="file">The documented file (null for untitled).</param>
    /// <param name="lines">The content snapshot.</param>
    /// <param name="row">The cursor row.</param>
    /// <param name="col">The cursor column.</param>
    public void WriteKey(string key, string? file, IList<string> lines, int row, int col)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        Directory.CreateDirectory(Dir);
        var draft = new DocDraft(file, new List<string>(lines), row, col, DateTime.UtcNow);
        string tmp = System.IO.Path.Combine(Dir, Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(draft));
        File.Move(tmp, System.IO.Path.Combine(Dir, key + ".json"), overwrite: true);
        PruneTmp();
    }

    private void PruneTmp()
    {
        try
        {
            DateTime limit = DateTime.UtcNow.AddDays(-1);
            foreach (string f in Directory.GetFiles(Dir, "*.tmp"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(f) < limit)
                        File.Delete(f);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>Reads all readable drafts (skips corrupt ones).</summary>
    public List<(string key, DocDraft draft)> ReadAll()
    {
        var list = new List<(string, DocDraft)>();
        string[] files;
        try
        {
            files = Directory.GetFiles(Dir, "*.json");
        }
        catch
        {
            return list;
        }
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string f in files)
        {
            try
            {
                var d = JsonSerializer.Deserialize<DocDraft>(File.ReadAllText(f));
                if (d is not null && d.Lines is not null)
                    list.Add((System.IO.Path.GetFileNameWithoutExtension(f), d));
            }
            catch
            {
            }
        }
        return list;
    }

    public void Delete(string? file) => DeleteKey(KeyFor(file));

    public void DeleteKey(string key)
    {
        try
        {
            File.Delete(System.IO.Path.Combine(Dir, key + ".json"));
        }
        catch
        {
        }
    }
}
