using System.Text;
using System.Text.RegularExpressions;

namespace TuiEdit;

/// <summary>Перевод строки в файле.</summary>
public enum LineEnding
{
    Lf,
    CrLf,
    Cr,
}

/// <summary>
/// Модель текстового буфера: строки (как есть, с табами), dirty-флаг, undo/redo,
/// кодировка / переводы строк / отступ (определяются при открытии, сохраняются при записи).
/// </summary>
internal sealed class TextBuffer
{
    public List<string> Lines { get; private set; } = new() { string.Empty };
    public string? FilePath { get; private set; }
    public bool IsModified { get; private set; }

    /// <summary>Счётчик изменений содержимого: по нему кэши понимают, что пора пересчитаться.</summary>
    public int Version { get; private set; }

    private Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public string EncodingLabel { get; private set; } = "UTF-8";

    /// <summary>Перевод строки файла (определяется при открытии).</summary>
    public LineEnding Ending { get; private set; } = DefaultEnding();

    /// <summary>Метка перевода строки для статусбара: LF / CRLF / CR.</summary>
    public string EndingLabel => Ending switch
    {
        LineEnding.CrLf => "CRLF",
        LineEnding.Lf => "LF",
        _ => "CR",
    };

    /// <summary>Строка отступа (таб или пробелы), определяется при открытии.</summary>
    public string IndentString { get; private set; } = "    ";

    /// <summary>Метка отступа для статусбара: Tab / 2sp / 4sp / ...</summary>
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

