using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Modular highlighting: grammars, tokenizer, cache, registry.</summary>
public sealed class SyntaxTests
{
    private static readonly string SeedDir =
        Path.Combine(Path.GetTempPath(), "TuiEdit.Tests", "grammars");

    static SyntaxTests() => GrammarRegistry.UseDirForTests(SeedDir);

    private static TextBuffer Buf(params string[] lines)
    {
        string p = Path.GetTempFileName();
        File.WriteAllLines(p, lines);
        var b = new TextBuffer(null);
        b.Open(p);
        File.Delete(p);
        return b;
    }

    private static CompiledGrammar Cs() => GrammarRegistry.ForExtension(".cs")!;

    private static string ScopeAt(IReadOnlyList<SyntaxToken> toks, int col)
    {
        foreach (var t in toks)
            if (col >= t.Start && col < t.Start + t.Length)
                return t.Scope;
        return "?";
    }

    [Fact]
    public void EmbeddedGrammarsLoad()
    {
        Assert.Equal("C#", GrammarRegistry.ForExtension(".cs")?.Name);
        Assert.Equal("C#", GrammarRegistry.ForExtension(".CS")?.Name);
        Assert.Equal("Python", GrammarRegistry.ForExtension(".py")?.Name);
        Assert.Equal("JSON", GrammarRegistry.ForExtension(".json")?.Name);
        Assert.Equal("JavaScript", GrammarRegistry.ForExtension(".ts")?.Name);
        Assert.Null(GrammarRegistry.ForExtension(".xyz"));
        Assert.Null(GrammarRegistry.ForExtension(""));
        Assert.Equal("JSON", GrammarRegistry.ByName("json")?.Name);
        Assert.Null(GrammarRegistry.ByName("nope"));
    }

    [Fact]
    public void NewGrammarsLoad()
    {
        Assert.Equal("Markdown", GrammarRegistry.ForExtension(".md")?.Name);
        Assert.Equal("PowerShell", GrammarRegistry.ForExtension(".PS1")?.Name);
        Assert.Equal("XML", GrammarRegistry.ForExtension(".xml")?.Name);
        Assert.Equal("XML", GrammarRegistry.ForExtension(".csproj")?.Name);
        Assert.Equal("INI", GrammarRegistry.ForExtension(".ini")?.Name);
        Assert.Equal("//", GrammarRegistry.ForExtension(".cs")?.LineComment);
        Assert.Equal("#", GrammarRegistry.ForExtension(".py")?.LineComment);
        Assert.Equal("#", GrammarRegistry.ForExtension(".ps1")?.LineComment);
        Assert.Equal(";", GrammarRegistry.ForExtension(".ini")?.LineComment);
        Assert.Equal("", GrammarRegistry.ForExtension(".json")?.LineComment);
        var hl = new SyntaxHighlighter();
        var md = Buf("# Title", "`code` and **bold**", "- item");
        Assert.Equal("keyword", ScopeAt(hl.GetLine(md, GrammarRegistry.ByName("Markdown")!, 0), 0));
        Assert.Equal("string", ScopeAt(hl.GetLine(md, GrammarRegistry.ByName("Markdown")!, 1), 0));
    }

    [Fact]
    public void DefaultsSeededToDir()
    {
        GrammarRegistry.EnsureLoaded();
        Assert.True(File.Exists(Path.Combine(SeedDir, "csharp.json")));
        Assert.True(File.Exists(Path.Combine(SeedDir, "powershell.json")));
        Assert.Contains("\"Name\": \"C#\"", File.ReadAllText(Path.Combine(SeedDir, "csharp.json")));
    }

