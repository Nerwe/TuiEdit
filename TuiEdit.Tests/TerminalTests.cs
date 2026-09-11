using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Terminal mode helpers: best-effort, never throw (headless CI included).</summary>
public sealed class TerminalTests
{
    [Fact]
    public void DiscardPendingInputNeverThrows()
    {
        var ex = Record.Exception(() => Terminal.DiscardPendingInput());
        Assert.Null(ex);
    }
}
