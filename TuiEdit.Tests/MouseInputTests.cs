using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>SGR-мышь: парсер пачек и маппинг клика в документ.</summary>
public sealed class MouseInputTests
{
    [Theory]
    [InlineData("[<0;11;6M", 10, 5, 0)] // 1-based -> 0-based, LeftPress
    [InlineData("[<0;1;1M", 0, 0, 0)]
    [InlineData("[<32;5;5M", 4, 4, 3)] // движение (?1002) — hover
    [InlineData("[<64;5;5M", 4, 4, 1)] // WheelUp
    [InlineData("[<65;5;5M", 4, 4, 2)] // WheelDown
    [InlineData("[<72;5;5M", 4, 4, 1)] // колесо + shift-модификатор
    public void ParseValid(string burst, int x, int y, int action)
    {
        MouseInput? m = MouseInput.TryParse(burst);
        Assert.NotNull(m);
        Assert.Equal(x, m.X);
        Assert.Equal(y, m.Y);
        Assert.Equal((MouseAction)action, m.Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[200~")]
    [InlineData("[<0;1;1m")] // release в v1 игнорим
    [InlineData("[<1;5;5M")] // средняя кнопка
    [InlineData("[<2;5;5M")] // правая кнопка
    [InlineData("[<0;5M")] // мало частей
    [InlineData("[<0;5;5X")] // не M/m
    [InlineData("[<a;5;5M")] // не число
    [InlineData("[A")] // стрелка — не мышь
    public void ParseInvalid(string burst)
    {
        Assert.Null(MouseInput.TryParse(burst));
    }

    private static Terminal.MouseEventRecord Rec(uint buttons, uint flags, short x, short y) =>
        new() { ButtonState = buttons, EventFlags = flags, MousePosition = new Terminal.Coord { X = x, Y = y } };

    [Theory]
    [InlineData(0x0001u, 0u, 10, 5, 0)] // левый клик
    [InlineData(0x0001u, 0x0002u, 10, 5, 0)] // дабл-клик (второе нажатие) — тоже клик
    [InlineData(0x0000u, 0u, 10, 5, -1)] // отпускание — игнор
    [InlineData(0x0002u, 0u, 10, 5, -1)] // правая — игнор
    [InlineData(0x0001u, 0x0001u, 10, 5, 3)] // движение — hover (по одному за Read)
    [InlineData(0x00780000u, 0x0004u, 3, 7, 1)] // колесо вверх (delta +120)
    [InlineData(0xFF880000u, 0x0004u, 3, 7, 2)] // колесо вниз (delta -120)
    [InlineData(0x00000000u, 0x0004u, 3, 7, -1)] // колесо с нулевой дельтой — игнор
    public void TranslateConsoleRecord(uint buttons, uint flags, short x, short y, int action)
    {
        Terminal.MouseEventRecord r = Rec(buttons, flags, x, y);
        if (action < 0)
        {
            Assert.Null(Terminal.TranslateMouse(r));
            return;
        }
        MouseInput? m = Terminal.TranslateMouse(r);
        Assert.NotNull(m);
        Assert.Equal((MouseAction)action, m.Action);
        Assert.Equal(x, m.X);
        Assert.Equal(y, m.Y);
    }

    [Fact]
    public void TranslateClampsNegativeCoords()
    {
        MouseInput? m = Terminal.TranslateMouse(Rec(0x0001u, 0u, -5, -2));
        Assert.NotNull(m);
        Assert.Equal(0, m.X);
        Assert.Equal(0, m.Y);
    }

    [Fact]
    public void ColumnAtPlainAndTabs()
    {
        Assert.Equal(0, TuiEditor.ColumnAt("hello", 0));
        Assert.Equal(3, TuiEditor.ColumnAt("hello", 3));
        Assert.Equal(5, TuiEditor.ColumnAt("hello", 99)); // дальше конца — не убегаем (кламп выше)
        Assert.Equal(1, TuiEditor.ColumnAt("\thello", 4)); // таб шириной 4
        Assert.Equal(0, TuiEditor.ColumnAt("\thello", 3)); // середина таба — левый край
    }

    [Fact]
    public void LocateClickPlain()
    {
        var lines = new List<string> { "aaa", "bbb", "ccc" };
        var folds = new SortedSet<int>();
        Assert.Equal((1, 2), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 0, 1, 2));
        Assert.Equal((0, 0), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 0, 0, 0));
        // Ниже текста — конец документа.
        Assert.Equal((2, 3), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 0, 9, 0));
    }

    [Fact]
    public void LocateClickWrapAndScroll()
    {
        var lines = new List<string> { "aaa", "bbb" };
        var folds = new SortedSet<int>();
        // Скролл top=1: vis 0 — вторая строка.
        Assert.Equal((1, 1), TuiEditor.LocateClick(lines, folds, 1, 0, false, 80, 0, 0, 1));
        // Горизонтальный скролл left=2: колонка считается от него.
        Assert.Equal((0, 3), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 2, 0, 1));
    }

    [Fact]
    public void LocateClickSkipsFolds()
    {
        var lines = new List<string> { "if x:", "    a", "    b", "done" };
        var folds = new SortedSet<int> { 0 }; // строки 1-2 скрыты (по отступу)
        Assert.Equal((0, 1), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 0, 0, 1));
        Assert.Equal((3, 0), TuiEditor.LocateClick(lines, folds, 0, 0, false, 80, 0, 1, 0));
    }
}
