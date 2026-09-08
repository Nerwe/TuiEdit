using System.Globalization;

namespace TuiEdit;

/// <summary>
/// Версионные копии сохраняемых файлов в центральной папке (проект не замусоривается).
/// Имя: sha1(пути)_время.bak; на файл — до 5 свежих, старше 7 дней — чистка.
/// </summary>
public sealed class BackupStore(string dir)
{
    public const int MaxPerFile = 5;

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    public string Dir { get; } = dir;

    // Монотонный счётчик в имени: освобождённое ротацией имя нельзя занимать
    // заново в ту же секунду, иначе ротация удалит свежую копию как «старейшую».
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

    /// <summary>Сохранить копию байт файла перед перезаписью + ротация. Ошибки — молча.</summary>
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

    /// <summary>Выкинуть копии старше лимита (по всем файлам). Ошибки — молча.</summary>
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
