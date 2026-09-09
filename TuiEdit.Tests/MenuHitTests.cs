using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Menu hit tests: bar cells and dropdown rows.</summary>
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
        // Help starts at 12, cell [12,18) did not fit at width 12 (break).
        Assert.Null(MenuHit.BarHit(Menus(), 13, 12));
        Assert.Equal(0, MenuHit.BarHit(Menus(), 0, 12));
    }

    [Fact]
    public void DropdownHitRows()
    {
        TopMenu file = Menus()[0];
        Assert.Equal(0, MenuHit.DropdownHit(file, 0, 3, 2, 80, 24)); // New
        Assert.Null(MenuHit.DropdownHit(file, 0, 3, 3, 80, 24)); // separator — ignore
        Assert.Equal(2, MenuHit.DropdownHit(file, 0, 3, 4, 80, 24)); // Exit
        Assert.Null(MenuHit.DropdownHit(file, 0, 3, 1, 80, 24)); // top frame
        Assert.Null(MenuHit.DropdownHit(file, 0, 3, 5, 80, 24)); // bottom frame
        Assert.Null(MenuHit.DropdownHit(file, 0, 79, 2, 80, 24)); // miss on x
    }

    [Fact]
    public void DropdownHitTinyScreen()
    {
        TopMenu file = Menus()[0];
        Assert.Null(MenuHit.DropdownHit(file, 0, 3, 2, 9, 24)); // boxW 9 < 10 — did not fit
        Assert.Null(MenuHit.DropdownHit(file, 0, 3, 2, 80, 4)); // maxRows 2 < 3 — did not fit
    }
}
