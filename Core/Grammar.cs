using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TuiEdit;

/// <summary>
/// Правило подсветки: однострочное совпадение (Match) или многострочная
/// конструкция (Begin/End). Scope: keyword, string, comment, number, type.
/// </summary>
public sealed class GrammarRule
{
    public string Scope { get; set; } = string.Empty;
    public string? Match { get; set; }
    public string? Begin { get; set; }
    public string? End { get; set; }
}

/// <summary>
/// JSON-грамматика языка: имя, расширения, регистр, упорядоченные правила.
/// Ранее в списке — выше приоритет при ничьей, иначе побеждает самое раннее.
/// </summary>
public sealed class Grammar
{
    public string Name { get; set; } = string.Empty;
    public List<string> Extensions { get; set; } = new();
    public bool IgnoreCase { get; set; }
    public List<GrammarRule> Rules { get; set; } = new();
}

/// <summary>Скомпилированное правило (битые шаблоны уже выкинуты).</summary>
internal sealed record CompiledRule(string Scope, Regex? Match, Regex? Begin, Regex? End)
{
    public bool IsMultiline => Begin is not null;
}

/// <summary>Скомпилированная грамматика.</summary>
internal sealed class CompiledGrammar
{
    public string Name { get; }
    public List<string> Extensions { get; } = new();
    public List<CompiledRule> Rules { get; } = new();

    public CompiledGrammar(string name)
    {
        Name = name;
    }
}

/// <summary>Реестр грамматик: встроенные + пользовательские (перекрывают).</summary>
internal static class GrammarRegistry
{
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

    public static string UserDir()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
                return Path.Combine(appData, "TuiEdit", "grammars");
        }
        catch
        {
        }
        return Path.Combine(Path.GetTempPath(), "TuiEdit", "grammars");
    }

    public static string? PortableDir()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "grammars");
            if (Directory.Exists(dir))
                return dir;
        }
        catch
        {
        }
        return null;
    }

    /// <summary>Загрузить всё (повторно — нет): встроенные, затем пользовательские.</summary>
    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        foreach (string name in typeof(GrammarRegistry).Assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith("TuiEdit.Grammars.", StringComparison.Ordinal)
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
        string? portable = PortableDir();
        if (portable is not null)
            LoadDir(portable);
        LoadDir(UserDir());
    }

    private static void LoadDir(string dir)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(dir))
                return;
            files = Directory.GetFiles(dir, "*.json");
        }
        catch
        {
            return;
        }
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string f in files)
        {
            try
            {
                AddJson(File.ReadAllText(f));
            }
            catch
            {
            }
        }
    }

    /// <summary>Добавить грамматику из JSON-текста. Без валидных правил — мимо.</summary>
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
        var cg = new CompiledGrammar(g.Name.Trim());
        RegexOptions opts = RegexOptions.CultureInvariant;
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

    /// <summary>Грамматика по расширению файла (".cs" / "cs", регистр не важен).</summary>
    public static CompiledGrammar? ForExtension(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
            return null;
        EnsureLoaded();
        return _byExt.TryGetValue(NormalizeExt(ext), out CompiledGrammar? g) ? g : null;
    }

    /// <summary>Грамматика по имени языка.</summary>
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
