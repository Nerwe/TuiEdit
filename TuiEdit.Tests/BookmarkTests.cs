using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

public sealed class BookmarkTests
{
    [Fact]
    public void ToggleShiftDropMove()
    {
        var t = new DocTab(new TextBuffer(null));
        Assert.True(t.ToggleBookmark(2));
        Assert.True(t.ToggleBookmark(5));
        Assert.True(t.ToggleBookmark(7));
        Assert.False(t.ToggleBookmark(5));
        Assert.Equal(new[] { 2, 7 }, t.Bookmarks);
        t.ShiftBookmarks(5, 1);
        Assert.Equal(new[] { 2, 8 }, t.Bookmarks);
        t.ShiftBookmarks(3, -1);
        Assert.Equal(new[] { 2, 7 }, t.Bookmarks);
        t.DropBookmarks(2, 2);
        Assert.Equal(new[] { 7 }, t.Bookmarks);
        t.MoveBookmarks(6, 8, -1);
        Assert.Equal(new[] { 6 }, t.Bookmarks);
    }

    [Fact]
    public void EditorToggleAndNextWrap()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string> { "a", "b", "c", "d" });
        var ed = new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")));
        ed.GoToLineNumber(1);
        ed.ToggleBookmark();
        ed.GoToLineNumber(3);
        ed.ToggleBookmark();
        ed.GoToLineNumber(1);
        ed.NextBookmark();
        Assert.Equal(2, Row(ed));
        ed.NextBookmark();
        Assert.Equal(0, Row(ed));
    }

    [Fact]
    public void NextWithoutBookmarksKeepsCursor()
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string> { "a" });
        var ed = new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")));
        ed.NextBookmark();
        Assert.Equal(0, Row(ed));
    }

    private static int Row(TuiEditor ed) =>
        (int)typeof(TuiEditor).GetField("_row", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed)!;
}
