namespace TuiEdit;

/// <summary>
/// Represents a split-view pane: its own tab list, active tab, and tab-strip scroll.
/// Tab view state lives in <see cref="DocTab"/>; editor fields cache the active tab of the active pane.
/// </summary>
internal sealed class Pane
{
    public List<DocTab> Docs { get; } = new();

    public int Active;

    public int TabLeft;

    public Pane(DocTab first)
    {
        Docs.Add(first);
    }
}
