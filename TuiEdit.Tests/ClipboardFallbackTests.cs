using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Clipboard fallback chain: OSC 52, then platform tools, then internal-only.</summary>
public sealed class ClipboardFallbackTests : IDisposable
{
    private readonly Func<string, string?> _prevEnv;
    private readonly Func<string, bool> _prevWriter;
    private readonly Func<string, string[], string, bool> _prevRunner;

    public ClipboardFallbackTests()
    {
        _prevEnv = SystemClipboard.Env;
        _prevWriter = SystemClipboard.Osc52Writer;
        _prevRunner = SystemClipboard.ToolRunner;
    }

    public void Dispose()
    {
        SystemClipboard.Env = _prevEnv;
        SystemClipboard.Osc52Writer = _prevWriter;
        SystemClipboard.ToolRunner = _prevRunner;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EmptyExportSkips()
    {
        SystemClipboard.Env = _ => null;
        Assert.Equal(ClipboardExportResult.SkippedEmpty,
            SystemClipboard.TryExportWithResult([]));
    }

    [Fact]
    public void Osc52WinsWhenSupported()
    {
        SystemClipboard.Env = name => name == "WT_SESSION" ? "1" : null;
        bool wrote = false;
        SystemClipboard.Osc52Writer = _ => { wrote = true; return true; };
        SystemClipboard.ToolRunner = (_, _, _) => { Assert.Fail("tool must not run when OSC 52 works"); return false; };
        Assert.Equal(ClipboardExportResult.ExportedOsc52,
            SystemClipboard.TryExportWithResult(["hi"]));
        Assert.True(wrote);
    }

    [Fact]
    public void ToolFallbackWhenOsc52Unsupported()
    {
        SystemClipboard.Env = _ => null;
        SystemClipboard.Osc52Writer = _ => { Assert.Fail("OSC 52 must not run unsupported"); return false; };
        SystemClipboard.ToolRunner = (_, _, _) => true;
        Assert.Equal(ClipboardExportResult.ExportedTool,
            SystemClipboard.TryExportWithResult(["hi"]));
    }

    [Fact]
    public void ToolFallbackWhenOsc52WriterFails()
    {
        SystemClipboard.Env = name => name == "WT_SESSION" ? "1" : null;
        SystemClipboard.Osc52Writer = _ => false;
        SystemClipboard.ToolRunner = (_, _, _) => true;
        Assert.Equal(ClipboardExportResult.ExportedTool,
            SystemClipboard.TryExportWithResult(["hi"]));
    }

    [Fact]
    public void InternalOnlyWhenEverythingFails()
    {
        SystemClipboard.Env = _ => null;
        SystemClipboard.ToolRunner = (_, _, _) => false;
        Assert.Equal(ClipboardExportResult.InternalOnly,
            SystemClipboard.TryExportWithResult(["hi"]));
    }

    [Fact]
    public void LargeTextSkipsOsc52ForTools()
    {
        SystemClipboard.Env = name => name == "WT_SESSION" ? "1" : null;
        SystemClipboard.Osc52Writer = _ => { Assert.Fail("large text must skip OSC 52"); return false; };
        SystemClipboard.ToolRunner = (_, _, _) => true;
        string big = new('x', 200 * 1024);
        Assert.Equal(ClipboardExportResult.ExportedTool,
            SystemClipboard.TryExportWithResult([big]));
    }

    [Theory]
    [InlineData("KITTY_WINDOW_ID")]
    [InlineData("WEZTERM_PANE")]
    [InlineData("VSCODE_INJECTION")]
    public void KnownTerminalsSupportOsc52(string marker)
    {
        SystemClipboard.Env = name => name == marker ? "1" : null;
        Assert.True(SystemClipboard.Osc52Supported());
    }
}