    [Fact]
    public void EditedSeedFileWinsOverEmbedded()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TuiEdit.Tests", "override");
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            GrammarRegistry.UseDirForTests(dir);
            GrammarRegistry.EnsureLoaded();
            Assert.Equal("Python", GrammarRegistry.ForExtension(".py")?.Name);
            File.WriteAllText(Path.Combine(dir, "python.json"), """
                { "Name": "Mine", "Extensions": [".py"],
                  "Rules": [ { "Scope": "keyword", "Match": "x" } ] }
                """);
            GrammarRegistry.ResetForTests();
            GrammarRegistry.EnsureLoaded();
            Assert.Equal("Mine", GrammarRegistry.ForExtension(".py")?.Name);
            Assert.Contains("Mine", File.ReadAllText(Path.Combine(dir, "python.json")));
        }
        finally
        {
            GrammarRegistry.UseDirForTests(SeedDir);
        }
    }

    [Fact]
    public void CSharpLine()
    {
        var hl = new SyntaxHighlighter();
        var b = Buf("using System;", "string s = \"a//b\"; // tail", "int n = 42;", "Console.Write(x);");
        Assert.Equal("keyword", ScopeAt(hl.GetLine(b, Cs(), 0), 0));
        var t1 = hl.GetLine(b, Cs(), 1);
        Assert.Equal("keyword", ScopeAt(t1, 0));
        Assert.Equal("string", ScopeAt(t1, 11)); // "a//b" — string beats comment
        Assert.Equal("comment", ScopeAt(t1, 19));
        var t2 = hl.GetLine(b, Cs(), 2);
        Assert.Equal("keyword", ScopeAt(t2, 0));
        Assert.Equal("number", ScopeAt(t2, 8));
        var t3 = hl.GetLine(b, Cs(), 3);
        Assert.Equal("type", ScopeAt(t3, 0));
    }

    [Fact]
    public void BlockCommentMultiline()
    {
        var hl = new SyntaxHighlighter();
        var b = Buf("int a;", "/* open", "code();", "*/ done", "int y;");
        Assert.Equal("keyword", ScopeAt(hl.GetLine(b, Cs(), 0), 0));
        Assert.Equal("comment", ScopeAt(hl.GetLine(b, Cs(), 1), 0));
        Assert.Equal("comment", ScopeAt(hl.GetLine(b, Cs(), 2), 2));
        Assert.Equal("comment", ScopeAt(hl.GetLine(b, Cs(), 3), 0));
        Assert.Equal("keyword", ScopeAt(hl.GetLine(b, Cs(), 4), 0));
    }

    [Fact]
    public void CacheInvalidatesDownstream()
    {
        var hl = new SyntaxHighlighter();
        var b = Buf("code", "/*", "more");
        Assert.Equal("comment", ScopeAt(hl.GetLine(b, Cs(), 2), 0));
        b.InsertChar(1, 0, '/'); // "/*" -> "//*" — block was closed originally, became line comment
        Assert.Equal("", ScopeAt(hl.GetLine(b, Cs(), 2), 0)); // recomputed below
    }

    [Fact]
    public void PythonTripleString()
    {
        var hl = new SyntaxHighlighter();
        var py = GrammarRegistry.ForExtension(".py")!;
        var b = Buf("def f():", "\"\"\"doc", "still # not comment", "\"\"\"", "x = 1 # tail");
        Assert.Equal("keyword", ScopeAt(hl.GetLine(b, py, 0), 0));
        Assert.Equal("string", ScopeAt(hl.GetLine(b, py, 1), 0));
        Assert.Equal("string", ScopeAt(hl.GetLine(b, py, 2), 8)); // # inside the string
        var t4 = hl.GetLine(b, py, 4);
        Assert.Equal("number", ScopeAt(t4, 4));
        Assert.Equal("comment", ScopeAt(t4, 6));
    }

    [Fact]
    public void NoGrammarOrRangeGivesEmpty()
    {
        var hl = new SyntaxHighlighter();
        var b = Buf("x");
        Assert.Empty(hl.GetLine(b, null, 0));
        Assert.Empty(hl.GetLine(b, Cs(), -1));
        Assert.Empty(hl.GetLine(b, Cs(), 99));
    }

    [Fact]
    public void VersionBumpsOnMutations()
    {
        var b = new TextBuffer(null);
        Assert.Equal(0, b.Version);
        b.InsertChar(0, 0, 'x');
        Assert.Equal(1, b.Version);
        b.Undo();
        Assert.Equal(2, b.Version);
        b.Redo();
        Assert.Equal(3, b.Version);
        b.RestoreContent(new List<string> { "z" });
        Assert.Equal(4, b.Version);
        b.Clear();
        Assert.Equal(5, b.Version);
    }

    [Fact]
    public void UserPlugin()
    {
        Assert.True(GrammarRegistry.AddJson("""
            { "Name": "TestLang", "Extensions": [".testlang"],
              "Rules": [ { "Scope": "keyword", "Match": "\\bTODO\\b" } ] }
            """));
        Assert.Equal("TestLang", GrammarRegistry.ForExtension(".testlang")?.Name);
        Assert.Equal("TestLang", GrammarRegistry.ByName("testlang")?.Name);
        // Repeated name and extension are overridden (without affecting built-ins).
        Assert.True(GrammarRegistry.AddJson("""
            { "Name": "TestLang", "Extensions": [".testlang"],
              "Rules": [ { "Scope": "number", "Match": "\\d+" } ] }
            """));
        var tl = GrammarRegistry.ForExtension(".testlang")!;
        Assert.Single(tl.Rules);
        Assert.Equal("number", tl.Rules[0].Scope);
    }

    [Fact]
    public void BadPluginSkipped()
    {
        Assert.False(GrammarRegistry.AddJson("{not json"));
        Assert.False(GrammarRegistry.AddJson("""{ "Name": "", "Rules": [] }"""));
        Assert.False(GrammarRegistry.AddJson("""{ "Name": "Empty", "Rules": [] }"""));
        // Broken rule skipped, good one works.
        Assert.True(GrammarRegistry.AddJson("""
            { "Name": "HalfBad", "Extensions": [".halfbad"],
              "Rules": [
                { "Scope": "keyword", "Match": "([" },
                { "Scope": "", "Match": "x" },
                { "Scope": "number", "Match": "\\d+" } ] }
            """));
        var g = GrammarRegistry.ByName("HalfBad")!;
        Assert.Single(g.Rules);
    }

    [Fact]
    public void CatastrophicPatternSafe()
    {
        GrammarRegistry.AddJson("""
            { "Name": "Evil", "Extensions": [".evil"],
              "Rules": [ { "Scope": "keyword", "Match": "^(a+)+$" } ] }
            """);
        var g = GrammarRegistry.ByName("Evil")!;
        var hl = new SyntaxHighlighter();
        var b = Buf(new string('a', 32) + "!");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var toks = hl.GetLine(b, g, 0);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal("", ScopeAt(toks, 0)); // timeout — plain text
    }

    [Fact]
    public void ScopeColors()
    {
        Theme dark = Themes.Dark;
        Assert.Equal(dark.SynKeywordFg, SyntaxHighlighter.ScopeColor(dark, "keyword"));
        Assert.Equal(dark.SynStringFg, SyntaxHighlighter.ScopeColor(dark, "string"));
        Assert.Equal(dark.SynCommentFg, SyntaxHighlighter.ScopeColor(dark, "comment"));
        Assert.Equal(dark.SynNumberFg, SyntaxHighlighter.ScopeColor(dark, "number"));
        Assert.Equal(dark.SynTypeFg, SyntaxHighlighter.ScopeColor(dark, "type"));
        Assert.Null(SyntaxHighlighter.ScopeColor(dark, ""));
        Assert.Null(SyntaxHighlighter.ScopeColor(dark, "nope"));
        // A custom theme recolors scope roles.
        var s = new AppSettings();
        s.Themes.Add(new ThemeScheme
        {
            Name = "Mine",
            Colors = new Dictionary<string, string> { ["SynKeywordFg"] = "#ff0000" },
        });
        Theme t = ThemeCatalog.Resolve(s, "Mine");
        Assert.Equal(new Rgb(0xFF, 0, 0), t.SynKeywordFg);
        Assert.Equal(Themes.Dark.SynStringFg, t.SynStringFg);
        Assert.Equal(new Rgb(0xFF, 0, 0), SyntaxHighlighter.ScopeColor(t, "keyword"));
    }

    [Fact]
    public void GrammarSettingDefaultsAuto()
    {
        var s = new AppSettings();
        Assert.Equal("auto", s.Grammar);
        s.Grammar = "  ";
        s.Normalize();
        Assert.Equal("auto", s.Grammar);
    }

    // --- Incremental cache: check against full recompute (early Ensure exit). ---

    private static List<List<SyntaxToken>> SnapshotAll(TextBuffer buf, CompiledGrammar g)
    {
        var fresh = new SyntaxHighlighter();
        var rows = new List<List<SyntaxToken>>();
        for (int r = 0; r < buf.Count; r++)
            rows.Add(new List<SyntaxToken>(fresh.GetLine(buf, g, r)));
        return rows;
    }

    private static void AssertSameAsFresh(TextBuffer buf, SyntaxHighlighter hl, CompiledGrammar g)
    {
        List<List<SyntaxToken>> want = SnapshotAll(buf, g);
        Assert.Equal(buf.Count, want.Count);
        for (int r = 0; r < buf.Count; r++)
            Assert.Equal(want[r], hl.GetLine(buf, g, r));
    }

    private static TextBuffer BlockBuf() => Buf(
        "int a = 1;",
        "/* open block",
        "int b = 2;",
        "int c = 3;",
        "int d = 4;");

    [Fact]
    public void IncrementalEditAboveBlockKeepsComment()
    {
        var b = BlockBuf();
        var g = Cs();
        var hl = new SyntaxHighlighter();
        Assert.Equal("comment", ScopeAt(hl.GetLine(b, g, 2), 0)); // cache warm-up
        b.InsertChar(0, 0, '/'); // edit above the block
        AssertSameAsFresh(b, hl, g);
        Assert.Equal("comment", ScopeAt(hl.GetLine(b, g, 2), 0)); // state propagated down
        b.Undo();
        AssertSameAsFresh(b, hl, g);
    }

    [Fact]
    public void IncrementalCloseBlockReconverges()
    {
        var b = BlockBuf();
        var g = Cs();
        var hl = new SyntaxHighlighter();
        Assert.Equal("comment", ScopeAt(hl.GetLine(b, g, 4), 0));
        b.InsertString(1, 12, " */"); // closed the block on line 1
        AssertSameAsFresh(b, hl, g);
        Assert.Equal("keyword", ScopeAt(hl.GetLine(b, g, 2), 0)); // code is code again
        b.Undo();
        AssertSameAsFresh(b, hl, g);
        Assert.Equal("comment", ScopeAt(hl.GetLine(b, g, 2), 0));
    }

    [Fact]
    public void IncrementalInsertDeleteLine()
    {
        var b = BlockBuf();
        var g = Cs();
        var hl = new SyntaxHighlighter();
        hl.GetLine(b, g, 4); // warm-up
        b.InsertString(0, 0, "int z = 0;\n"); // +line on top (shift)
        Assert.Equal(6, b.Count);
        AssertSameAsFresh(b, hl, g);
        Assert.Equal("comment", ScopeAt(hl.GetLine(b, g, 3), 0));
        b.CutLine(0); // line removal (shift back)
        Assert.Equal(5, b.Count);
        AssertSameAsFresh(b, hl, g);
    }

    [Fact]
    public void IncrementalReplaceAllMultiHunk()
    {
        var b = Buf("int a = 1;", "int b = 1;", "/* x */", "int c = 1;");
        var g = Cs();
        var hl = new SyntaxHighlighter();
        hl.GetLine(b, g, 3); // warm-up
        // Several hotspots in one version (single undo step): early exit
        // between hotspots must recheck the tail, not glue the old one.
        Assert.Equal(3, b.ReplaceAll("1", "22", 0, 0, true, false));
        AssertSameAsFresh(b, hl, g);
        Assert.Equal("comment", ScopeAt(hl.GetLine(b, g, 2), 0));
        Assert.Equal("number", ScopeAt(hl.GetLine(b, g, 3), 8));
        Assert.Equal("int c = 22;", b.GetLine(3));
        b.Undo();
        AssertSameAsFresh(b, hl, g);
    }

    [Fact]
    public void IncrementalSortKeepsHighlight()
    {
        var b = Buf("int z = 3;", "int a = 1;", "int m = 2;");
        var g = Cs();
        var hl = new SyntaxHighlighter();
        hl.GetLine(b, g, 2); // warm-up
        b.SortLines(0, 2);
        AssertSameAsFresh(b, hl, g);
        Assert.Equal("int a = 1;", b.GetLine(0));
    }
}
