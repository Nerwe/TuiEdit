using System.Reflection;
using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>Version comes from the assembly, not from a hardcode.</summary>
public sealed class VersionTests
{
    [Fact]
    public void AppVersionMatchesAssembly()
    {
        string? asm = Assembly.GetAssembly(typeof(AppSettings))!.GetName().Version?.ToString(3);
        Assert.False(string.IsNullOrEmpty(asm));
        Assert.Equal(asm, TuiEditor.AppVersion);
    }

    [Fact]
    public void AboutShowsVersion()
    {
        var m = ModalState.About(Loc.Load("en"), TuiEditor.AppVersion);
        Assert.Contains(TuiEditor.AppVersion, m.Lines[0]);
    }
}
