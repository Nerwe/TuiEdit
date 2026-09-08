using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Хит-тесты меню: ячейки бара и строки дропдауна.</summary>
public sealed class MenuHitTests
{
    private static List<TopMenu> Menus() => new()
    {
        new TopMenu("File", 'F', new List<MenuItem>
        {
            new("New", 'N', null, EditorCommand.NewTab),
            MenuItem.Separator,
            new("Exit", 'X', "^Q", EditorCommand.Quit),
        }),
        new TopMenu("Edit", 'E', new List<MenuItem> { new("Undo", 'U', "^Z", EditorCommand.Undo) }),
        new TopMenu("Help", 'H', new List<MenuItem> { new("About", 'A', null, EditorCommand.About) }),
    };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(6, 1)]
    [InlineData(11, 1)]
    [InlineData(12, 2)]
    [InlineData(17, 2)]
    public void BarHitCells(int x, int want)
    {
        Assert.Equal(want, MenuHit.BarHit(Menus(), x, 80));
    }

    [Theory]
    [InlineData(18)]
    [InlineData(79)]
    public void BarHitMiss(int x)
    {
        Assert.Null(MenuHit.BarHit(Menus(), x, 80));
    }

    [Fact]
    public void BarHitRespectsWidth()
    {
        // Help начинается на 12, ячейка [12,18) при ширине 12 не влезла (break).
        Assert.Null(MenuHit.BarHit(Menus(), 13, 12));
        Assert.Equal(0, MenuHit.BarHit(Menus(), 0, 12));
    }

    [Fact]
    public void DropdownHitRows()
    {
        TopMenu file = Menus()[0];
        Assert.Equal(0, MenuHit.DropdownHit(file, 0, 3, 2, 80, 24)); // New
        Assert.Null(MenuHit.DropdownHit(file, 0, 3, 3, 80, 24)); // разделитель — игнор
        Assert.Equal(2, MenuHit.DropdownHit(file, 0, 3, 4, 80, 24)); // Exit
        Assert.Null(MenuHit.DropdownHit(file, 0, 3, 1, 80, 24)); // рамка сверху
        Assert.Null(MenuHit.DropdownHit(file, 0, 3, 5, 80, 24)); // рамка снизу
        Assert.Null(MenuHit.DropdownHit(file, 0, 79, 2, 80, 24)); // мимо по x
    }

    [Fact]
    public void DropdownHitTinyScreen()
    {
        TopMenu file = Menus()[0];
        Assert.Null(MenuHit.DropdownHit(file, 0, 3, 2, 9, 24)); // boxW 9 < 10 — не влез
        Assert.Null(MenuHit.DropdownHit(file, 0, 3, 2, 80, 4)); // maxRows 2 < 3 — не влез
    }
}
