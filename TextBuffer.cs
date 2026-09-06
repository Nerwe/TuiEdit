using System.Text;

namespace TuiEdit;

/// <summary>
/// Модель текстового буфера: строки, dirty-флаг, undo/redo, загрузка/сохранение UTF-8.
/// Основано на System.IO.File + System.Text.Encoding (см. Microsoft Learn: System.Console, System.IO.File).
/// </summary>
internal sealed class TextBuffer
{
    public List<string> Lines { get; private set; } = new() { string.Empty };
    public string? FilePath { get; private set; }
    public bool IsModified { get; private set; }

    private readonly Stack<List<string>> _undo = new();
    private readonly Stack<List<string>> _redo = new();
    private const int MaxHistory = 200;

    public int Count => Lines.Count;

    public TextBuffer(string? filePath)
    {
        FilePath = filePath;
        if (filePath is not null && File.Exists(filePath))
        {
            // File.ReadAllLines определяет наличие файла; табуляции раскрываем в пробелы,
            // чтобы визуальная колонка курсора совпадала с индексом символа.
            Lines = File.ReadAllLines(filePath, Encoding.UTF8)
                .Select(l => l.Replace("\t", "    "))
                .ToList();
            if (Lines.Count == 0)
                Lines.Add(string.Empty);
            IsModified = false;
        }
    }

    public string GetLine(int row) => Lines[row];

    public void MarkSaved(string path)
    {
        FilePath = path;
        IsModified = false;
    }

    public void Save(string? path = null)
    {
        string target = path ?? FilePath
            ?? throw new InvalidOperationException("Нет имени файла. Используйте «Сохранить как».");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".");
        File.WriteAllLines(target, Lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
