namespace TuiEdit;

/// <summary>Цвет RGB для truecolor-ANSI вывода: только так доступны мягкие пастельные палитры (16 цветов консоли для этого не годятся).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string ToAnsiFg() => $"\x1b[38;2;{R};{G};{B}m";

    public string ToAnsiBg() => $"\x1b[48;2;{R};{G};{B}m";

    /// <summary>Смешать с другим цветом (t=0 — этот, t=1 — other).</summary>
    public Rgb Blend(Rgb other, double t)
    {
        double k = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)(R + (other.R - R) * k),
            (byte)(G + (other.G - G) * k),
            (byte)(B + (other.B - B) * k));
    }

    /// <summary>Ближайший из 16 цветов консоли (fallback без VT).</summary>
    public ConsoleColor ToConsoleColor()
    {
        (ConsoleColor color, int r, int g, int b)[] table =
        [
            (ConsoleColor.Black, 0, 0, 0),
            (ConsoleColor.DarkBlue, 0, 0, 128),
            (ConsoleColor.DarkGreen, 0, 128, 0),
            (ConsoleColor.DarkCyan, 0, 128, 128),
            (ConsoleColor.DarkRed, 128, 0, 0),
            (ConsoleColor.DarkMagenta, 128, 0, 128),
            (ConsoleColor.DarkYellow, 128, 128, 0),
            (ConsoleColor.Gray, 192, 192, 192),
            (ConsoleColor.DarkGray, 128, 128, 128),
            (ConsoleColor.Blue, 0, 0, 255),
            (ConsoleColor.Green, 0, 255, 0),
            (ConsoleColor.Cyan, 0, 255, 255),
            (ConsoleColor.Red, 255, 0, 0),
            (ConsoleColor.Magenta, 255, 0, 255),
            (ConsoleColor.Yellow, 255, 255, 0),
            (ConsoleColor.White, 255, 255, 255),
        ];
        ConsoleColor best = ConsoleColor.Black;
        int bestDist = int.MaxValue;
        foreach (var (color, r, g, b) in table)
        {
            int dr = R - r;
            int dg = G - g;
            int db = B - b;
            int dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = color;
            }
        }
        return best;
    }
}
