using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class FoldingTests
{
    private static List<string> Code() => new()
    {
        "def f():",       // 0
        "    x = 1",      // 1
        "    if x:",      // 2
        "        y = 2",  // 3
        "",               // 4
        "    z = 3",      // 5
        "w = 4",          // 6
    };

    [Fact]
    public void RangeAndHidden()
    {
        var lines = Code();
        Assert.True(Folding.CanFold(lines, 0));
        Assert.Equal(5, Folding.EndOf(lines, 0));
        Assert.Equal(3, Folding.EndOf(lines, 2));
        Assert.False(Folding.CanFold(lines, 1));
        Assert.False(Folding.CanFold(lines, 6));
        var folds = new SortedSet<int> { 0 };
        Assert.False(Folding.IsHidden(lines, folds, 0));
        Assert.True(Folding.IsHidden(lines, folds, 1));
        Assert.True(Folding.IsHidden(lines, folds, 5));
        Assert.False(Folding.IsHidden(lines, folds, 6));
        Folding.UnfoldContaining(lines, folds, 3);
        Assert.Empty(folds);
    }

    [Fact]
    public void ToggleFoldAndKey()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(Code());
        var ed = new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")));
        ed.GoToLineNumber(1);
        ed.ToggleFold();
        Assert.True(IsHidden(ed, 3));
        ed.GoToLineNumber(7);
        Assert.True(IsHidden(ed, 3)); // прыжок мимо не трогает
        ed.GoToLineNumber(4);
        Assert.False(IsHidden(ed, 3)); // прыжок внутрь раскрывает
        Assert.Equal(EditorCommand.ToggleFold,
            KeyMap.Map(new ConsoleKeyInfo('-', ConsoleKey.OemMinus, false, true, false)));
    }

    private static bool IsHidden(TuiEditor ed, int row)
    {
        var buf = (TextBuffer)typeof(TuiEditor).GetProperty("_buf",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        var docs = (System.Collections.IList)typeof(TuiEditor).GetProperty("_docs",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ed)!;
        object? tab = docs[0];
        var folds = (SortedSet<int>)tab!.GetType().GetProperty("Folds")!.GetValue(tab)!;
        return Folding.IsHidden(buf.Lines, folds, row);
    }
}
