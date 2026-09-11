using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Shell filter (:pipe): splitter, runner, and editor replacement.</summary>
public sealed class ShellFilterTests
{
    [Theory]
    [InlineData("sort", "sort", "")]
    [InlineData("sort -r", "sort", "-r")]
    [InlineData("powershell -c \"a b\"", "powershell", "-c a b")]
    [InlineData("  grep   -i  x  ", "grep", "-i x")]
    public void SplitParsesCommandLine(string command, string exe, string argsJoined)
    {
        (string gotExe, List<string> gotArgs) = ShellFilter.Split(command);
        Assert.Equal(exe, gotExe);
        Assert.Equal(argsJoined, string.Join(" ", gotArgs));
    }

    [Fact]
    public void SplitEmptyThrows() =>
        Assert.Throws<InvalidOperationException>(() => ShellFilter.Split("   "));

    [Fact]
    public void SplitLinesTrimsTrailingBlanks()
    {
        Assert.Equal(new List<string> { "a", "", "b" }, ShellFilter.SplitLines("a\n\nb\n\n"));
        Assert.Empty(ShellFilter.SplitLines(""));
        Assert.Empty(ShellFilter.SplitLines("\n"));
    }

    [Fact]
    public void RunEchoRoundtrips()
    {
        string exe = OperatingSystem.IsWindows() ? "cmd" : "/bin/echo";
        var args = OperatingSystem.IsWindows() ? new List<string> { "/c", "echo", "hi" } : new List<string> { "hi" };
        Assert.Equal(new List<string> { "hi" }, ShellFilter.Run(exe, args, "ignored"));
    }

    [Fact]
    public void RunNonzeroThrows()
    {
        string exe = OperatingSystem.IsWindows() ? "cmd" : "sh";
        var args = OperatingSystem.IsWindows()
            ? new List<string> { "/c", "exit", "3" }
            : new List<string> { "-c", "exit 3" };
        var ex = Assert.Throws<InvalidOperationException>(() => ShellFilter.Run(exe, args, ""));
        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public void RunKillsOnTimeout()
    {
        string exe = OperatingSystem.IsWindows() ? "cmd" : "sleep";
        var args = OperatingSystem.IsWindows()
            ? new List<string> { "/c", "ping", "-n", "6", "127.0.0.1" }
            : new List<string> { "5" };
        var ex = Assert.Throws<InvalidOperationException>(() => ShellFilter.Run(exe, args, "", timeoutMs: 800));
        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public void CtrlRMapsToShellFilter()
    {
        var table = new KeyBindingTable(KeyBindingTable.DefaultRows());
        Assert.Equal(EditorCommand.ShellFilter,
            table.Map(new ConsoleKeyInfo('\x12', ConsoleKey.R, shift: false, alt: false, control: true)));
    }

    [Fact]
    public void ReplaceLinesIsSingleUndo()
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string> { "a", "b", "c" });
        b.ReplaceLines(0, 1, new List<string> { "x" });
        Assert.Equal(new List<string> { "x", "c" }, b.Lines);
        b.Undo();
        Assert.Equal(new List<string> { "a", "b", "c" }, b.Lines);
    }

    [Fact]
    public void ReplaceLinesEmptyKeepsOneLine()
    {
        var b = new TextBuffer(null);
        b.RestoreContent(new List<string> { "a" });
        b.ReplaceLines(0, 0, new List<string>());
        Assert.Equal(new List<string> { string.Empty }, b.Lines);
    }

    [Fact]
    public void FilterSortsSelectionInOneUndo()
    {
        var ed = NewEditor("b", "a");
        HandleKey(ed, K('\x01', ConsoleKey.A, ctrl: true)); // select all
        ed.ShellFilterWith("sort");
        Assert.Equal(new List<string> { "a", "b" }, Lines(ed));
        HandleKey(ed, K('\x1a', ConsoleKey.Z, ctrl: true)); // undo
        Assert.Equal(new List<string> { "b", "a" }, Lines(ed));
    }

    [Fact]
    public void FilterFailureKeepsText()
    {
        var ed = NewEditor("b", "a");
        HandleKey(ed, K('\x01', ConsoleKey.A, ctrl: true));
        string cmd = OperatingSystem.IsWindows() ? "cmd /c exit 3" : "sh -c 'exit 3'";
        ed.ShellFilterWith(cmd);
        Assert.Equal(new List<string> { "b", "a" }, Lines(ed));
    }

    private static TuiEditor NewEditor(params string[] lines)
    {
        var buf = new TextBuffer(null);
        buf.RestoreContent(new List<string>(lines));
        return new TuiEditor(buf, new AppSettings(),
            new SettingsStore(Path.Combine("x", "settings.json")));
    }

    private static void HandleKey(TuiEditor ed, ConsoleKeyInfo k) =>
        typeof(TuiEditor).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ed, [k]);

    private static List<string> Lines(TuiEditor ed)
    {
        object? buf = typeof(TuiEditor).GetProperty("_buf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ed);
        return (List<string>)buf!.GetType().GetProperty("Lines")!.GetValue(buf)!;
    }

    private static ConsoleKeyInfo K(char ch, ConsoleKey key, bool ctrl = false) =>
        new(ch, key, shift: false, alt: false, control: ctrl);
}
