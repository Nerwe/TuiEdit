namespace TuiEdit;

/// <summary>
/// Геометрия экрана редактора: единый источник для Render и мыши.
/// Правило: ни Render, ни HandleMouse не считают раскладку руками.
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
    /// <summary>Посчитать раскладку (зеркало бывшего ручного кода в Render).</summary>
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

    /// <summary>Панель под координатой X или -1 (сайдбар/мимо).</summary>
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
