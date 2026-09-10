using System.Runtime.CompilerServices;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// Golden frames: render a dialog onto a Screen and compare the text snapshot
// against a checked-in file. Regenerate intentionally with UPDATE_GOLDENS=1
// (then review the diff — a changed golden is a changed UI).
internal static class Golden
{
    /// <summary>Asserts the screen snapshot matches the golden file (or rewrites it).</summary>
    /// <param name="name">The golden file name (e.g. "help.en.txt").</param>
    /// <param name="screen">The rendered screen.</param>
    /// <param name="testPath">The calling test file (resolves the Goldens dir).</param>
    public static void AssertMatch(
        string name, Screen screen, [CallerFilePath] string testPath = "")
    {
        ArgumentNullException.ThrowIfNull(screen);
        string dir = Path.Combine(Path.GetDirectoryName(testPath)!, "Goldens");
        string path = Path.Combine(dir, name);
        string actual = string.Join("\n", screen.Snapshot());
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, actual + "\n");
            return;
        }
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (expected.EndsWith('\n'))
            expected = expected[..^1];
        Assert.Equal(expected, actual);
    }
}
