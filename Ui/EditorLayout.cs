namespace TuiEdit;

/// <summary>
/// Describes the editor screen geometry: the single source for Render and mouse handling.
/// Rule: neither Render nor HandleMouse computes layout by hand.
/// </summary>
internal sealed record EditorLayout(
    int W,
    int H,
    int SideW,
    int[] PaneXs,
    int[] PaneWs,
    int TabH,
    int Y0,
    int TextHeight)
{
    /// <summary>Computes the layout (mirrors the former hand-rolled code in Render).</summary>
    public static EditorLayout Compute(int w, int h, int sideW, int paneCount, bool multiTab)
    {
        int[] paneWs = TuiEditor.PaneWidths(w - sideW, paneCount);
        int[] paneXs = new int[paneWs.Length];
        for (int i = 0, x = sideW; i < paneXs.Length; i++)
        {
            paneXs[i] = x;
            x += paneWs[i];
        }
        int tabH = multiTab ? 1 : 0;
        return new EditorLayout(w, h, sideW, paneXs, paneWs, tabH, 1 + tabH, h - 2 - tabH);
    }

    /// <summary>Gets the pane under X, or -1 (sidebar/miss).</summary>
    public int PaneAt(int x)
    {
        for (int i = 0; i < PaneXs.Length; i++)
        {
            if (x >= PaneXs[i] && x < PaneXs[i] + PaneWs[i])
                return i;
        }
        return -1;
    }
}
