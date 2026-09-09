using System.Reflection;

namespace TuiEdit;

/// <summary>
/// Represents a custom theme from config: name, base built-in theme,
/// and role overrides with #rrggbb colors. Falls back to the base theme when unset.
/// </summary>
public sealed class ThemeScheme
{
    public string Name { get; set; } = string.Empty;

    public string? Base { get; set; }

    /// <summary>Gets or sets role (Theme field name) to #rrggbb color mappings.</summary>
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>Tries to parse #rrggbb (the hash is optional, #rgb is allowed too).</summary>
    internal static bool TryParseHex(string? s, out Rgb rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;
        string h = s.Trim().TrimStart('#');
        if (h.Length == 3)
            h = new string([h[0], h[0], h[1], h[1], h[2], h[2]]);
        if (h.Length != 6)
            return false;
        try
        {
            rgb = new Rgb(
                Convert.ToByte(h.Substring(0, 2), 16),
                Convert.ToByte(h.Substring(2, 2), 16),
                Convert.ToByte(h.Substring(4, 2), 16));
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Provides the theme catalog: built-in plus custom themes from settings.</summary>
public static class ThemeCatalog
{
    /// <summary>Lists names for enumeration: built-in first, then custom.</summary>
    public static List<string> Names(AppSettings settings)
    {
        var names = new List<string>(Themes.Names);
        foreach (ThemeScheme s in settings.Themes)
        {
            if (string.IsNullOrWhiteSpace(s.Name))
                continue;
            if (!names.Any(n => string.Equals(n, s.Name, StringComparison.OrdinalIgnoreCase)))
                names.Add(s.Name);
        }
        return names;
    }

    /// <summary>Gets the display label in settings: built-in names are localized, custom names stay as-is.</summary>
    public static string DisplayName(Loc loc, string name) => name switch
    {
        "light" => loc["settings.light"],
        "dark" => loc["settings.dark"],
        _ => name,
    };

    public static bool Contains(AppSettings settings, string? name) =>
        Names(settings).Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds a theme: applies the custom scheme over the base and ignores invalid values.</summary>
    public static Theme Resolve(AppSettings settings, string? name)
    {
        ThemeScheme? scheme = settings.Themes.FirstOrDefault(
            s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (scheme is null)
            return Themes.Get(name);
        Theme @base = Themes.Get(scheme.Base);
        var overrides = new Dictionary<string, string>(scheme.Colors, StringComparer.OrdinalIgnoreCase);
        var baseRgb = typeof(Theme).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(Rgb))
            .ToDictionary(p => p.Name, p => (Rgb)p.GetValue(@base)!,
                StringComparer.OrdinalIgnoreCase);
        var ctor = typeof(Theme).GetConstructors().Single();
        var pars = ctor.GetParameters();
        object?[] args = new object?[pars.Length];
        for (int i = 0; i < pars.Length; i++)
        {
            if (pars[i].ParameterType == typeof(string))
            {
                args[i] = scheme.Name;
            }
            else if (pars[i].ParameterType == typeof(Rgb)
                && overrides.TryGetValue(pars[i].Name ?? string.Empty, out string? hex)
                && ThemeScheme.TryParseHex(hex, out Rgb custom))
            {
                args[i] = custom;
            }
            else if (pars[i].ParameterType == typeof(Rgb)
                && baseRgb.TryGetValue(pars[i].Name ?? string.Empty, out Rgb bv))
            {
                args[i] = bv;
            }
            else
            {
                return @base; // The Theme struct changed — returns the base theme as-is
            }
        }
        return (Theme)ctor.Invoke(args);
    }
}
