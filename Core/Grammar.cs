using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TuiEdit;

/// <summary>
/// Represents a highlighting rule: a single-line match (Match) or a multiline
/// construct (Begin/End). Scope: keyword, string, comment, number, type.
/// </summary>
internal sealed class GrammarRule
{
    public string Scope { get; set; } = string.Empty;
    public string? Match { get; set; }
    public string? Begin { get; set; }
    public string? End { get; set; }
}

/// <summary>
/// Represents a language JSON grammar: name, extensions, case handling, and ordered rules.
/// Earlier in the list means higher priority on ties, otherwise the earliest match wins.
/// </summary>
internal sealed class Grammar
{
    public string Name { get; set; } = string.Empty;
    public List<string> Extensions { get; set; } = new();
    public bool IgnoreCase { get; set; }
    public string? LineComment { get; set; }
    public List<GrammarRule> Rules { get; set; } = new();
}

/// <summary>Represents a compiled rule (invalid patterns already discarded).</summary>
internal sealed record CompiledRule(string Scope, Regex? Match, Regex? Begin, Regex? End)
{
    public bool IsMultiline => Begin is not null;
}

/// <summary>Represents a compiled grammar.</summary>
internal sealed class CompiledGrammar
{
    public string Name { get; }
    public string LineComment { get; } = string.Empty;
    public List<string> Extensions { get; } = new();
    public List<CompiledRule> Rules { get; } = new();

    public CompiledGrammar(string name, string lineComment = "")
    {
        Name = name;
        LineComment = lineComment;
    }
}

/// <summary>Provides the grammar registry: a folder next to settings.json (overrides built-in grammars).</summary>
internal static class GrammarRegistry
{
    private const string ResourcePrefix = "TuiEdit.Grammars.";
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private static bool _loaded;
    private static readonly Dictionary<string, CompiledGrammar> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CompiledGrammar> _byExt =
        new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultDir()
    {
        try
        {
            string? dir = Path.GetDirectoryName(SettingsStore.ResolvePath());
            if (!string.IsNullOrEmpty(dir))
                return Path.Combine(dir, "grammars");
        }
        catch
        {
        }
        return Path.Combine(Path.GetTempPath(), "TuiEdit", "grammars");
    }

    private static string? _dirOverride;

    internal static void UseDirForTests(string dir)
    {
        _dirOverride = dir;
        ResetForTests();
    }

    public static void EnsureLoaded() => EnsureLoaded(_dirOverride ?? DefaultDir());

    internal static void EnsureLoaded(string dir)
    {
        if (_loaded)
            return;
        _loaded = true;
        SeedDefaults(dir);
        if (!LoadDir(dir))
            LoadEmbedded();
    }

    /// <summary>Deploys missing built-in grammars (leaves user edits untouched).</summary>
    private static void SeedDefaults(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            foreach (string name in typeof(GrammarRegistry).Assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                    || !name.EndsWith(".json", StringComparison.Ordinal))
                    continue;
                string target = Path.Combine(dir, name[ResourcePrefix.Length..]);
                if (File.Exists(target))
                    continue;
                using Stream? s = typeof(GrammarRegistry).Assembly.GetManifestResourceStream(name);
                if (s is null)
                    continue;
                using var reader = new StreamReader(s);
                File.WriteAllText(target, reader.ReadToEnd());
            }
        }
        catch
        {
        }
    }

    private static void LoadEmbedded()
    {
        foreach (string name in typeof(GrammarRegistry).Assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !name.EndsWith(".json", StringComparison.Ordinal))
                continue;
            try
            {
                using Stream? s = typeof(GrammarRegistry).Assembly.GetManifestResourceStream(name);
                if (s is null)
                    continue;
                using var reader = new StreamReader(s);
                AddJson(reader.ReadToEnd());
            }
            catch
            {
            }
        }
    }

    /// <summary>Loads a folder (*.json alphabetically, later files override). Returns <see langword="true" /> if at least one grammar loads; otherwise, <see langword="false" />.</summary>
    private static bool LoadDir(string dir)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(dir))
                return false;
            files = Directory.GetFiles(dir, "*.json");
        }
        catch
        {
            return false;
        }
        bool any = false;
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string f in files)
        {
            try
            {
                any |= AddJson(File.ReadAllText(f));
            }
            catch
            {
            }
        }
        return any;
    }

    /// <summary>Adds a grammar from JSON text. Returns <see langword="true" /> if the grammar contains valid rules; otherwise, <see langword="false" />.</summary>
    internal static bool AddJson(string json)
    {
        Grammar? g;
        try
        {
            g = JsonSerializer.Deserialize<Grammar>(json, JsonOptions);
        }
        catch
        {
            return false;
        }
        if (g is null || string.IsNullOrWhiteSpace(g.Name))
            return false;
        var cg = new CompiledGrammar(g.Name.Trim(), (g.LineComment ?? string.Empty).Trim());
        // Compiled: grammars compile once per session but match on every line of every edit —
        // interpreting them per line would be noticeably more expensive.
        RegexOptions opts = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        if (g.IgnoreCase)
            opts |= RegexOptions.IgnoreCase;
        foreach (GrammarRule r in g.Rules)
        {
            if (string.IsNullOrWhiteSpace(r.Scope))
                continue;
            string scope = r.Scope.Trim().ToLowerInvariant();
            Regex? match = TryCompile(r.Match, opts);
            Regex? begin = TryCompile(r.Begin, opts);
            Regex? end = TryCompile(r.End, opts);
            if (match is not null)
                cg.Rules.Add(new CompiledRule(scope, match, null, null));
            else if (begin is not null && end is not null)
                cg.Rules.Add(new CompiledRule(scope, null, begin, end));
        }
        if (cg.Rules.Count == 0)
            return false;
        foreach (string e in g.Extensions)
        {
            string ext = NormalizeExt(e);
            if (ext.Length > 0)
                cg.Extensions.Add(ext);
        }
        _byName[cg.Name] = cg;
        foreach (string ext in cg.Extensions)
            _byExt[ext] = cg;
        return true;
    }

    private static Regex? TryCompile(string? pattern, RegexOptions opts)
    {
        if (string.IsNullOrEmpty(pattern))
            return null;
        try
        {
            return new Regex(pattern, opts, PatternTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal static string NormalizeExt(string e)
    {
        string ext = e.Trim().ToLowerInvariant();
        if (ext.Length == 0)
            return string.Empty;
        return ext.StartsWith('.') ? ext : "." + ext;
    }

    /// <summary>Finds a grammar by file extension (".cs" / "cs", case-insensitive).</summary>
    public static CompiledGrammar? ForExtension(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
            return null;
        EnsureLoaded();
        return _byExt.TryGetValue(NormalizeExt(ext), out CompiledGrammar? g) ? g : null;
    }

    /// <summary>Finds a grammar by language name.</summary>
    public static CompiledGrammar? ByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        EnsureLoaded();
        return _byName.TryGetValue(name.Trim(), out CompiledGrammar? g) ? g : null;
    }

    internal static void ResetForTests()
    {
        _loaded = false;
        _byName.Clear();
        _byExt.Clear();
    }
}
