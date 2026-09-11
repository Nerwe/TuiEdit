using System.Text;
using System.Text.RegularExpressions;

namespace TuiEdit;

/// <summary>Represents line endings in a file.</summary>
internal enum LineEnding
{
    Lf,
    CrLf,
    Cr,
}

/// <summary>
/// Represents the text buffer model: lines (as-is, with tabs), dirty flag, undo/redo,
/// encoding / line endings / indent (detected on open, preserved on write).
/// </summary>
internal sealed class TextBuffer
{
    public List<string> Lines { get; private set; } = new() { string.Empty };
    public string? FilePath { get; private set; }
    public bool IsModified { get; private set; }

    /// <summary>Gets the content change counter: caches use it to know when to recalculate.</summary>
    public int Version { get; private set; }

    private Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public string EncodingLabel { get; private set; } = "UTF-8";

    /// <summary>Gets the file line ending (detected on open).</summary>
    public LineEnding Ending { get; private set; } = DefaultEnding();

    /// <summary>Gets the line-ending label for the status bar: LF / CRLF / CR.</summary>
    public string EndingLabel => Ending switch
    {
        LineEnding.CrLf => "CRLF",
        LineEnding.Lf => "LF",
        _ => "CR",
    };

    /// <summary>Gets the indent string (tab or spaces), detected on open.</summary>
    public string IndentString { get; private set; } = "    ";

    /// <summary>Gets the indent label for the status bar: Tab / 2sp / 4sp / ...</summary>
    public string IndentLabel => IndentString switch
    {
        "\t" => "Tab",
        var s => $"{s.Length}sp",
    };

    private static LineEnding DefaultEnding() =>
        OperatingSystem.IsWindows() ? LineEnding.CrLf : LineEnding.Lf;

    private readonly Stack<List<string>> _undo = new();
    private readonly Stack<List<string>> _redo = new();
    private const int MaxHistory = 200;

    public int Count => Lines.Count;

    public TextBuffer(string? filePath)
    {
        if (filePath is not null && File.Exists(filePath))
            Open(filePath);
        else
            FilePath = filePath;
    }

    public string GetLine(int row) => Lines[row];

