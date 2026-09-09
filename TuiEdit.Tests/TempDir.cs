using Xunit;

namespace TuiEdit.Tests;

/// <summary>
/// Isolated temp directory for file-system tests (one per test class via
/// <c>IClassFixture&lt;TempDir&gt;</c>; files inside must still use unique names).
/// Replaces hand-rolled <c>GetTempPath + Guid + try/catch Delete</c> scaffolding.
/// </summary>
public sealed class TempDir : IDisposable
{
    /// <summary>Gets the unique directory path (created on construction).</summary>
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "tui-edit-" + Guid.NewGuid().ToString("N"));

    /// <summary>Initializes a new instance of the <see cref="TempDir"/> class.</summary>
    public TempDir() => Directory.CreateDirectory(Path);

    /// <summary>Gets a unique file path inside the directory (file is not created).</summary>
    /// <param name="name">An optional name hint (a suffix keeps it unique).</param>
    public string UniquePath(string name = "f.txt") =>
        System.IO.Path.Combine(Path, Guid.NewGuid().ToString("N") + "-" + name);

    /// <summary>Creates a unique subdirectory inside the directory and returns its path.</summary>
    /// <param name="name">An optional name hint.</param>
    public string NewDir(string name = "d")
    {
        string path = UniquePath(name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Writes <paramref name="content"/> to a unique file and returns its path.</summary>
    /// <param name="content">The file content.</param>
    /// <param name="name">An optional name hint.</param>
    public string WriteFile(string content, string name = "f.txt")
    {
        string path = UniquePath(name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Deletes the directory recursively (best-effort, never throws).</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
        }
    }
}
