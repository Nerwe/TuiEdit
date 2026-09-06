using System.Text;

namespace TuiEdit;

/// <summary>Перевод строки в файле.</summary>
public enum LineEnding
{
    /// <summary>LF (\n).</summary>
    Lf,
    /// <summary>CRLF (\r\n).</summary>
    CrLf,
    /// <summary>CR (\r, старый Mac).</summary>
    Cr,
}

/// <summary>
/// Модель текстового буфера: строки (как есть, с табами), dirty-флаг, undo/redo,
/// кодировка / перевод строк / отступ (определяются при открытии, сохраняются при записи).
/// Основано на System.IO.File + System.Text.Encoding (см. Microsoft Learn: System.IO.File).
/// </summary>
internal sealed class TextBuffer
{
    public List<string> Lines { get; private set; } = new() { string.Empty };
    public string? FilePath { get; private set; }
    public bool IsModified { get; private set; }

    private Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Метка кодировки для статусбара: UTF-8, UTF-8 BOM, UTF-16 LE/BE.</summary>
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

    /// <summary>
    /// Открывает файл в буфер: определяет BOM/кодировку, перевод строк и отступ.
    /// Несуществующий путь даёт пустой документ с этим именем.
    /// Табы хранятся как есть, раскрытие — только для отрисовки (см. <see cref="TabStops"/>).
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
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
        _undo.Clear();
        _redo.Clear();
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
        _undo.Clear();
        _redo.Clear();
    }

    public void MarkSaved(string path)
    {
        FilePath = path;
        IsModified = false;
    }

    public void Save(string? path = null)
    {
        string target = path ?? FilePath
            ?? throw new InvalidOperationException("NoFileName");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".");
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
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(new List<string>(Lines));
        Lines = _undo.Pop();
        IsModified = true;
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(new List<string>(Lines));
        Lines = _redo.Pop();
        IsModified = true;
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
    /// <returns>Позиция курсора — конец вставки.</returns>
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

    /// <returns>Новая позиция курсора (row, col) после split.</returns>
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

    /// <returns>Новая позиция курсора после Backspace.</returns>
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
        // Склейка с предыдущей строкой.
        string prev = Lines[row - 1];
        string cur = Lines[row];
        Lines[row - 1] = prev + cur;
        Lines.RemoveAt(row);
        return (row - 1, prev.Length);
    }

    /// <returns>Новая позиция курсора после Delete (не меняется, кроме склейки).</returns>
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

    /// <summary>
    /// Удаляет нормализованный диапазон [start, end) одной undo-записью.
    /// </summary>
    /// <returns>Позиция курсора — начало диапазона.</returns>
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

    /// <summary>
    /// Добавляет отступ в начало строк [startRow, endRow] одной undo-записью.
    /// </summary>
    /// <returns>Добавленная ширина по каждой строке.</returns>
    public int[] IndentLines(int startRow, int endRow, string indent)
    {
        PushUndo();
        var added = new int[endRow - startRow + 1];
        for (int r = startRow; r <= endRow; r++)
        {
            Lines[r] = indent + Lines[r];
            added[r - startRow] = indent.Length;
        }
        return added;
    }

    /// <summary>
    /// Убирает один уровень отступа в строках [startRow, endRow] одной undo-записью.
    /// </summary>
    /// <returns>Убранная ширина по каждой строке.</returns>
    public int[] UnindentLines(int startRow, int endRow, string indent)
    {
        PushUndo();
        var removed = new int[endRow - startRow + 1];
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

    private static int TakeSpaces(string line, int max)
    {
        int n = 0;
        while (n < line.Length && line[n] == ' ' && n < max)
            n++;
        return n;
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

    /// <summary>Поиск вперёд от (startRow, startCol). Возвращает null если не найдено.</summary>
    public (int row, int col)? FindNext(string term, int startRow, int startCol)
    {
        if (string.IsNullOrEmpty(term)) return null;
        for (int r = startRow; r < Lines.Count; r++)
        {
            int from = (r == startRow) ? Math.Min(startCol, Lines[r].Length) : 0;
            int idx = Lines[r].IndexOf(term, from, StringComparison.Ordinal);
            if (idx >= 0) return (r, idx);
        }
        // Зацикливаем поиск с начала файла (как в nano).
        for (int r = 0; r <= startRow && r < Lines.Count; r++)
        {
            int to = (r == startRow) ? Math.Min(startCol, Lines[r].Length) : Lines[r].Length;
            int idx = Lines[r].IndexOf(term, 0, to, StringComparison.Ordinal);
            if (idx >= 0) return (r, idx);
        }
        return null;
    }

    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
