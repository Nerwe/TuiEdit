namespace TuiEdit;

/// <summary>Builds the status bar row: text on the left, a pinned block on the right (encoding | line endings | indent | file).</summary>
public static class StatusBar
{
    /// <summary>Builds a row exactly width wide; the right block stays visible, showing its tail on overflow.</summary>
    public static string Build(string left, string right, int width) => (left, right, width) switch
    {
        (_, _, <= 0) => string.Empty,
        var (_, r, w) when r.Length >= w => r[^w..],
        var (l, r, w) when l.Length > w - r.Length => l[..(w - r.Length)] + r,
        var (l, r, w) => l + new string(' ', w - r.Length - l.Length) + r,
    };

    /// <summary>Builds the right block: encoding | endings | indent | file [| git] [| tabs] [| panes].</summary>
    public static string BuildRight(
        string encoding, string ending, string indent, string file, string? git,
        int tabIndex, int tabCount, int paneIndex, int paneCount) =>
        $" {encoding} | {ending} | {indent} | {file} "
        + (git is null ? string.Empty : $"| {git} ")
        + (tabCount > 1 ? $"| {tabIndex + 1}/{tabCount} " : string.Empty)
        + (paneCount > 1 ? $"| P{paneIndex + 1}/{paneCount} " : string.Empty);
}
