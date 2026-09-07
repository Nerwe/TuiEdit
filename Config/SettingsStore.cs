using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TuiEdit;

/// <summary>
/// Хранилище настроек в JSON
/// (<c>%APPDATA%\TuiEdit\settings.json</c>, на Linux — <c>~/.config/TuiEdit</c>).
/// </summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Путь к файлу.</summary>
    public string Path { get; } = path;

    /// <summary>Путь по умолчанию для платформы.</summary>
    public static string DefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TuiEdit", "settings.json");

    /// <summary>
    /// Итоговый путь: settings.json рядом с exe (portable-режим), иначе по умолчанию.
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

    /// <summary>Загрузить (нет/битый — умолчания).</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path));
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

    /// <summary>Сохранить (тихо игнорирует ошибки IO).</summary>
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

/// <summary>Черновик несохранённого документа для восстановления после краша.</summary>
public sealed record DocDraft(string? File, List<string> Lines, int Row, int Col, DateTime SavedAt);

/// <summary>
/// Хранилище черновиков (%TEMP%/TuiEdit/drafts/&lt;sha1 пути&gt;.json).
/// Чистый IO-класс без консоли — покрывается unit-тестами.
/// </summary>
public sealed class DraftStore(string dir)
{
    /// <summary>Каталог черновиков.</summary>
    public string Dir { get; } = dir;

    /// <summary>Каталог по умолчанию для платформы.</summary>
    public static string DefaultDir() => System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "TuiEdit", "drafts");

    /// <summary>Ключ черновика: sha1 пути (безымянный — "untitled").</summary>
    public static string KeyFor(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return "untitled";
        try
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(file)));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return "untitled";
        }
    }

    /// <summary>Записать черновик (ошибки — вызывающему).</summary>
    public void Write(string? file, IList<string> lines, int row, int col)
    {
        Directory.CreateDirectory(Dir);
        var draft = new DocDraft(file, new List<string>(lines), row, col, DateTime.UtcNow);
        File.WriteAllText(
            System.IO.Path.Combine(Dir, KeyFor(file) + ".json"),
            JsonSerializer.Serialize(draft));
    }

    /// <summary>Все читаемые черновики (битые пропускаются).</summary>
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

    /// <summary>Удалить черновик документа (нет — тихо).</summary>
    public void Delete(string? file) => DeleteKey(KeyFor(file));

    /// <summary>Удалить черновик по ключу (нет — тихо).</summary>
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
