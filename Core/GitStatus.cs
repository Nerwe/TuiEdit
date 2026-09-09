namespace TuiEdit;

/// <summary>
/// Branch and dirt for the status bar, e.g. "⎇ main*" (dirt covers tracked files only;
/// untracked files are skipped for speed). Null outside a repo and on any error.
/// Background refresh (a single <c>status -sb</c> spawn per update): synchronous forks
/// in Render used to stall ~100ms every 5s even on a small repo, seconds on a large one.
/// Render only reads the last known value.
/// </summary>
/// <remarks>
/// Legacy static facade over <see cref="GitService.Shared"/>; new code should depend
/// on <see cref="IGitService"/> instead.
/// </remarks>
internal static class GitStatus
{
    /// <summary>Bar segment for a file ("⎇ main*") or null. Never blocks.</summary>
    public static string? ForFile(string? filePath) => GitService.Shared.StatusSegment(filePath);

    /// <summary>Same, synchronously (for tests; also populates the cache).</summary>
    internal static string? ForFileSync(string? filePath) => GitService.Shared.StatusSegmentSync(filePath);
}
