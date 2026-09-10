using System.Reflection;
using TuiEdit;
using Xunit;
using Xunit.Abstractions;

namespace TuiEdit.Tests;

// Stress over real directories (read-only walk) plus a synthetic tree driven
// through real keys (destructive ops allowed there).
[Trait("Category", "Integration")]
public sealed class SidebarStressTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _tmp;

    public SidebarStressTests(ITestOutputHelper output)
    {
        _out = output;
        _tmp = Path.Combine(Path.GetTempPath(), "tui_sbst_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tmp, "deep", "deeper", "deepest"));
        Directory.CreateDirectory(Path.Combine(_tmp, ".hdir"));
        for (int i = 0; i < 60; i++)
            File.WriteAllText(Path.Combine(_tmp, $"f{i:D2}.txt"), "x");
        File.WriteAllText(Path.Combine(_tmp, "deep", "deeper", "deepest", "leaf.txt"), "x");
        File.WriteAllText(Path.Combine(_tmp, ".hidden"), "x");
        File.WriteAllText(Path.Combine(_tmp, "run.bat"), "x");
        File.WriteAllText(Path.Combine(_tmp, "uni-файл-日本語.txt"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Fact]
    public void ProbeRealDirs()
    {
        string[] roots =
        [
            @"D:\Projects\TuiEdit",
            @"C:\",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Directory.GetCurrentDirectory(),
        ];
        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            _out.WriteLine("ROOT: " + root);
            var ed = NewEditor();
            var ex = Record.Exception(() =>
            {
                ed.OpenSidebarRoot(root);
                var sb = (SidebarState)Field(ed, "_sidebar")!;
                _out.WriteLine($"ROWS: {sb.Rows.Count}");
                Walk(sb, 200);
                Draw(ed);
            });
            Assert.Null(ex);
        }
    }

    [Fact]
    public void KeysWalkSyntheticTree()
    {
        var ed = NewEditor();
        var ex = Record.Exception(() =>
        {
            ed.OpenSidebarRoot(_tmp);
            WalkKeys(ed, 500);
            Draw(ed);
        });
        Assert.Null(ex);
        Assert.True(Directory.Exists(_tmp)); // the tree root itself survives
    }

    private static void Walk(SidebarState sb, int steps)
    {
        var rng = new Random(7);
        for (int i = 0; i < steps && sb.Rows.Count > 0; i++)
        {
            switch (rng.Next(8))
            {
                case 0: sb.MoveHighlight(1, 10); break;
                case 1: sb.MoveHighlight(-1, 10); break;
                case 2: sb.MoveHighlight(5, 10); break;
                case 3: sb.EnterSelected(); break;
                case 4: sb.ExpandSelected(); break;
                case 5: sb.CollapseOrParent(); break;
                case 6: sb.Refresh(); break;
                default: sb.SelectPath(sb.Rows[sb.Selected].node.Path); break;
            }
        }
    }

    private static void WalkKeys(TuiEditor ed, int steps)
    {
        var rng = new Random(99);
        var keys = new[]
        {
            new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.PageUp, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.F8, false, false, false),
        };
        var handleKey = typeof(TuiEditor).GetMethod("HandleKey",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var focusField = typeof(TuiEditor).GetField("_sidebarFocus",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 0; i < steps; i++)
        {
            // Keep focus in the panel (Enter on a file leaves it).
            focusField.SetValue(ed, true);
            handleKey.Invoke(ed, [keys[rng.Next(keys.Length)]]);
        }
    }

    private static void Draw(TuiEditor ed)
    {
        var scr = (Screen)typeof(TuiEditor)
            .GetField("_screen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        scr.Resize(60, 20);
        typeof(TuiEditor).GetMethod("DrawSidebar", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [2, 17]);
    }

    private static TuiEditor NewEditor() =>
        new(new TextBuffer(null), new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")));

    private static object? Field(object o, string name) =>
        typeof(TuiEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
}
