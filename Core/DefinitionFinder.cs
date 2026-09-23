using System.Text;
using System.Text.RegularExpressions;

namespace TuiEdit;

/// <summary>Represents a definition site (public for tests).</summary>
internal sealed record DefHit(string File, int Row, int Col);

/// <summary>
/// Textual go-to-definition: finds `class|def|function NAME`, `NAME(` and
/// `NAME =` sites across the current file first, then the project.
/// No language server — ranking prefers keyword definitions over call sites.
/// </summary>
internal static class DefinitionFinder
{
    public const int MaxFiles = 500;
    public const int MaxHits = 20;

    private const long MaxFileBytes = 1L * 1024 * 1024;

    private static readonly TimeSpan RxTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Searches one file's lines. Pure, unit-tested.</summary>
    public static List<DefHit> FindInLines(IReadOnlyList<string> lines, string file,
        string name, int skipRow = -1, int skipCol = -1)
    {
        var hits = new List<DefHit>();
        if (lines.Count == 0 || string.IsNullOrEmpty(name))
            return hits;
        string esc = Regex.Escape(name);
        string[] patterns =
        [
            $@"^\s*(?:class|interface|struct|enum|record|def|function|fn|func)\s+{esc}\b",
            $@"(?<!\.)\b{esc}\s*\(",
            $@"^\s*{esc}\s*=",
        ];
        foreach (string pat in patterns)
        {
            Regex rx;
            try
            {
                rx = new Regex(pat, RegexOptions.CultureInvariant, RxTimeout);
            }
            catch (ArgumentException)
            {
                continue;
            }
            for (int r = 0; r < lines.Count && hits.Count < MaxHits; r++)
            {
                string line = lines[r];
                Match m;
                try
                {
                    m = rx.Match(line);
                }
                catch (RegexMatchTimeoutException)
                {
                    break;
                }
                if (!m.Success)
                    continue;
                // Point at NAME itself (patterns may start with a keyword prefix).
                int c = line.IndexOf(name, m.Index, StringComparison.Ordinal);
                if (c < 0)
                    c = m.Index;
                if (r == skipRow && c == skipCol)
                    continue;
                if (!hits.Any(h => h.Row == r && h.Col == c))
                    hits.Add(new DefHit(file, r, c));
            }
            if (hits.Count > 0)
                break; // keyword definitions outrank call sites
        }
        return hits;
    }

    /// <summary>Reads a file safely (size cap, binary skip). Returns null when skipped.</summary>
    public static List<string>? ReadSafeLines(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes || info.Length == 0)
                return null;
            byte[] head = new byte[Math.Min(8192, (int)info.Length)];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                fs.ReadExactly(head, 0, head.Length);
            if (head.Contains((byte)0))
                return null; // binary
            return File.ReadAllLines(path, Encoding.UTF8).ToList();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Searches the current file first, then the project root.</summary>
    public static List<DefHit> Find(string rootDir, string? currentFile,
        IReadOnlyList<string> currentLines, string name, int caretRow, int caretCol,
        int maxHits = MaxHits)
    {
        var hits = new List<DefHit>();
        if (!string.IsNullOrEmpty(currentFile))
            hits.AddRange(FindInLines(currentLines, currentFile, name, caretRow, caretCol));
        if (hits.Count >= maxHits || string.IsNullOrWhiteSpace(rootDir))
            return hits.Take(maxHits).ToList();
        string? currentFull = null;
        try
        {
            currentFull = string.IsNullOrEmpty(currentFile) ? null : Path.GetFullPath(currentFile);
        }
        catch
        {
        }
        int scanned = 0;
        foreach (string f in FileIndex.EnumerateFiles(rootDir))
        {
            if (hits.Count >= maxHits || scanned >= MaxFiles)
                break;
            string full;
            try
            {
                full = Path.GetFullPath(f);
            }
            catch
            {
                continue;
            }
            if (currentFull is not null && full.Equals(currentFull, StringComparison.OrdinalIgnoreCase))
                continue;
            scanned++;
            List<string>? lines = ReadSafeLines(f);
            if (lines is null)
                continue;
            hits.AddRange(FindInLines(lines, f, name));
        }
        return hits.Take(maxHits).ToList();
    }
}
