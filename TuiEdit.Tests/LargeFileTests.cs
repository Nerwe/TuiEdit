using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Large-file guard: confirm dialog over the limit.</summary>
[Trait("Category", "Integration")]
public sealed class LargeFileTests : IDisposable
{
    private readonly string _dir;
    private readonly string _small;
    private readonly string _big;

    public LargeFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tui_large_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _small = Path.Combine(_dir, "small.txt");
        File.WriteAllText(_small, "hi\n");
        _big = Path.Combine(_dir, "big.bin");
        using (FileStream fs = File.Create(_big))
            fs.SetLength(TuiEditor.LargeFileBytes + 1); // sparse, instant
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static ConsoleKeyInfo K(char c, ConsoleKey k) => new(c, k, false, false, false);

    private static TuiEditor NewEditor()
    {
        var buf = new TextBuffer(null);
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine(Path.GetTempPath(), "tui_largecfg_" + Guid.NewGuid().ToString("N"), "s.json")));
    }

    private static void LoadFile(TuiEditor ed, string path) =>
        typeof(TuiEditor).GetMethod("LoadFile", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [path, false]);

    private static object? Get(object o, string name) =>
        typeof(TuiEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);

    private static void HandleKey(TuiEditor ed, ConsoleKeyInfo k) =>
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [k]);

    private static ModalKind DialogKind(TuiEditor ed)
    {
        object dlg = Get(ed, "_dialog")!;
        ModalState state = (ModalState)dlg.GetType()
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dlg)!;
        return state.Kind;
    }

    private static TextBuffer ActiveBuf(TuiEditor ed) =>
        (TextBuffer)typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;

    [Fact]
    public void SmallFileOpensWithoutDialog()
    {
        var ed = NewEditor();
        LoadFile(ed, _small);
        Assert.Null(Get(ed, "_dialog"));
        Assert.Equal(_small, ActiveBuf(ed).FilePath);
    }

    [Fact]
    public void BigFileAsksThenOpensOnYes()
    {
        var ed = NewEditor();
        LoadFile(ed, _big);
        Assert.NotNull(Get(ed, "_dialog"));
        Assert.Equal(ModalKind.LargeFile, DialogKind(ed));
        Assert.Null(ActiveBuf(ed).FilePath); // not open yet
        // Default — "No": Enter rejects.
        HandleKey(ed, K('\0', ConsoleKey.Enter));
        Assert.Null(Get(ed, "_dialog"));
        Assert.Null(ActiveBuf(ed).FilePath);
        // Second attempt + Y hotkey — opens.
        LoadFile(ed, _big);
        Assert.Equal(ModalKind.LargeFile, DialogKind(ed));
        HandleKey(ed, K('y', ConsoleKey.Y));
        Assert.Null(Get(ed, "_dialog"));
        Assert.Equal(_big, ActiveBuf(ed).FilePath);
    }

    [Fact]
    public void LargeFileModalShape()
    {
        var loc = Loc.Load("en");
        var m = ModalState.LargeFile(loc, "big.bin", 17, 16);
        Assert.Equal(ModalKind.LargeFile, m.Kind);
        Assert.True(m.Danger);
        Assert.Equal(2, m.Buttons.Count);
        Assert.Contains("17", m.Lines[0]);
    }
}
