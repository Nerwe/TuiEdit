namespace TuiEdit;

/// <summary>Сборка строки статусбара: слева — текст, справа — прижатый блок (кодировка | переводы строк | отступ | файл).</summary>
public static class StatusBar
{
    /// <summary>Собирает строку ровно шириной width; правый блок виден всегда, при переполнении — его хвост.</summary>
    public static string Build(string left, string right, int width) => (left, right, width) switch
    {
        (_, _, <= 0) => string.Empty,
        var (_, r, w) when r.Length >= w => r[^w..],
        var (l, r, w) when l.Length > w - r.Length => l[..(w - r.Length)] + r,
        var (l, r, w) => l + new string(' ', w - r.Length - l.Length) + r,
    };
}
