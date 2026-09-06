namespace TuiEdit;

/// <summary>Класс символа для word-навигации.</summary>
public enum CharClass
{
    /// <summary>Пробел и таб.</summary>
    Whitespace,
    /// <summary>Разделитель (пунктуация).</summary>
    Separator,
    /// <summary>Всё остальное: буквы (incl. кириллица), цифры, _.</summary>
    Word,
}

/// <summary>
/// Word-навигация в стиле VS Code / MS Edit
/// (<c>word_forward / word_backward</c> в <c>buffer/navigation.rs</c> репозитория microsoft/edit):
/// пропуск пробелов, затем одно слово или сепаратор
/// (после одиночного сепаратора — ещё и следующее слово).
/// </summary>
public static class WordMotion
{
    private const string Separators = "`~!@#$%^&*()-=+[{]}\\|;:'\",.<>/?";

    /// <summary>Класс символа.</summary>
    public static CharClass Classify(char c) => c switch
    {
        ' ' or '\t' => CharClass.Whitespace,
        var ch when Separators.Contains(ch) => CharClass.Separator,
        _ => CharClass.Word,
    };

    /// <summary>Следующая граница слова вперёд (как <c>word_forward</c>).</summary>
    /// <param name="line">Строка без переводов.</param>
    /// <param name="col">Стартовая позиция [0, длина].</param>
    public static int Forward(string line, int col)
    {
        ArgumentNullException.ThrowIfNull(line);
        int i = Math.Clamp(col, 0, line.Length);
        while (i < line.Length && Classify(line[i]) == CharClass.Whitespace)
            i++;
        if (i < line.Length)
        {
            CharClass cls = Classify(line[i]);
            int start = i;
            i++;
            while (i < line.Length && Classify(line[i]) == cls)
                i++;
            if (i == start + 1 && cls == CharClass.Separator)
                while (i < line.Length && Classify(line[i]) == CharClass.Word)
                    i++;
        }
        return i;
    }

    /// <summary>Предыдущая граница слова (как <c>word_backward</c>).</summary>
    public static int Backward(string line, int col)
    {
        ArgumentNullException.ThrowIfNull(line);
        int i = Math.Clamp(col, 0, line.Length);
        while (i > 0 && Classify(line[i - 1]) == CharClass.Whitespace)
            i--;
        if (i > 0)
        {
            CharClass cls = Classify(line[i - 1]);
            int start = i;
            i--;
            while (i > 0 && Classify(line[i - 1]) == cls)
                i--;
            if (i == start - 1 && cls == CharClass.Separator)
                while (i > 0 && Classify(line[i - 1]) == CharClass.Word)
                    i--;
        }
        return i;
    }
}
