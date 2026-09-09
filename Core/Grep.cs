using System.Text;
using System.Text.RegularExpressions;

namespace TuiEdit;

/// <summary>Represents a file search match (public for the modal factory).</summary>
public sealed record GrepHit(string File, int Row, int Col, string Text);

/// <summary>Searches files: recurses, skips hidden entries, and also skips binary and large files.</summary>
internal static class Grep
{
    public const int MaxHits = 500;

    private const long MaxFileBytes = 5L * 1024 * 1024;

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(500);

    public static List<GrepHit> Search(string root, string pattern,
        bool matchCase, bool wholeWord, bool useRegex, int maxHits = MaxHits)
    {
        var hits = new List<GrepHit>();
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrEmpty(pattern) || maxHits <= 0)
            return hits;
        Regex rx;
        try
        {
            string pat = useRegex ? pattern : Regex.Escape(pattern);
            if (wholeWord)
                pat = $@"\b(?:{pat})\b";
            RegexOptions opts = RegexOptions.CultureInvariant;
            if (!matchCase)
                opts |= RegexOptions.IgnoreCase;
            rx = new Regex(pat, opts, PatternTimeout);
        }
        catch (ArgumentException)
        {
            return hits;
        }
        string[] files;
        bool singleFile = false;
        try
        {
            if (File.Exists(root))
            {
                files = [Path.GetFullPath(root)];
                singleFile = true;
            }
            else if (Directory.Exists(root))
            {
                files = Directory.GetFiles(Path.GetFullPath(root), "*", SearchOption.AllDirectories);
            }
            else
            {
                return hits;
            }
        }
        catch
        {
            return hits;
        }
        Array.Sort(files, StringComparer.Ordinal);
        string rootFull;
        try
        {
            rootFull = Path.GetFullPath(root);
        }
        catch
        {
            return hits;
        }
        foreach (string f in files)
        {
            if (hits.Count >= maxHits)
                break;
            if (!singleFile && IsHiddenUnder(f, rootFull))
                continue;
            ScanFile(f, rx, hits, maxHits);
        }
        return hits;
    }

    internal static bool IsHiddenUnder(string file, string root)
    {
        try
        {
            if (SidebarState.IsHidden(file))
                return true;
            string? dir = Path.GetDirectoryName(Path.GetFullPath(file));
            while (!string.IsNullOrEmpty(dir) && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
            {
                if (SidebarState.IsHidden(dir))
                    return true;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch
        {
        }
        return false;
    }

    private static void ScanFile(string file, Regex rx, List<GrepHit> hits, int maxHits)
    {
        string text;
        try
        {
            if (new FileInfo(file).Length > MaxFileBytes)
                return;
            using var sr = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = sr.ReadToEnd();
            if (text.Contains('\0'))
                return;
        }
        catch
        {
            return;
        }
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int r = 0; r < lines.Length && hits.Count < maxHits; r++)
        {
            MatchCollection ms;
            try
            {
                ms = rx.Matches(lines[r]);
            }
            catch (RegexMatchTimeoutException)
            {
                return;
            }
            foreach (Match m in ms)
            {
                if (hits.Count >= maxHits)
                    return;
                hits.Add(new GrepHit(file, r, m.Index, lines[r]));
            }
        }
    }
}
