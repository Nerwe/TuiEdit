using BenchmarkDotNet.Attributes;
using TuiEdit;

namespace TuiEdit.Bench;

/// <summary>
/// Hot paths of the editor core: search, highlighting, sorting, and layout.
/// Headless-safe (no console); run with <c>dotnet run -c Release --project TuiEdit.Bench</c>.
/// </summary>
[MemoryDiagnoser]
public class CoreBenchmarks
{
    private TextBuffer _code = new(null);
    private TextBuffer _text = new(null);
    private CompiledGrammar? _grammar;
    private SyntaxHighlighter _highlighter = new();
    private List<string> _files = [];
    private string _longLine = string.Empty;

    /// <summary>Builds deterministic buffers once (not measured).</summary>
    [GlobalSetup]
    public void Setup()
    {
        var code = new List<string>();
        for (int i = 0; i < 500; i++)
        {
            code.Add($"// comment line {i}");
            code.Add($"var value{i} = \"text {i}\"; // trailing {i}");
            code.Add($"int number{i} = {i * 7};");
        }
        _code = new TextBuffer(null);
        _code.RestoreContent(code);

        var text = new List<string>();
        for (int i = 0; i < 2000; i++)
            text.Add($"lorem ipsum dolor sit amet line {i} with needle hidden near the end needle");
        _text = new TextBuffer(null);
        _text.RestoreContent(text);

        _grammar = GrammarRegistry.ForExtension(".cs");
        _highlighter = new SyntaxHighlighter();

        _files = new List<string>();
        for (int i = 0; i < 1000; i++)
            _files.Add($"file{i % 100}-{i}.txt");

        _longLine = new string('x', 400) + "\t" + new string('y', 400);
    }

    /// <summary>Plain-text full scan (count) over a 2000-line buffer.</summary>
    [Benchmark]
    public int FindNextPlain() => _text.CountMatches("needle", true, false);

    /// <summary>Regex full scan (count) over a 2000-line buffer.</summary>
    [Benchmark]
    public int FindNextRegex() => _text.CountMatches(@"line \d+ with", true, false, useRegex: true);

    /// <summary>Single highlight row (incremental cache warm).</summary>
    [Benchmark]
    public int HighlightRow()
    {
        _highlighter.GetLine(_code, _grammar, 750);
        return _highlighter.GetLine(_code, _grammar, 751).Count;
    }

    /// <summary>Full highlight pass over a 1500-line buffer.</summary>
    [Benchmark]
    public int HighlightFull()
    {
        var hl = new SyntaxHighlighter();
        int total = 0;
        for (int r = 0; r < _code.Count; r++)
            total += hl.GetLine(_code, _grammar, r).Count;
        return total;
    }

    /// <summary>Natural sort of 1000 file names.</summary>
    [Benchmark]
    public string SortFiles()
    {
        var copy = new List<string>(_files);
        copy.Sort(NaturalSort.Compare);
        return copy[0];
    }

    /// <summary>Visual slice of a long tabbed line.</summary>
    [Benchmark]
    public string TabSlice() => TabStops.Slice(_longLine, 100, 80);

    /// <summary>Soft-wrap segmentation of a long tabbed line.</summary>
    [Benchmark]
    public int WrapSegments() => WordWrap.CountSegments(_longLine, 80);

    /// <summary>Sequential character typing (with undo history).</summary>
    [Benchmark]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "BenchmarkDotNet requires instance benchmark methods.")]
    public int TypeChars()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string> { string.Empty });
        for (int i = 0; i < 1000; i++)
            buf.InsertChar(0, i, (char)('a' + (i % 26)));
        return buf.GetLine(0).Length;
    }
}
