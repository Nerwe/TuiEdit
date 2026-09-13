using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Prompt history and Tab-completion helpers.</summary>
public sealed class PromptHistoryTests
{
    [Fact]
    public void PushDedupsAndCaps()
    {
        var h = new PromptHistory();
        h.Push("find", "a");
        h.Push("find", "b");
        h.Push("find", "a");
        Assert.Equal(["a", "b"], h.Get("find"));
        for (int i = 0; i < 60; i++)
            h.Push("find", "x" + i);
        Assert.Equal(PromptHistory.Cap, h.Get("find").Count);
        Assert.Equal("x59", h.Get("find")[0]);
    }

    [Fact]
    public void EmptyPushIgnoredAndKindsIsolated()
    {
        var h = new PromptHistory();
        h.Push("find", "");
        Assert.Empty(h.Get("find"));
        h.Push("find", "a");
        Assert.Empty(h.Get("goto"));
    }

    [Fact]
    public void CompleteCmdlineVerbs()
    {
        (string text, int pos) = PromptComplete.CompleteCmdline("s", 1);
        Assert.Equal("save", text);
        Assert.Equal(4, pos);
    }

    [Fact]
    public void CompleteSetKeys()
    {
        (string text, int pos) = PromptComplete.CompleteCmdline("set th", 6);
        Assert.Equal("set theme", text);
        Assert.Equal(9, pos);
    }

    [Fact]
    public void CompleteCyclesCandidates()
    {
        (string first, int p1) = PromptComplete.CompleteCmdline("set ", 4);
        (string second, _) = PromptComplete.CompleteCmdline(first, p1);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CompleteWordFromCandidates()
    {
        (string text, int pos) = PromptComplete.CompleteWord("he", 2, ["hello", "help", "bye"]);
        Assert.Equal("hello", text);
        Assert.Equal(5, pos);
        (string cycled, _) = PromptComplete.CompleteWord(text, pos, ["hello", "help", "bye"]);
        Assert.Equal("help", cycled);
    }

    [Fact]
    public void NoMatchKeepsText()
    {
        (string text, int pos) = PromptComplete.CompleteCmdline("zzz", 3);
        Assert.Equal("zzz", text);
        Assert.Equal(3, pos);
    }
}