    /// <summary>Gets a value that indicates whether the file is read-only (attribute). Checks on open; Save rechecks live.</summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// Opens a file into the buffer: detects BOM/encoding, line endings, and indent.
    /// Creates an empty document with the given name for a nonexistent path.
    /// </summary>
    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
        DetectEncoding(bytes);
        string text = _encoding.GetString(bytes);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..]; // Strips the BOM marker
        DetectLineEnding(text);
        List<string> lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (lines.Count == 0)
            lines.Add(string.Empty);
        Lines = lines;
        DetectIndent(lines);
        IsReadOnly = IsReadOnlyPath(path);
        _undo.Clear();
        _redo.Clear();
        Version++;
        MarkSaved(path);
    }

    private void DetectEncoding(byte[] bytes)
    {
        (_encoding, EncodingLabel) = bytes switch
        {
            [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(true), "UTF-8 BOM"),
            [0xFF, 0xFE, ..] => (Encoding.Unicode, "UTF-16 LE"),
            [0xFE, 0xFF, ..] => (Encoding.BigEndianUnicode, "UTF-16 BE"),
            _ => (new UTF8Encoding(false), "UTF-8"),
        };
    }

    private static bool IsReadOnlyPath(string path)
    {
        try
        {
            return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        }
        catch
        {
            return false;
        }
    }

    private void DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    crlf++;
                    i++;
                    break;
                case '\r':
                    cr++;
                    break;
                case '\n':
                    lf++;
                    break;
            }
        }
        Ending = (crlf, lf, cr) switch
        {
            (0, 0, 0) => DefaultEnding(),
            var (c, l, r) when c >= l && c >= r => LineEnding.CrLf,
            var (_, l, r) when l >= r => LineEnding.Lf,
            _ => LineEnding.Cr,
        };
    }

    private void DetectIndent(List<string> lines)
    {
        int tabs = 0;
        var widths = new Dictionary<int, int>();
        foreach (string line in lines)
        {
            if (line.Length == 0)
                continue;
            switch (line[0])
            {
                case '\t':
                    tabs++;
                    break;
                case ' ':
                    int n = 0;
                    while (n < line.Length && line[n] == ' ')
                        n++;
                    widths[n] = widths.TryGetValue(n, out int c) ? c + 1 : 1;
                    break;
            }
        }
        if (tabs > 0 && tabs >= widths.Values.Sum())
        {
            IndentString = "\t";
            return;
        }
        IndentString = widths.Count == 0
            ? "    "
            : new string(' ', widths.MaxBy(kv => kv.Value).Key);
    }

    /// <summary>Resets the buffer to a new unnamed document (UTF-8, system line endings, 4 spaces).</summary>
    public void Clear()
    {
        Lines = new List<string> { string.Empty };
        FilePath = null;
        IsModified = false;
        _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        EncodingLabel = "UTF-8";
        Ending = DefaultEnding();
        IndentString = "    ";
        IsReadOnly = false;
        _undo.Clear();
        _redo.Clear();
        Version++;
    }

    public void MarkSaved(string path)
    {
        FilePath = path;
        IsModified = false;
    }

    /// <summary>
    /// Restores content from a draft: replaces lines, resets undo, and marks dirty.
    /// Leaves path and encoding untouched (a draft holds only text and cursor).
    /// </summary>
    public void RestoreContent(IList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Lines = new List<string>(lines);
        if (Lines.Count == 0)
            Lines.Add(string.Empty);
        _undo.Clear();
        _redo.Clear();
        IsModified = true;
        Version++;
    }

    /// <param name="path">The path (null means current).</param>
    /// <param name="backup">The versioned-copy store (null means no copy).</param>
    public void Save(string? path = null, BackupStore? backup = null)
    {
        string target = path ?? FilePath
            ?? throw new InvalidOperationException("NoFileName");
        if (IsReadOnlyPath(target))
            throw new InvalidOperationException("ReadOnly");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".");
        if (backup is not null && File.Exists(target))
        {
            try { backup.Write(target, File.ReadAllBytes(target)); }
            catch { }
        }
        string newline = Ending switch
        {
            LineEnding.CrLf => "\r\n",
            LineEnding.Lf => "\n",
            _ => "\r",
        };
        // Writes atomically: tmp in the SAME folder (otherwise move is not atomic) plus rename.
        // An interruption mid-write leaves the old file intact, not truncated.
        // Nuance: the replaced file loses non-standard ACLs (rename, not in-place write).
        string dir = Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".";
        string tmp = Path.Combine(dir, ".tui-edit-" + Path.GetRandomFileName() + ".tmp");
        try
        {
            // Writes back as-is: file encoding and line endings, plus a trailing line break.
            File.WriteAllText(tmp, string.Join(newline, Lines) + newline, _encoding);
            File.Move(tmp, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
        MarkSaved(target);
        // Keeps history after save — undo stays available.
    }

    private void PushUndo()
    {
        _undo.Push(new List<string>(Lines));
        if (_undo.Count > MaxHistory)
        {
            // Stack cannot drop the bottom cheaply; recreates it on overflow (rare path).
            var arr = _undo.ToArray(); // top-first
            _undo.Clear();
            for (int i = MaxHistory - 1; i >= 0; i--)
                _undo.Push(arr[i]);
        }
        _redo.Clear();
        IsModified = true;
        Version++;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(new List<string>(Lines));
        Lines = _undo.Pop();
        IsModified = true;
        Version++;
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(new List<string>(Lines));
        Lines = _redo.Pop();
        IsModified = true;
        Version++;
    }

    /// <summary>
    /// Merges the last <paramref name="count"/> undo steps into one by dropping
    /// intermediate snapshots. Snapshots are full states, so dropping is always
    /// text-correct — only granularity changes. Used for wire-speed runs where
    /// every char pushed its own entry. Best-effort, never throws.
    /// </summary>
    internal void CoalesceUndo(int count)
    {
        try
        {
            if (count <= 1 || _undo.Count <= 1)
                return;
            int drop = Math.Min(count - 1, _undo.Count - 1);
            for (int i = 0; i < drop; i++)
                _undo.Pop();
        }
        catch
        {
        }
    }

    public void InsertChar(int row, int col, char c)
    {
        PushUndo();
        string line = Lines[row];
        Lines[row] = line.Insert(col, c.ToString());
    }

    public void InsertString(int row, int col, string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        PushUndo();
        string line = Lines[row];
        Lines[row] = line.Insert(col, s);
    }

    /// <summary>
    /// Inserts multiline text (a bracketed-paste block) as a single undo entry:
    /// splits lines on (\r\n, \r, \n) and drops control characters (except \t).
    /// </summary>
    public (int row, int col) InsertText(int row, int col, string text)
    {
        PushUndo();
        string[] parts = CleanPastedText(text).Split('\n');
        if (parts.Length == 1)
        {
            Lines[row] = Lines[row].Insert(col, parts[0]);
            return (row, col + parts[0].Length);
        }
        string before = Lines[row][..col];
        string after = Lines[row][col..];
        Lines[row] = before + parts[0];
        for (int i = 1; i < parts.Length - 1; i++)
            Lines.Insert(row + i, parts[i]);
        Lines.Insert(row + parts.Length - 1, parts[^1] + after);
        return (row + parts.Length - 1, parts[^1].Length);
    }

    private static string CleanPastedText(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (c == '\n' || c == '\t' || !char.IsControl(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    public (int row, int col) SplitLine(int row, int col)
    {
        PushUndo();
        string line = Lines[row];
        string before = line[..col];
        string after = line[col..];
        Lines[row] = before;
        Lines.Insert(row + 1, after);
        return (row + 1, 0);
    }

    public (int row, int col) Backspace(int row, int col)
    {
        if (row == 0 && col == 0) return (row, col);
        PushUndo();
        if (col > 0)
        {
            string line = Lines[row];
            Lines[row] = line.Remove(col - 1, 1);
            return (row, col - 1);
        }
        string prev = Lines[row - 1];
        string cur = Lines[row];
        Lines[row - 1] = prev + cur;
        Lines.RemoveAt(row);
        return (row - 1, prev.Length);
    }

    /// <summary>
    /// Inserts a whole pair in one undo step; places the cursor between (`(|)`).
    /// </summary>
    public (int row, int col) InsertPair(int row, int col, char open, char close)
    {
        PushUndo();
        string line = Lines[row];
        Lines[row] = line.Insert(col, new string([open, close]));
        return (row, col + 1);
    }

    /// <summary>
    /// Deletes both characters on Backspace between a pair (`(|)` in one step); returns null when not a pair.
    /// </summary>
    public (int row, int col)? DeletePair(int row, int col)
    {
        if (AutoPair.PairDeleteCol(Lines[row], col) is not int nc)
            return null;
        PushUndo();
        string line = Lines[row];
        Lines[row] = line.Remove(nc, 2);
        return (row, nc);
    }

    public (int row, int col) Delete(int row, int col)
    {
        string line = Lines[row];
        if (col < line.Length)
        {
            PushUndo();
            Lines[row] = line.Remove(col, 1);
            return (row, col);
        }
        if (row < Lines.Count - 1)
        {
            PushUndo();
            Lines[row] = line + Lines[row + 1];
            Lines.RemoveAt(row + 1);
        }
        return (row, col);
    }

    public string CutLine(int row)
    {
        PushUndo();
        string cut = Lines[row];
        Lines.RemoveAt(row);
        if (Lines.Count == 0)
            Lines.Add(string.Empty);
        return cut;
    }

    /// <summary>
    /// Gets text of the normalized [start, end) range for copy/cut.
    /// Returns a multiline fragment as a string list (edges are partial).
    /// </summary>
    public List<string> GetRangeText(int startRow, int startCol, int endRow, int endCol)
    {
        if (startRow == endRow)
            return new List<string> { Lines[startRow][startCol..endCol] };
        var frag = new List<string> { Lines[startRow][startCol..] };
        for (int r = startRow + 1; r < endRow; r++)
            frag.Add(Lines[r]);
        frag.Add(Lines[endRow][..endCol]);
        return frag;
    }

    /// <summary>Deletes the normalized [start, end) range in a single undo entry.</summary>
    public (int row, int col) DeleteRange(int startRow, int startCol, int endRow, int endCol)
    {
        if (startRow == endRow && startCol == endCol)
            return (startRow, startCol);
        PushUndo();
        if (startRow == endRow)
        {
            Lines[startRow] = Lines[startRow].Remove(startCol, endCol - startCol);
            return (startRow, startCol);
        }
        Lines[startRow] = Lines[startRow][..startCol] + Lines[endRow][endCol..];
        Lines.RemoveRange(startRow + 1, endRow - startRow);
        return (startRow, startCol);
    }

    /// <summary>Cycles the save encoding: UTF-8 → UTF-8 BOM → UTF-16 LE (dir sets the direction).</summary>
    public void CycleEncoding(int dir = 1)
    {
        string[] order = ["UTF-8", "UTF-8 BOM", "UTF-16 LE"];
        int i = CycleIndex(order, EncodingLabel, dir);
        (_encoding, EncodingLabel) = i switch
        {
            1 => (new UTF8Encoding(true), order[1]),
            2 => (Encoding.Unicode, order[2]),
            _ => (new UTF8Encoding(false), order[0]),
        };
        IsModified = true;
    }

    /// <summary>Cycles line endings: CRLF → LF → CR (dir sets the direction).</summary>
    public void CycleEnding(int dir = 1)
    {
        LineEnding[] order = [LineEnding.CrLf, LineEnding.Lf, LineEnding.Cr];
        Ending = order[CycleIndex(order, Ending, dir)];
        IsModified = true;
    }

    /// <summary>
    /// Sets the save encoding by name (utf8, utf8bom, utf16/utf16le); returns
    /// <see langword="false"/> on unknown names. Marks the buffer modified.
    /// </summary>
    /// <param name="name">The encoding name (case-insensitive, dashes/spaces ignored).</param>
    public bool TrySetEncoding(string? name)
    {
        string key = (name ?? string.Empty).Replace("-", "").Replace(" ", "").Replace("_", "").ToLowerInvariant();
        (Encoding encoding, string label)? pick = key switch
        {
            "utf8" => (new UTF8Encoding(false), "UTF-8"),
            "utf8bom" => (new UTF8Encoding(true), "UTF-8 BOM"),
            "utf16" or "utf16le" or "unicode" => (Encoding.Unicode, "UTF-16 LE"),
            _ => null,
        };
        if (pick is null)
            return false;
        (_encoding, EncodingLabel) = pick.Value;
        IsModified = true;
        return true;
    }

    /// <summary>
    /// Sets line endings by name (crlf, lf, cr); returns <see langword="false"/>
    /// on unknown names. Marks the buffer modified.
    /// </summary>
    /// <param name="name">The endings name (case-insensitive).</param>
    public bool TrySetEnding(string? name)
    {
        LineEnding? ending = (name ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "crlf" or "windows" => LineEnding.CrLf,
            "lf" or "unix" => LineEnding.Lf,
            "cr" or "mac" => LineEnding.Cr,
            _ => null,
        };
        if (ending is null)
            return false;
        Ending = ending.Value;
        IsModified = true;
        return true;
    }

    /// <summary>
    /// Sets the indent unit by name (4, 2, tab); returns <see langword="false"/>
    /// on unknown names. Leaves text unaffected.
    /// </summary>
    /// <param name="name">The indent name (case-insensitive).</param>
    public bool TrySetIndent(string? name)
    {
        string? indent = (name ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "4" or "4spaces" or "spaces" => "    ",
            "2" or "2spaces" => "  ",
            "tab" or "\\t" or "t" => "\t",
            _ => null,
        };
        if (indent is null)
            return false;
        IndentString = indent;
        return true;
    }

    /// <summary>Cycles the indent unit: 4 spaces → 2 → tab (leaves text unaffected).</summary>
    public void CycleIndent(int dir = 1)
    {
        string[] order = ["    ", "  ", "\t"];
        IndentString = order[CycleIndex(order, IndentString, dir)];
    }

    private static int CycleIndex<T>(T[] order, T current, int dir)
    {
        int i = Array.IndexOf(order, current);
        if (i < 0)
            i = dir >= 0 ? -1 : 0;
        return ((i + dir) % order.Length + order.Length) % order.Length;
    }

    /// <summary>Trims trailing spaces/tabs at line ends in a single undo entry.</summary>
    public int TrimTrailingWhitespace()
    {
        int n = 0;
        for (int r = 0; r < Lines.Count; r++)
            if (Lines[r].TrimEnd().Length != Lines[r].Length)
                n++;
        if (n == 0)
            return 0;
        PushUndo();
        for (int r = 0; r < Lines.Count; r++)
            Lines[r] = Lines[r].TrimEnd();
        return n;
    }

    /// <summary>Indents the start of lines [startRow, endRow] in a single undo entry.</summary>
    public int[] IndentLines(int startRow, int endRow, string indent)
    {
        PushUndo();
        int[] added = new int[endRow - startRow + 1];
        for (int r = startRow; r <= endRow; r++)
        {
            Lines[r] = indent + Lines[r];
            added[r - startRow] = indent.Length;
        }
        return added;
    }

    /// <summary>Removes one indent level in lines [startRow, endRow].</summary>
    public int[] UnindentLines(int startRow, int endRow, string indent)
    {
        PushUndo();
        int[] removed = new int[endRow - startRow + 1];
        for (int r = startRow; r <= endRow; r++)
        {
            int n = UnindentWidth(Lines[r], indent);
            Lines[r] = Lines[r][n..];
            removed[r - startRow] = n;
        }
        return removed;
    }

    private static int UnindentWidth(string line, string indentUnit) => (line, indentUnit) switch
    {
        ("", _) => 0,
        var (l, u) when l.StartsWith(u, StringComparison.Ordinal) => u.Length,
        var (l, _) when l[0] == '\t' => 1, // Foreign tab — removes one
        var (l, u) => TakeSpaces(l, u == "\t" ? TabStops.Width : u.Length),
    };

    /// <summary>Toggles the line comment for lines [startRow, endRow] in a single undo entry.</summary>
    public int[] ToggleLineComment(int startRow, int endRow, string lineComment)
    {
        PushUndo();
        int[] delta = new int[endRow - startRow + 1];
        bool all = true;
        for (int r = startRow; r <= endRow; r++)
            if (!IsCommented(Lines[r], lineComment))
            {
                all = false;
                break;
            }
        for (int r = startRow; r <= endRow; r++)
        {
            string line = Lines[r];
            if (all)
            {
                int at = line.IndexOf(lineComment, StringComparison.Ordinal);
                int cut = lineComment.Length;
                if (at + cut < line.Length && line[at + cut] == ' ')
                    cut++;
                Lines[r] = line.Remove(at, cut);
                delta[r - startRow] = -cut;
            }
            else if (!IsCommented(line, lineComment))
            {
                int indent = 0;
                while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t'))
                    indent++;
                Lines[r] = line.Insert(indent, lineComment + " ");
                delta[r - startRow] = lineComment.Length + 1;
            }
        }
        return delta;
    }

    private static bool IsCommented(string line, string lc) =>
        line.TrimStart().StartsWith(lc, StringComparison.Ordinal);

    private static int TakeSpaces(string line, int max)
    {
        int n = 0;
        while (n < line.Length && line[n] == ' ' && n < max)
            n++;
        return n;
    }

    /// <summary>Sorts lines [startRow, endRow] (ordinal) in a single undo entry.</summary>
    public void SortLines(int startRow, int endRow)
    {
        PushUndo();
        var slice = Lines.GetRange(startRow, endRow - startRow + 1);
        slice.Sort(StringComparer.Ordinal);
        for (int i = 0; i < slice.Count; i++)
            Lines[startRow + i] = slice[i];
    }

    public void PasteLines(int row, int col, IList<string> clipboard)
    {
        if (clipboard.Count == 0) return;
        PushUndo();
        if (clipboard.Count == 1)
        {
            Lines[row] = Lines[row].Insert(col, clipboard[0]);
            return;
        }
        string cur = Lines[row];
        string before = cur[..col];
        string after = cur[col..];
        Lines[row] = before + clipboard[0];
        for (int i = 1; i < clipboard.Count - 1; i++)
            Lines.Insert(row + i, clipboard[i]);
        Lines.Insert(row + clipboard.Count - 1, clipboard[^1] + after);
    }

    /// <summary>Duplicates lines [startRow, endRow] below the block. Uses a single undo step.</summary>
    public int DuplicateLines(int startRow, int endRow)
    {
        startRow = Math.Clamp(startRow, 0, Lines.Count - 1);
        endRow = Math.Clamp(endRow, startRow, Lines.Count - 1);
        PushUndo();
        Lines.InsertRange(endRow + 1, Lines.GetRange(startRow, endRow - startRow + 1));
        return endRow + 1;
    }

    /// <summary>Moves a block of lines one line up. Uses a single undo step. Returns <see langword="false" /> when the block is already at the top.</summary>
    public bool MoveLinesUp(int startRow, int endRow)
    {
        startRow = Math.Clamp(startRow, 0, Lines.Count - 1);
        endRow = Math.Clamp(endRow, startRow, Lines.Count - 1);
        if (startRow <= 0) return false;
        PushUndo();
        string above = Lines[startRow - 1];
        Lines.RemoveAt(startRow - 1);
        Lines.Insert(endRow, above);
        return true;
    }

    /// <summary>Moves a block of lines one line down. Uses a single undo step. Returns <see langword="false" /> when the block is already at the bottom.</summary>
    public bool MoveLinesDown(int startRow, int endRow)
    {
        startRow = Math.Clamp(startRow, 0, Lines.Count - 1);
        endRow = Math.Clamp(endRow, startRow, Lines.Count - 1);
        if (endRow >= Lines.Count - 1) return false;
        PushUndo();
        string below = Lines[endRow + 1];
        Lines.RemoveAt(endRow + 1);
        Lines.Insert(startRow, below);
        return true;
    }

    /// <summary>Specifies the regex search timeout (guards against catastrophic backtracking).</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Determines whether a regex pattern is invalid (pre-search check for messaging).</summary>
    public static bool IsBadRegex(string term, bool matchCase, bool wholeWord) =>
        TryBuildRegex(term, matchCase, wholeWord) is null;

    /// <summary>Compiles a pattern (returns null when empty/invalid).</summary>
    /// <summary>
    /// First-match preview for the replace confirm: row plus before/after lines.
    /// Only the first occurrence on its line is substituted; long lines are
    /// centered on the match. Null when there is no match or nothing would change.
    /// </summary>
    /// <param name="term">The search term.</param>
    /// <param name="replacement">The replacement text.</param>
    /// <param name="matchCase">Whether case matters.</param>
    /// <param name="wholeWord">Whether whole words only.</param>
    /// <param name="useRegex">Whether the term is a regular expression.</param>
    public (int row, string before, string after)? PreviewReplace(
        string term, string replacement, bool matchCase, bool wholeWord, bool useRegex = false)
    {
        if (string.IsNullOrEmpty(term) || replacement is null)
            return null;
        if (useRegex)
        {
            Regex? rx = TryBuildRegex(term, matchCase, wholeWord);
            if (rx is null)
                return null;
            for (int r = 0; r < Lines.Count; r++)
            {
                Match m;
                try
                {
                    m = rx.Match(Lines[r]);
                }
                catch
                {
                    return null;
                }
                if (!m.Success)
                    continue;
                string after;
                try
                {
                    after = rx.Replace(Lines[r], replacement, 1);
                }
                catch
                {
                    return null;
                }
                return FinishPreview(r, m.Index, m.Length, Lines[r], after);
            }
            return null;
        }
        var hit = FindNext(term, 0, 0, matchCase, wholeWord, wrap: false);
        if (hit is null)
            return null;
        string line = Lines[hit.Value.row];
        int idx = IndexOfOpt(line, term, 0, line.Length, matchCase, wholeWord);
        if (idx < 0)
            return null;
        return FinishPreview(hit.Value.row, idx, term.Length, line,
            line[..idx] + replacement + line[(idx + term.Length)..]);
    }

    private static (int row, string before, string after)? FinishPreview(
        int row, int index, int length, string line, string after)
    {
        if (after == line)
            return null; // nothing would visibly change
        return (row, TruncateAround(line, index, length), TruncateAround(after, index, length));
    }

    private static string TruncateAround(string s, int index, int length)
    {
        const int max = 56;
        if (s.Length <= max)
            return s;
        int start = Math.Clamp(index - 20, 0, Math.Max(0, s.Length - max));
        string window = s.Substring(start, Math.Min(max, s.Length - start));
        if (start > 0)
            window = "…" + window[1..];
        if (start + max < s.Length)
            window = window[..^1] + "…";
        return window;
    }

    internal static Regex? TryBuildRegex(string term, bool matchCase, bool wholeWord, TimeSpan? timeout = null)
    {
        if (string.IsNullOrEmpty(term)) return null;
        try
        {
            string pat = wholeWord ? $@"\b(?:{term})\b" : term;
            RegexOptions opts = RegexOptions.CultureInvariant;
            if (!matchCase) opts |= RegexOptions.IgnoreCase;
            return new Regex(pat, opts, timeout ?? RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Searches forward from (startRow, startCol): matches starting at positions
    /// with index &gt;= startCol in the start line. When wrap is <see langword="true" />, wraps
    /// from the file start (wrapping); the wrapped flag marks the wrap-around.
    /// </summary>
    public (int row, int col)? FindNext(string term, int startRow, int startCol) =>
        FindNext(term, startRow, startCol, matchCase: true, wholeWord: false) is { } h
            ? (h.row, h.col)
            : null;

    /// <inheritdoc cref="FindNext(string, int, int)"/>
    public (int row, int col, bool wrapped)? FindNext(
        string term, int startRow, int startCol, bool matchCase, bool wholeWord,
        bool wrap = true, bool useRegex = false)
    {
        if (string.IsNullOrEmpty(term)) return null;
        if (useRegex)
        {
            Regex? rx = TryBuildRegex(term, matchCase, wholeWord);
            if (rx is null) return null;
            try
            {
                startRow = Math.Clamp(startRow, 0, Lines.Count - 1);
                for (int r = startRow; r < Lines.Count; r++)
                {
                    int from = (r == startRow) ? Math.Min(Math.Max(startCol, 0), Lines[r].Length) : 0;
                    int idx = RegexIndexOf(Lines[r], rx, from, Lines[r].Length);
                    if (idx >= 0) return (r, idx, false);
                }
                if (!wrap) return null;
                for (int r = 0; r <= startRow && r < Lines.Count; r++)
                {
                    int to = (r == startRow) ? Math.Min(Math.Max(startCol, 0), Lines[r].Length) : Lines[r].Length;
                    int idx = RegexIndexOf(Lines[r], rx, 0, to);
                    if (idx >= 0) return (r, idx, true);
                }
                return null;
            }
            catch (RegexMatchTimeoutException) { return null; }
        }
        startRow = Math.Clamp(startRow, 0, Lines.Count - 1);
        for (int r = startRow; r < Lines.Count; r++)
        {
            int from = (r == startRow) ? Math.Min(Math.Max(startCol, 0), Lines[r].Length) : 0;
            int idx = IndexOfOpt(Lines[r], term, from, Lines[r].Length, matchCase, wholeWord);
            if (idx >= 0) return (r, idx, false);
        }
        if (!wrap) return null;
        for (int r = 0; r <= startRow && r < Lines.Count; r++)
        {
            int to = (r == startRow) ? Math.Min(Math.Max(startCol, 0), Lines[r].Length) : Lines[r].Length;
            int idx = IndexOfOpt(Lines[r], term, 0, to, matchCase, wholeWord);
            if (idx >= 0) return (r, idx, true);
        }
        return null;
    }

    /// <summary>
    /// Searches backward from (startRow, startCol): matches starting strictly left of
    /// startCol in the start line. When wrap is <see langword="true" />, wraps from the file end.
    /// </summary>
    public (int row, int col, bool wrapped)? FindPrev(
        string term, int startRow, int startCol, bool matchCase, bool wholeWord,
        bool wrap = true, bool useRegex = false)
    {
        if (string.IsNullOrEmpty(term)) return null;
        if (useRegex)
        {
            Regex? rx = TryBuildRegex(term, matchCase, wholeWord);
            if (rx is null) return null;
            try
            {
                startRow = Math.Clamp(startRow, 0, Lines.Count - 1);
                for (int r = startRow; r >= 0; r--)
                {
                    int to = (r == startRow) ? Math.Min(Math.Max(startCol, 0), Lines[r].Length) : Lines[r].Length;
                    int idx = LastRegexIndexOf(Lines[r], rx, 0, to);
                    if (idx >= 0) return (r, idx, false);
                }
                if (!wrap) return null;
                for (int r = Lines.Count - 1; r >= startRow; r--)
                {
                    int from = (r == startRow) ? Math.Min(Math.Max(startCol, 0), Lines[r].Length) : 0;
                    int idx = LastRegexIndexOf(Lines[r], rx, from, Lines[r].Length);
                    if (idx >= 0) return (r, idx, true);
                }
                return null;
            }
            catch (RegexMatchTimeoutException) { return null; }
        }
        startRow = Math.Clamp(startRow, 0, Lines.Count - 1);
        for (int r = startRow; r >= 0; r--)
        {
            int to = (r == startRow) ? Math.Min(Math.Max(startCol, 0), Lines[r].Length) : Lines[r].Length;
            int idx = LastIndexOfOpt(Lines[r], term, 0, to, matchCase, wholeWord);
            if (idx >= 0) return (r, idx, false);
        }
        if (!wrap) return null;
        for (int r = Lines.Count - 1; r >= startRow; r--)
        {
            int from = (r == startRow) ? Math.Min(Math.Max(startCol, 0), Lines[r].Length) : 0;
            int idx = LastIndexOfOpt(Lines[r], term, from, Lines[r].Length, matchCase, wholeWord);
            if (idx >= 0) return (r, idx, true);
        }
        return null;
    }

    /// <summary>Counts occurrences of term in the document (without overlaps).</summary>
    public int CountMatches(string term, bool matchCase, bool wholeWord, bool useRegex = false)
    {
        if (string.IsNullOrEmpty(term)) return 0;
        if (useRegex)
        {
            Regex? rx = TryBuildRegex(term, matchCase, wholeWord);
            if (rx is null) return 0;
            try
            {
                int total = 0;
                foreach (string line in Lines)
                    foreach (Match _ in rx.Matches(line))
                        total++;
                return total;
            }
            catch (RegexMatchTimeoutException) { return 0; }
        }
        int n = 0;
        foreach (string line in Lines)
        {
            int p = 0;
            while (p <= line.Length - term.Length)
            {
                int idx = IndexOfOpt(line, term, p, line.Length, matchCase, wholeWord);
                if (idx < 0) break;
                n++;
                p = idx + term.Length;
            }
        }
        return n;
    }

    /// <summary>Gets the 1-based ordinal of the occurrence at (row, col): strictly left plus 1.</summary>
    public int MatchOrdinal(string term, int row, int col, bool matchCase, bool wholeWord, bool useRegex = false)
    {
        if (string.IsNullOrEmpty(term)) return 0;
        if (useRegex)
        {
            Regex? rx = TryBuildRegex(term, matchCase, wholeWord);
            if (rx is null) return 0;
            try
            {
                int total = 0;
                for (int r = 0; r < Lines.Count; r++)
                {
                    int end = (r == row) ? Math.Min(Math.Max(col, 0), Lines[r].Length)
                        : (r < row ? Lines[r].Length : -1);
                    if (end < 0) break;
                    foreach (Match m in rx.Matches(Lines[r]))
                    {
                        if (m.Index >= end) break;
                        total++;
                    }
                }
                return total + 1;
            }
            catch (RegexMatchTimeoutException) { return 0; }
        }
        int n = 0;
        for (int r = 0; r < Lines.Count; r++)
        {
            int end = (r == row) ? Math.Min(Math.Max(col, 0), Lines[r].Length)
                : (r < row ? Lines[r].Length : -1);
            if (end < 0) break;
            int p = 0;
            while (p <= end - term.Length)
            {
                int idx = IndexOfOpt(Lines[r], term, p, end, matchCase, wholeWord);
                if (idx < 0) break;
                n++;
                p = idx + term.Length;
            }
        }
        return n + 1;
    }

    /// <summary>
    /// Replaces all occurrences from a position to the document end in one undo step.
    /// In regex mode, replacement supports $1 groups (standard .NET Regex).
    /// </summary>
    public int ReplaceAll(string term, string replacement,
        int fromRow, int fromCol, bool matchCase, bool wholeWord, bool useRegex = false)
    {
        if (string.IsNullOrEmpty(term)) return 0;
        if (useRegex)
            return ReplaceAllRegex(term, replacement, fromRow, fromCol, matchCase, wholeWord);
        fromRow = Math.Clamp(fromRow, 0, Lines.Count - 1);
        int total = 0;
        bool pushed = false;
        for (int r = fromRow; r < Lines.Count; r++)
        {
            string line = Lines[r];
            int p = (r == fromRow) ? Math.Min(Math.Max(fromCol, 0), line.Length) : 0;
            var sb = new StringBuilder(line.Length);
            sb.Append(line, 0, p);
            int cur = p;
            int n = 0;
            while (true)
            {
                int idx = IndexOfOpt(line, term, cur, line.Length, matchCase, wholeWord);
                if (idx < 0) break;
                sb.Append(line, cur, idx - cur);
                sb.Append(replacement);
                cur = idx + term.Length;
                n++;
            }
            if (n > 0)
            {
                sb.Append(line, cur, line.Length - cur);
                if (!pushed) { PushUndo(); pushed = true; }
                Lines[r] = sb.ToString();
                total += n;
            }
        }
        return total;
    }

    private static StringComparison Cmp(bool matchCase) =>
        matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Applies regex replacement: first collects matches (times out before any edits),
    /// then applies them. Consumes one character for empty matches (loop protection).
    /// </summary>
    private int ReplaceAllRegex(string term, string replacement,
        int fromRow, int fromCol, bool matchCase, bool wholeWord)
    {
        Regex? rx = TryBuildRegex(term, matchCase, wholeWord);
        if (rx is null) return 0;
        fromRow = Math.Clamp(fromRow, 0, Lines.Count - 1);
        var plan = new List<(int row, int start, List<Match> ms)>();
        try
        {
            for (int r = fromRow; r < Lines.Count; r++)
            {
                string line = Lines[r];
                int p = (r == fromRow) ? Math.Min(Math.Max(fromCol, 0), line.Length) : 0;
                var ms = new List<Match>();
                foreach (Match m in rx.Matches(line))
                {
                    if (m.Index >= p)
                        ms.Add(m);
                }
                if (ms.Count > 0)
                    plan.Add((r, p, ms));
            }
        }
        catch (RegexMatchTimeoutException) { return 0; }
        if (plan.Count == 0) return 0;
        PushUndo();
        int total = 0;
        foreach (var (r, p, ms) in plan)
        {
            string line = Lines[r];
            var sb = new StringBuilder(line.Length + 16);
            sb.Append(line, 0, p);
            int cur = p;
            foreach (Match m in ms)
            {
                if (m.Index < cur) continue;
                sb.Append(line, cur, m.Index - cur);
                sb.Append(m.Result(replacement));
                total++;
                cur = m.Index + m.Length;
                if (m.Length == 0)
                {
                    if (cur >= line.Length) break;
                    sb.Append(line[cur]);
                    cur++;
                }
            }
            sb.Append(line, cur, line.Length - cur);
            Lines[r] = sb.ToString();
        }
        return total;
    }

    /// <summary>
    /// Finds the first occurrence <b>starting</b> in [start, end): -1 when absent.
    /// Importantly, the start (not the end) owns the boundary — a match may
    /// extend past end (otherwise wrap-around search loses the match under the cursor).
    /// </summary>
    private static int IndexOfOpt(string line, string term, int start, int end, bool matchCase, bool wholeWord)
    {
        int p = Math.Clamp(start, 0, line.Length);
        int lim = Math.Clamp(end, 0, line.Length);
        while (p < lim)
        {
            // Counts with term-length slack: searches starts < lim, tail may extend past lim.
            int idx = line.IndexOf(term, p, Math.Min(line.Length, lim + term.Length - 1) - p, Cmp(matchCase));
            if (idx < 0 || idx >= lim) return -1;
            if (!wholeWord || IsWholeOk(line, idx, term.Length)) return idx;
            p = idx + 1; // Rejects on word boundary — steps with overlap
        }
        return -1;
    }

    private static int LastIndexOfOpt(string line, string term, int start, int end, bool matchCase, bool wholeWord)
    {
        int p = Math.Clamp(start, 0, line.Length);
        int lim = Math.Clamp(end, 0, line.Length);
        int last = -1;
        while (p < lim)
        {
            int idx = line.IndexOf(term, p, Math.Min(line.Length, lim + term.Length - 1) - p, Cmp(matchCase));
            if (idx < 0 || idx >= lim) break;
            if (!wholeWord || IsWholeOk(line, idx, term.Length)) last = idx;
            p = idx + 1;
        }
        return last;
    }

    private static bool IsWholeOk(string line, int idx, int len) =>
        (idx == 0 || !IsWordChar(line[idx - 1])) &&
        (idx + len >= line.Length || !IsWordChar(line[idx + len]));

    private static int RegexIndexOf(string line, Regex rx, int start, int end)
    {
        int lim = Math.Clamp(end, 0, line.Length);
        foreach (Match m in rx.Matches(line))
        {
            if (m.Index < start) continue;
            if (m.Index >= lim) break;
            return m.Index;
        }
        return -1;
    }

    private static int LastRegexIndexOf(string line, Regex rx, int start, int end)
    {
        int lim = Math.Clamp(end, 0, line.Length);
        int last = -1;
        foreach (Match m in rx.Matches(line))
        {
            if (m.Index < start) continue;
            if (m.Index >= lim) break;
            last = m.Index;
        }
        return last;
    }

    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Computes document statistics: lines, words (IsWordChar runs), characters excluding breaks.</summary>
    public (int Lines, int Words, int Chars) CountStats()
    {
        int words = 0, chars = 0;
        foreach (string line in Lines)
        {
            chars += line.Length;
            bool inWord = false;
            foreach (char c in line)
            {
                if (IsWordChar(c))
                {
                    if (!inWord)
                    {
                        inWord = true;
                        words++;
                    }
                }
                else
                {
                    inWord = false;
                }
            }
        }
        return (Lines.Count, words, chars);
    }
}
