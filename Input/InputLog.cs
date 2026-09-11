namespace TuiEdit;

/// <summary>
/// Opt-in raw input diagnostics: when <c>TUIEDIT_INPUT_LOG</c> holds a file path,
/// every consumed key and every produced input event is appended there.
/// For "keys do nothing" reports: shows whether the console delivers anything
/// and what the parser makes of it. Costs one env lookup per key when unset.
/// </summary>
internal static class InputLog
{
    private static string? LogPath() => Environment.GetEnvironmentVariable("TUIEDIT_INPUT_LOG");

    /// <summary>Whether input logging is enabled.</summary>
    internal static bool Enabled => LogPath() is not null;

    /// <summary>Logs a consumed console key.</summary>
    internal static void Key(ConsoleKeyInfo k)
    {
        string? path = LogPath();
        if (path is null)
            return;
        Write(path, $"key={k.Key} char=U+{(int)k.KeyChar:X4} mods={k.Modifiers}");
    }

    /// <summary>Logs a produced input event (or null when nothing was pending).</summary>
    internal static void Yield(InputEvent? ev)
    {
        string? path = LogPath();
        if (path is null)
            return;
        Write(path, ev switch
        {
            null => "yield=null",
            KeyInput key => $"yield=Key {key.Key.Key} U+{(int)key.Key.KeyChar:X4} {key.Key.Modifiers}",
            PasteInput paste => $"yield=Paste {paste.Text.Length} chars",
            MouseInput => "yield=Mouse",
            _ => $"yield={ev.GetType().Name}",
        });
    }

    /// <summary>Logs a dispatched editor command and whether it had a handler.</summary>
    internal static void Command(string command, bool handled)
    {
        string? path = LogPath();
        if (path is null)
            return;
        Write(path, $"command={command} handled={handled}");
    }

    /// <summary>Logs a main-loop iteration (an input event was obtained).</summary>
    internal static void Loop()
    {
        string? path = LogPath();
        if (path is null)
            return;
        Write(path, "loop");
    }

    /// <summary>Logs that a frame actually renders (throttle may skip).</summary>
    internal static void Paint()
    {
        string? path = LogPath();
        if (path is null)
            return;
        Write(path, "paint");
    }

    /// <summary>Logs a frame slower than the slow-frame budget.</summary>
    internal static void Frame(long ms)
    {
        string? path = LogPath();
        if (path is null)
            return;
        Write(path, $"slow-frame={ms}ms");
    }

    /// <summary>Announces on stderr that logging is on (so a forgotten variable can't cost silently).</summary>
    internal static void Announce(bool starting)
    {
        string? path = LogPath();
        if (path is null)
            return;
        try
        {
            Console.Error.WriteLine(starting
                ? $"tui-edit: input logging is ON — appending to {path}. Unset TUIEDIT_INPUT_LOG to turn it off."
                : "tui-edit: input logging was ON (TUIEDIT_INPUT_LOG).");
            Console.Error.Flush();
        }
        catch
        {
        }
    }

    /// <summary>Logs a session boundary (main loop entry/exit): proves clean quits and tells instances apart.</summary>
    internal static void Session(string phase)
    {
        string? path = LogPath();
        if (path is null)
            return;
        Write(path, $"session={phase}");
    }

    private static void Write(string path, string line)
    {
        try
        {
            File.AppendAllText(path, string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{DateTime.Now:HH:mm:ss.fff} [{Environment.ProcessId}] {line}{Environment.NewLine}"));
        }
        catch
        {
        }
    }
}
