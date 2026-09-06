namespace TuiEdit;

/// <summary>
/// Сборка строки статусбара: слева — текст, справа — прижатый блок
/// (кодировка | переводы строк | отступ | файл).
/// Чистая функция без консоли — покрывается unit-тестами.
/// </summary>
public static class StatusBar
{
    /// <summary>
    /// Собирает строку ровно шириной <paramref name="width"/>.
    /// </summary>
    /// <param name="left">Левый текст (позиция или сообщение).</param>
    /// <param name="right">Правый блок (видно всегда; при переполнении — его хвост с именем файла).</param>
    /// <param name="width">Ширина консоли.</param>
    /// <returns>Строка длиной <paramref name="width"/> (или пустая при неположительной ширине).</returns>
    public static string Build(string left, string right, int width) => (left, right, width) switch
    {
        (_, _, <= 0) => string.Empty,
        var (_, r, w) when r.Length >= w => r[^w..],
        var (l, r, w) when l.Length > w - r.Length => l[..(w - r.Length)] + r,
        var (l, r, w) => l + new string(' ', w - r.Length - l.Length) + r,
    };
}
