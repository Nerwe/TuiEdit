using BenchmarkDotNet.Attributes;
using TuiEdit;

namespace TuiEdit.Bench;

/// <summary>
/// Frame composition cost (headless <see cref="TuiEditor.RenderFrame"/> at 120x30):
/// cursor depth is the variable — held-Down must not degrade linearly.
/// Run with <c>dotnet run -c Release --project TuiEdit.Bench -- --filter 'Frame*'</c>.
/// </summary>
[MemoryDiagnoser]
public class FrameBenchmarks
{
    private TuiEditor _top = null!;
    private TuiEditor _deep = null!;
    private TuiEditor _deepWrap = null!;

    /// <summary>Builds deterministic editors once (not measured).</summary>
    [GlobalSetup]
    public void Setup()
    {
        _top = MakeEditor(cursorRow: 5, wrap: false);
        _deep = MakeEditor(cursorRow: 5500, wrap: false);
        _deepWrap = MakeEditor(cursorRow: 5500, wrap: true);
    }

    private static TuiEditor MakeEditor(int cursorRow, bool wrap)
    {
        var lines = new List<string>();
        for (int i = 0; i < 6000; i++)
        {
            lines.Add($"// comment line {i}");
            lines.Add($"var value{i} = \"text {i}\"; // trailing {i}");
        }
        var buf = new TextBuffer(null);
        buf.RestoreContent(lines);
        var settings = new AppSettings { WordWrap = wrap };
        var ed = new TuiEditor(buf, settings,
            new SettingsStore(Path.Combine("x", "settings.json")));
        ed.GoToLineNumber(Math.Min(cursorRow + 1, buf.Count));
        ed.RenderFrame(120, 30); // warm caches once
        return ed;
    }

    /// <summary>Frame with the cursor near the top.</summary>
    [Benchmark]
    public void FrameCursorTop() => _top.RenderFrame(120, 30);

    /// <summary>Frame with the cursor 5500 rows deep.</summary>
    [Benchmark]
    public void FrameCursorDeep() => _deep.RenderFrame(120, 30);

    /// <summary>Frame with the cursor deep plus word wrap on.</summary>
    [Benchmark]
    public void FrameCursorDeepWrap() => _deepWrap.RenderFrame(120, 30);
}
