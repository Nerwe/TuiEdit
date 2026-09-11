using System.Reflection;
using System.Text.RegularExpressions;

namespace TuiEdit;

/// <summary>Builds the status bar row: text on the left, a pinned block on the right (encoding | line endings | indent | file).</summary>
internal static class StatusBar
{
    private static readonly Regex VerbRx = new(@"\$\((.+?)\)", RegexOptions.Compiled);

    /// <summary>Expands $(verb) markers via resolve (unknown verbs pass through literally).</summary>
    public static string Expand(string format, Func<string, string?> resolve) =>
        VerbRx.Replace(format, m => resolve(m.Groups[1].Value) ?? m.Value);

    private static readonly Dictionary<string, PropertyInfo> OptProps = typeof(AppSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0
            && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum))
        .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Formats an option for $(opt:name): bools as on/off, rest invariant (unknown → null).</summary>
    public static string? OptValue(AppSettings settings, string name)
    {
        if (!OptProps.TryGetValue(name, out PropertyInfo? p) || p is null)
            return null;
        object? v;
        try
        {
            v = p.GetValue(settings);
        }
        catch
        {
            return null;
        }
        return v is bool b
            ? (b ? "on" : "off")
            : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Looks up a command binding for $(bind:Command): formatted or empty when unbound/unknown.</summary>
    public static string BindVerb(KeyBindingTable table, string name)
    {
        if (!Enum.TryParse<EditorCommand>(name, ignoreCase: true, out EditorCommand cmd))
            return string.Empty;
        foreach (KeyRow r in table.Rows)
            if (r.Command == cmd && r.Key.HasValue)
                return KeyBindings.Format(new KeyStroke(r.Key.Value, r.Alt, r.Ctrl, r.Shift));
        return string.Empty;
    }

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
