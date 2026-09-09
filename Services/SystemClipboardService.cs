namespace TuiEdit;

/// <summary>
/// System clipboard export seam (testable): pushes the internal clipboard out
/// so the terminal can paste it back.
/// </summary>
internal interface ISystemClipboard
{
    /// <summary>
    /// Best-effort export of <paramref name="lines"/> (quietly ignored when unsupported).
    /// </summary>
    /// <param name="lines">The clipboard content lines.</param>
    void Export(IEnumerable<string> lines);
}

/// <summary>
/// Default <see cref="ISystemClipboard"/> backed by <see cref="SystemClipboard"/> OSC 52.
/// </summary>
internal sealed class SystemClipboardService : ISystemClipboard
{
    /// <summary>Process-wide instance for the default wiring.</summary>
    public static SystemClipboardService Shared { get; } = new();

    /// <inheritdoc/>
    public void Export(IEnumerable<string> lines) => SystemClipboard.TryExport(lines);
}
