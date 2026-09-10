using System.Text;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Fuzzing the stdin machine: random bursts must never throw, block, get stuck,
// or produce out-of-range coordinates. Deterministic seeds — reproducible in CI.
// Paste-marker tails ("[200~") are excluded here (the paste wait costs 1.5s);
// they are covered by the directed paste facts below.
public sealed class InputFuzzTests
{
    private static readonly char[] Alphabet =
    [
        '[', '<', 'M', 'm', ';', '0', '1', '2', '5', '9', '-', '+', ' ',
        'I', 'O', '~', 'A', '\x1b', '\0', '\n', '\x7f', 'ÿ',
    ];

    private static readonly string[] Coords =
    [
        "0", "1", "2", "3", "10", "32", "35", "64", "65", "-1", "",
        "9999999999999999999999", "+5", " 3", "3 ", "0x10", "1.5",
        "2147483647", "2147483648",
    ];

    public static IEnumerable<object[]> RandomBursts()
    {
        var rng = new Random(20260910);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Count < 3000)
        {
            int len = rng.Next(0, 48);
            var sb = new StringBuilder(len);
            for (int j = 0; j < len; j++)
                sb.Append(Alphabet[rng.Next(Alphabet.Length)]);
            string tail = sb.ToString();
            if (tail.Contains("[200~", StringComparison.Ordinal))
                continue; // paste waits belong to the directed facts
            if (seen.Add(tail))
                yield return [tail];
        }
    }

    public static IEnumerable<object[]> RandomSgr()
    {
        var rng = new Random(77);
        string[] terms = ["M", "m", "MM", "", "X", "mM"];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Count < 2000)
        {
            string s = (rng.Next(4) == 0 ? string.Empty : "[")
                + (rng.Next(4) == 0 ? string.Empty : "<")
                + Coords[rng.Next(Coords.Length)] + ";"
                + Coords[rng.Next(Coords.Length)] + ";"
                + Coords[rng.Next(Coords.Length)]
                + terms[rng.Next(terms.Length)];
            if (seen.Add(s))
                yield return [s];
        }
    }

    [Theory]
    [MemberData(nameof(RandomBursts))]
    public void BurstNeverThrowsBlocksOrSticks(string tail)
    {
        foreach (bool mouse in new[] { false, true })
        {
            var parser = NewParser();
            FeedBurst(parser, tail);
            int steps = 0;
            while (parser.HasQueued)
            {
                Assert.True(steps++ < 2000, $"Parser did not drain (mouse={mouse}): {Show(tail)}");
                InputEvent? ev = parser.TryRead(mouse);
                Assert.NotNull(ev);
                if (ev is MouseInput m)
                {
                    Assert.True(m.X >= 0 && m.Y >= 0);
                    Assert.True(m.Count >= 1);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(RandomSgr))]
    public void SgrParseIsTotal(string burst)
    {
        MouseInput? m = MouseInput.TryParse(burst); // must not throw
        if (m is null)
            return;
        Assert.True(m.X >= 0 && m.Y >= 0);
        Assert.True(m.Count >= 1);
    }

    [Fact]
    public void CompletePaste()
    {
        var parser = NewParser();
        FeedChars(parser, "\x1b[200~hi\x1b[201~");
        InputEvent? ev = parser.TryRead(mouseEnabled: true);
        var paste = Assert.IsType<PasteInput>(ev);
        Assert.Equal("hi", paste.Text);
        Assert.False(parser.HasQueued);
    }

    [Fact]
    [Trait("Category", "Integration")] // truncated paste waits out the 1.5s tail timeout
    public void TruncatedPasteReturnsHead()
    {
        var parser = NewParser();
        FeedChars(parser, "\x1b[200~ab");
        InputEvent? ev = parser.TryRead(mouseEnabled: true);
        var paste = Assert.IsType<PasteInput>(ev);
        Assert.Equal("ab", paste.Text);
    }

    private static InputParser NewParser() => new(
        hasConsoleChar: () => false,
        readConsoleKey: () => throw new InvalidOperationException("Parser blocked on console"));

    private static void FeedBurst(InputParser parser, string tail)
    {
        parser.Feed(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        FeedChars(parser, tail);
    }

    private static void FeedChars(InputParser parser, string chars)
    {
        foreach (char c in chars)
            parser.Feed(new ConsoleKeyInfo(c, (ConsoleKey)c, false, false, false));
    }

    private static string Show(string tail) =>
        tail.Replace("\x1b", "<ESC>", StringComparison.Ordinal);
}
