using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Startup plumbing: argument plans, help/version printers, home shortening.
[Trait("Category", "Integration")]
public sealed class StartupTests(TempDir tmp) : IClassFixture<TempDir>
{
    [Fact]
    public void ParseEmptyAndFlags()
    {
        StartupPlan empty = Startup.ParseArgs([]);
        Assert.False(empty.ShowHelp);
        Assert.False(empty.ShowVersion);
        Assert.Empty(empty.Files);
        Assert.Null(empty.StartDir);

        Assert.True(Startup.ParseArgs(["--help"]).ShowHelp);
        Assert.True(Startup.ParseArgs(["-h"]).ShowHelp);
        Assert.True(Startup.ParseArgs(["/?"]).ShowHelp);
        Assert.True(Startup.ParseArgs(["--version"]).ShowVersion);
        Assert.True(Startup.ParseArgs(["-v"]).ShowVersion);
    }

    [Fact]
    public void FirstOfHelpVersionWins()
    {
        Assert.True(Startup.ParseArgs(["-v", "-h"]).ShowVersion);
        Assert.False(Startup.ParseArgs(["-v", "-h"]).ShowHelp);
        Assert.True(Startup.ParseArgs(["-h", "-v"]).ShowHelp);
        Assert.False(Startup.ParseArgs(["-h", "-v"]).ShowVersion);
    }

    [Fact]
    public void ParseFilesAndDirs()
    {
        string dir = tmp.NewDir("start");
        StartupPlan plan = Startup.ParseArgs(["a.txt:10:2", dir, "--unknown", "b.txt"]);
        Assert.Equal(2, plan.Files.Count);
        Assert.Equal(("a.txt", 10, 2), plan.Files[0]);
        Assert.Equal(("b.txt", 0, 0), plan.Files[1]);
        Assert.Equal(dir, plan.StartDir);
    }

    [Fact]
    public void PrintVersionMentionsApp()
    {
        string output = CaptureOut(Startup.PrintVersion);
        Assert.Contains("TuiEdit", output, StringComparison.Ordinal);
        Assert.Contains(TuiEditor.AppVersion, output, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintHelpListsCommands()
    {
        string output = CaptureOut(() => Startup.PrintHelp(Loc.Load("en"), "settings.json"));
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
        Assert.Contains("Hotkeys:", output, StringComparison.Ordinal);
        Assert.Contains("settings.json", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortenHomeReplacesPrefix()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            return;
        Assert.Equal(
            "%USERPROFILE%" + Path.DirectorySeparatorChar + "x",
            Startup.ShortenHome(Path.Combine(home, "x")));
        Assert.Equal(@"C:\other", Startup.ShortenHome(@"C:\other"));
    }

    [Fact]
    public void RestoreTerminalNeverThrows()
    {
        var ex = Record.Exception(Startup.RestoreTerminal);
        Assert.Null(ex);
    }

    private static string CaptureOut(Action action)
    {
        var sb = new System.Text.StringBuilder();
        TextWriter old = Console.Out;
        try
        {
            using var writer = new StringWriter(sb);
            Console.SetOut(writer);
            action();
        }
        finally
        {
            Console.SetOut(old);
        }
        return sb.ToString();
    }
}
