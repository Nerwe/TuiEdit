namespace TuiEdit;

/// <summary>
/// Git integration for the status bar and the gutter: branch/dirt segment plus diff marks.
/// All members are non-blocking; expensive queries run in the background and
/// readers see the last known values.
/// </summary>
internal interface IGitService
{
    /// <summary>
    /// Status-bar segment for a file (e.g. "⎇ main*"), or <see langword="null"/>
    /// outside a repo, for unknown files, or on any error. Never blocks.
    /// </summary>
    /// <param name="filePath">The file to report on.</param>
    string? StatusSegment(string? filePath);

    /// <summary>
    /// Gutter marks for a file: added and modified new-side rows (0-based).
    /// Empty when outside a repo, unchanged, or on any error. Never blocks.
    /// </summary>
    /// <param name="filePath">The file to report on.</param>
    (IReadOnlySet<int> added, IReadOnlySet<int> modified) DiffMarks(string? filePath);
}

/// <summary>
/// Default <see cref="IGitService"/>: one background <c>git</c> spawn per refresh
/// window, results cached per directory (status) and per file (diff).
/// </summary>
internal sealed class GitService : IGitService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);
    private static readonly HashSet<int> Empty = [];

    /// <summary>Process-wide instance backing the legacy static facades.</summary>
    public static GitService Shared { get; } = new();

    private readonly object _statusGate = new();
    private string? _statusDir;
    private StatusInfo? _statusCached; // null is cached too (not a repo), to avoid useless forks
    private DateTime _statusAt = DateTime.MinValue;
    private bool _statusRefreshing;

    private readonly object _diffGate = new();
    private string? _diffFile;
    private DateTime _diffMtime;
    private HashSet<int> _diffAdded = [];
    private HashSet<int> _diffModified = [];
    private DateTime _diffAt = DateTime.MinValue;
    private bool _diffRefreshing;

    /// <inheritdoc/>
    public string? StatusSegment(string? filePath)
    {
        string? dir = DirOf(filePath);
        if (dir is null)
            return null;
        lock (_statusGate)
        {
            if (dir != _statusDir)
            {
                _statusDir = dir;
                _statusCached = null;
                _statusAt = DateTime.MinValue; // new location — refresh immediately
            }
            if (DateTime.UtcNow - _statusAt >= Ttl && !_statusRefreshing)
            {
                _statusRefreshing = true;
                string snapshot = dir;
                Task.Run(() =>
                {
                    try
                    {
                        StatusInfo? fresh = QueryStatus(snapshot);
                        lock (_statusGate)
                        {
                            if (snapshot == _statusDir)
                            {
                                _statusCached = fresh;
                                _statusAt = DateTime.UtcNow;
                            }
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        lock (_statusGate)
                        {
                            _statusRefreshing = false;
                        }
                    }
                });
            }
            return _statusCached is null ? null : Segment(_statusCached.Branch, _statusCached.Dirty);
        }
    }

    /// <inheritdoc/>
    public (IReadOnlySet<int> added, IReadOnlySet<int> modified) DiffMarks(string? filePath)
    {
        string? full;
        DateTime mtime;
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return (Empty, Empty);
            full = Path.GetFullPath(filePath);
            mtime = File.GetLastWriteTimeUtc(full);
        }
        catch
        {
            return (Empty, Empty);
        }
        lock (_diffGate)
        {
            if (full != _diffFile || mtime != _diffMtime)
            {
                _diffFile = full;
                _diffMtime = mtime;
                _diffAdded = [];
                _diffModified = [];
                _diffAt = DateTime.MinValue; // new location — refresh immediately
            }
            if (DateTime.UtcNow - _diffAt >= Ttl && !_diffRefreshing)
            {
                _diffRefreshing = true;
                string snapshot = full;
                Task.Run(() =>
                {
                    try
                    {
                        var (added, modified) = QueryDiff(snapshot);
                        lock (_diffGate)
                        {
                            if (snapshot == _diffFile)
                            {
                                _diffAdded = added;
                                _diffModified = modified;
                                _diffAt = DateTime.UtcNow;
                            }
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        lock (_diffGate)
                        {
                            _diffRefreshing = false;
                        }
                    }
                });
            }
            return (_diffAdded, _diffModified);
        }
    }

    /// <summary>Synchronous status query (for tests; also populates the cache).</summary>
    internal string? StatusSegmentSync(string? filePath)
    {
        string? dir = DirOf(filePath);
        if (dir is null)
            return null;
        StatusInfo? fresh = QueryStatus(dir);
        lock (_statusGate)
        {
            _statusDir = dir;
            _statusCached = fresh;
            _statusAt = DateTime.UtcNow;
        }
        return fresh is null ? null : Segment(fresh.Branch, fresh.Dirty);
    }

    /// <summary>Synchronous diff query (for tests).</summary>
    internal static (IReadOnlySet<int> added, IReadOnlySet<int> modified) DiffMarksSync(string? filePath)
    {
        string? full;
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return (Empty, Empty);
            full = Path.GetFullPath(filePath);
        }
        catch
        {
            return (Empty, Empty);
        }
        return QueryDiff(full);
    }

    private static string? DirOf(string? filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;
            return Path.GetDirectoryName(Path.GetFullPath(filePath));
        }
        catch
        {
            return null;
        }
    }

    private static string Segment(string? branch, bool dirty) =>
        $"⎇ {(string.IsNullOrEmpty(branch) ? "?" : branch)}{(dirty ? "*" : string.Empty)}";

    private sealed record StatusInfo(string Branch, bool Dirty);

    /// <summary>Single spawn: branch from the ## header, dirt from non-empty lines below.</summary>
    private static StatusInfo? QueryStatus(string dir)
    {
        if (!GitProcess.HasRepoRoot(dir))
            return null; // outside a repo a console-less git hangs — don't even spawn
        string? output = GitProcess.Run(dir, "status", "-sb", "--porcelain=v1", "--untracked-files=no");
        if (output is null)
            return null;
        string[] lines = output.Split('\n');
        string branch = "?";
        string head = lines.Length > 0 ? lines[0].Trim() : string.Empty;
        const string unborn = "No commits yet on ";
        if (head.StartsWith("## ", StringComparison.Ordinal))
        {
            string rest = head[3..];
            if (rest.StartsWith(unborn, StringComparison.Ordinal))
            {
                branch = rest[unborn.Length..];
            }
            else
            {
                int end = rest.IndexOfAny(['.', ' ']);
                branch = (end < 0 ? rest : rest[..end]).Trim();
            }
        }
        bool dirty = false;
        for (int i = 1; i < lines.Length; i++)
            if (lines[i].Length > 0)
            {
                dirty = true;
                break;
            }
        return new StatusInfo(branch, dirty);
    }

    private static (HashSet<int> added, HashSet<int> modified) QueryDiff(string full)
    {
        (HashSet<int> added, HashSet<int> modified) empty = ([], []);
        string? dir;
        try
        {
            dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir))
                return empty;
        }
        catch
        {
            return empty;
        }
        if (!GitProcess.HasRepoRoot(dir))
            return empty; // outside a repo a console-less git hangs — don't even spawn
        return GitDiff.Parse(GitProcess.Run(dir,
            "-c", "core.quotepath=false",
            "diff", "--no-color", "--no-ext-diff", "--unified=0", "--", full));
    }
}
