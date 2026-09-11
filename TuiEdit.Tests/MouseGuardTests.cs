using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

// A throwing mouse path must park the mouse for the session, log the crash,
// and keep the editor alive — never propagate.
[Trait("Category", "Integration")]
[Collection("MouseInput")]
public sealed class MouseGuardTests
{
    [Fact]
    public void MouseFailureParksMouseForSession()
    {
        var settings = new AppSettings { Mouse = MouseLevel.Basic };
        var ed = new TuiEditor(new TextBuffer(null), settings,
            new SettingsStore(Path.Combine("x", "settings.json")));
        try
        {
            InputReader.MouseLevel = MouseLevel.Basic;
            // Isolate from other crash-log tests (prune caps the shared dir at 20).
            try
            {
                if (Directory.Exists(CrashLog.Dir))
                    foreach (string f in Directory.GetFiles(CrashLog.Dir, "crash-*.log"))
                        File.Delete(f);
            }
            catch
            {
            }
            int before = 0;

            // Null event: HandleMouseAt throws, the guard must convert it
            // into a parked mouse plus a crash report.
            var ex = Record.Exception(() => ed.HandleMouseGuarded(null!, 80, 24));

            Assert.Null(ex); // swallowed by the fallback
            Assert.Equal(MouseLevel.Off, settings.Mouse);
            Assert.Equal(MouseLevel.Off, InputReader.MouseLevel);
            int after = Directory.GetFiles(CrashLog.Dir, "crash-*.log").Length;
            Assert.True(after > before, "expected a mouse crash report");
        }
        finally
        {
            InputReader.MouseLevel = MouseLevel.Off;
        }
    }
}
