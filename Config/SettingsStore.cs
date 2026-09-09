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

    /// <summary>Loads settings (missing/corrupt files yield defaults).</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), ReadOptions);
                if (s is not null)
                {
                    s.Normalize();
                    return s;
                }
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
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

    public void Write(string? file, IList<string> lines, int row, int col)
    {
        Directory.CreateDirectory(Dir);
        var draft = new DocDraft(file, new List<string>(lines), row, col, DateTime.UtcNow);
        File.WriteAllText(
            System.IO.Path.Combine(Dir, KeyFor(file) + ".json"),
            JsonSerializer.Serialize(draft));
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
