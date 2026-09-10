using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Pure color math: ANSI sequences, blending, nearest console color.
public sealed class RgbTests
{
    [Fact]
    public void AnsiSequences()
    {
        var c = new Rgb(1, 2, 3);
        Assert.Equal("\x1b[38;2;1;2;3m", c.ToAnsiFg());
        Assert.Equal("\x1b[48;2;1;2;3m", c.ToAnsiBg());
    }

    [Theory]
    [InlineData(0.0, 10, 20, 30)]
    [InlineData(1.0, 110, 120, 130)]
    [InlineData(0.5, 60, 70, 80)]
    [InlineData(-1.0, 10, 20, 30)] // clamped
    [InlineData(2.0, 110, 120, 130)] // clamped
    public void BlendEndpoints(double t, byte r, byte g, byte b)
    {
        var mixed = new Rgb(10, 20, 30).Blend(new Rgb(110, 120, 130), t);
        Assert.Equal(new Rgb(r, g, b), mixed);
    }

    [Theory]
    [InlineData(0, 0, 0, ConsoleColor.Black)]
    [InlineData(255, 255, 255, ConsoleColor.White)]
    [InlineData(255, 0, 0, ConsoleColor.Red)]
    [InlineData(0, 0, 100, ConsoleColor.DarkBlue)]
    [InlineData(200, 200, 200, ConsoleColor.Gray)]
    public void NearestConsoleColor(byte r, byte g, byte b, ConsoleColor want)
    {
        Assert.Equal(want, new Rgb(r, g, b).ToConsoleColor());
    }
}
