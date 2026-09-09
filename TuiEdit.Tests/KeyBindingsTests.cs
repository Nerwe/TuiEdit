using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Hotkey notation: parser, formatter, loading, seeding.</summary>
public sealed class KeyBindingsTests
{
    [Theory]
    [InlineData("Ctrl+S", ConsoleKey.S, false, true, false)]
    [InlineData("ctrl+shift+F3", ConsoleKey.F3, false, true, true)]
    [InlineData("Alt+/", ConsoleKey.Oem2, true, false, false)]
    [InlineData("alt+.", ConsoleKey.OemPeriod, true, false, false)]
    [InlineData("F9", ConsoleKey.F9, false, false, false)]
    [InlineData("Up", ConsoleKey.UpArrow, false, false, false)]
    [InlineData("Ctrl+5", ConsoleKey.D5, false, true, false)]
    [InlineData("Alt+OemMinus", ConsoleKey.OemMinus, true, false, false)]
    public void ParseValid(string notation, ConsoleKey key, bool alt, bool ctrl, bool shift)
    {
        KeyStroke? s = KeyBindings.Parse(notation);
        Assert.NotNull(s);
        Assert.Equal(new KeyStroke(key, alt, ctrl, shift), s.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Win+S")]
    [InlineData("Ctrl+Nope")]
    [InlineData("S")] // bare letter — typing protection
    [InlineData("Space")] // bare space — typing protection
    [InlineData("Enter")]
    [InlineData("OemMinus")] // bare minus — typing protection
    [InlineData("Ctrl+Alt")] // no key
    public void ParseInvalid(string? notation)
    {
        Assert.Null(KeyBindings.Parse(notation));
    }

    [Theory]
    [InlineData(ConsoleKey.S, false, true, false, "^S")]
    [InlineData(ConsoleKey.S, false, true, true, "Ctrl+Shift+S")]
    [InlineData(ConsoleKey.Oem2, true, false, false, "Alt+Oem2")]
    [InlineData(ConsoleKey.F9, false, false, false, "F9")]
    [InlineData(ConsoleKey.UpArrow, false, false, false, "Up")]
    public void FormatRoundtrip(ConsoleKey key, bool alt, bool ctrl, bool shift, string want)
    {
        Assert.Equal(want, KeyBindings.Format(new KeyStroke(key, alt, ctrl, shift)));
    }

    [Fact]
    public void LoadSkipsCommentsAndBadJson()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tui_kb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string p = Path.Combine(dir, "keybindings.json");
            File.WriteAllText(p, "{\n// comment\n\"Save\": \"Ctrl+S\",\n\"GoToLine\": null,\n}");
            var map = KeyBindings.Load(p);
            Assert.Equal("Ctrl+S", map["Save"]);
            Assert.Null(map["GoToLine"]);
            Assert.Empty(KeyBindings.Load(Path.Combine(dir, "missing.json")));
            File.WriteAllText(p, "{oops");
            Assert.Empty(KeyBindings.Load(p));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void SeedExampleDoesNotOverride()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tui_kb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string p = Path.Combine(dir, "keybindings.json");
            KeyBindings.SeedExample(p);
            Assert.True(File.Exists(p));
            Assert.Empty(KeyBindings.Load(p)); // all commented out — empty
            File.WriteAllText(p, "{\"Save\": \"Ctrl+S\"}");
            KeyBindings.SeedExample(p);
            Assert.Equal("Ctrl+S", KeyBindings.Load(p)["Save"]); // do not touch foreign entries

        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