    /// <summary>Файл только для чтения (атрибут). Проверяется при открытии; Save смотрит живьём.</summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// Открывает файл в буфер: определяет BOM/кодировку, переводы строк и отступ.
    /// Несуществующий путь даёт пустой документ с этим именем.
    /// </summary>
    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
        DetectEncoding(bytes);
        string text = _encoding.GetString(bytes);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..]; // срезаем BOM-маркер
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

    /// <summary>Сбрасывает буфер в новый безымянный документ (UTF-8, системные переводы, 4 пробела).</summary>
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
    /// Восстановить содержимое из черновика: замена строк, undo сброшен, dirty.
    /// Путь и кодировка не трогаются (черновик — только текст и курсор).
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

    /// <param name="path">Путь (null — текущий).</param>
    /// <param name="backup">Хранилище версионной копии (null — без копии).</param>
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
        // Пишем как было: кодировка и переводы файла, завершающий перевод строки.
        File.WriteAllText(target, string.Join(newline, Lines) + newline, _encoding);
        MarkSaved(target);
        // После сохранения историю можно не чистить — undo остаётся доступным.
    }

    private void PushUndo()
    {
        _undo.Push(new List<string>(Lines));
        if (_undo.Count > MaxHistory)
        {
            // Stack не умеет выкинуть дно дёшево; пересоздаём при переполнении (редкий путь).
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
    /// Вставка многострочного текста (блок из bracketed paste) одной undo-записью:
    /// переводы (\r\n, \r, \n) режут на строки, управляющие символы (кроме \t) выкидываются.
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
    /// Текст нормализованного диапазона [start, end) — для копирования/вырезания.
    /// Многострочный фрагмент возвращается списком строк (края — частичные).
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

    /// <summary>Удаляет нормализованный диапазон [start, end) одной undo-записью.</summary>
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

    /// <summary>Переключить кодировку сохранения: UTF-8 → UTF-8 BOM → UTF-16 LE (dir — направление).</summary>
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

    /// <summary>Переключить переводы строк: CRLF → LF → CR (dir — направление).</summary>
    public void CycleEnding(int dir = 1)
    {
        LineEnding[] order = [LineEnding.CrLf, LineEnding.Lf, LineEnding.Cr];
        Ending = order[CycleIndex(order, Ending, dir)];
        IsModified = true;
    }

    /// <summary>Переключить единицу отступа: 4 пробела → 2 → таб (на текст не влияет).</summary>
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

    /// <summary>Срезать висячие пробелы/табы в концах строк одной undo-записью.</summary>
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

    /// <summary>Отступ в начало строк [startRow, endRow] одной undo-записью.</summary>
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

    /// <summary>Убирает один уровень отступа в строках [startRow, endRow].</summary>
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
        var (l, _) when l[0] == '\t' => 1, // чужой таб — убираем один
        var (l, u) => TakeSpaces(l, u == "\t" ? TabStops.Width : u.Length),
    };

    /// <summary>Комментарий-переключатель для строк [startRow, endRow] одной undo-записью.</summary>
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

    /// <summary>Сортировка строк [startRow, endRow] (ordinal) одной undo-записью.</summary>
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

    /// <summary>Дублирует строки [startRow, endRow] ниже блока. Один шаг undo.</summary>
    public int DuplicateLines(int startRow, int endRow)
    {
        startRow = Math.Clamp(startRow, 0, Lines.Count - 1);
        endRow = Math.Clamp(endRow, startRow, Lines.Count - 1);
        PushUndo();
        Lines.InsertRange(endRow + 1, Lines.GetRange(startRow, endRow - startRow + 1));
        return endRow + 1;
    }

    /// <summary>Двигает блок строк на одну вверх. Один шаг undo. false — блок уже вверху.</summary>
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

    /// <summary>Двигает блок строк на одну вниз. Один шаг undo. false — блок уже внизу.</summary>
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

    /// <summary>Таймаут regex-поиска (защита от катастрофического бэктрекинга).</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Плохой ли regex-шаблон (проверка до поиска, для сообщения).</summary>
    public static bool IsBadRegex(string term, bool matchCase, bool wholeWord) =>
        TryBuildRegex(term, matchCase, wholeWord) is null;

    /// <summary>Скомпилировать шаблон (null — пустой/некорректный).</summary>
    internal static Regex? TryBuildRegex(string term, bool matchCase, bool wholeWord)
    {
        if (string.IsNullOrEmpty(term)) return null;
        try
        {
            string pat = wholeWord ? $@"\b(?:{term})\b" : term;
            RegexOptions opts = RegexOptions.CultureInvariant;
            if (!matchCase) opts |= RegexOptions.IgnoreCase;
            return new Regex(pat, opts, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Поиск вперёд от (startRow, startCol): вхождения, начинающиеся на позиции
    /// с индексом &gt;= startCol в стартовой строке. При wrap=true зацикливает
    /// с начала файла (как в nano), флаг wrapped отличает оборот.
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
    /// Поиск назад от (startRow, startCol): вхождения, начинающиеся строго левее
    /// startCol в стартовой строке. При wrap=true зацикливает с конца файла.
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

    /// <summary>Число вхождений term в документе (без пересечений).</summary>
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

    /// <summary>Порядковый номер (1-based) вхождения в (row, col): строго левее + 1.</summary>
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
    /// Заменяет все вхождения от позиции до конца документа за один шаг undo.
    /// В regex-режиме в replacement работают группы $1 (как в MS Edit).
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
    /// Regex-замена: сначала собираем совпадения (таймаут — до любых правок),
    /// затем применяем. Пустые совпадения поглощают один символ (как в MS Edit).
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
    /// Первое вхождение, <b>начинающееся</b> в [start, end): -1 если нет.
    /// Важно: границе принадлежит старт, а не конец — вхождение вправе
    /// выступать за end (иначе оборот поиска теряет совпадение под курсором).
    /// </summary>
    private static int IndexOfOpt(string line, string term, int start, int end, bool matchCase, bool wholeWord)
    {
        int p = Math.Clamp(start, 0, line.Length);
        int lim = Math.Clamp(end, 0, line.Length);
        while (p < lim)
        {
            // Каунт с запасом на длину term: ищем старты < lim, хвост вправе выйти за lim.
            int idx = line.IndexOf(term, p, Math.Min(line.Length, lim + term.Length - 1) - p, Cmp(matchCase));
            if (idx < 0 || idx >= lim) return -1;
            if (!wholeWord || IsWholeOk(line, idx, term.Length)) return idx;
            p = idx + 1; // отклонено границей слова — шаг с перекрытием
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

    /// <summary>Статистика документа: строки, слова (раны IsWordChar), символы без переводов.</summary>
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
