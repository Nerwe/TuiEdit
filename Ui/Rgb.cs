namespace TuiEdit;

/// <summary>Represents an RGB color for truecolor-ANSI output: the only way to get soft pastel tones (the 16 console colors cannot do it).</summary>
internal readonly record struct Rgb(byte R, byte G, byte B)
{
    public string ToAnsiFg() => $"\x1b[38;2;{R};{G};{B}m";

    public string ToAnsiBg() => $"\x1b[48;2;{R};{G};{B}m";

    /// <summary>Blends with another color (t=0 means this, t=1 means other).</summary>
    public Rgb Blend(Rgb other, double t)
    {
        double k = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)(R + (other.R - R) * k),
            (byte)(G + (other.G - G) * k),
            (byte)(B + (other.B - B) * k));
    }

    /// <summary>Finds the nearest of the 16 console colors (fallback without VT).</summary>
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
