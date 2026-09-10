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

    /// <summary>
    /// Repo-wide changed full paths (tracked modifications plus untracked files),
    /// for tree/file views. Empty outside a repo or on any error. Never blocks;
    /// readers see the last known sets.
    /// </summary>
    /// <param name="dirPath">Any directory inside the repo.</param>
    (IReadOnlySet<string> added, IReadOnlySet<string> modified) ChangedFiles(string? dirPath);
}

/// <summary>
/// Default <see cref="IGitService"/>: one background <c>git</c> spawn per refresh
/// window, results cached per directory (status) and per file (diff).
/// </summary>
internal sealed class GitService : IGitService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);
    private static readonly HashSet<int> Empty = [];
    private static readonly HashSet<string> EmptyPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cap on parsed porcelain lines (huge untracked trees degrade gracefully).</summary>
    private const int MaxStatusLines = 5000;

    /// <summary>Process-wide instance backing the legacy static facades.</summary>
    public static GitService Shared { get; } = new();

    private readonly object _statusGate = new();
    private string? _statusDir;
    private StatusInfo? _statusCached; // null is cached too (not a repo), to avoid useless forks
    private DateTime _statusAt = DateTime.MinValue;
    private CancellationTokenSource? _statusCts; // latest refresh; superseded refreshes are cancelled

    private readonly object _diffGate = new();
    private string? _diffFile;
    private DateTime _diffMtime;
    private HashSet<int> _diffAdded = [];
    private HashSet<int> _diffModified = [];
    private DateTime _diffAt = DateTime.MinValue;
    private CancellationTokenSource? _diffCts; // latest refresh; superseded refreshes are cancelled

    private readonly object _changesGate = new();
    private string? _changesRoot;
    private HashSet<string> _changesAdded = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _changesModified = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _changesAt = DateTime.MinValue;
    private CancellationTokenSource? _changesCts; // latest refresh; superseded refreshes are cancelled

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
            if (DateTime.UtcNow - _statusAt >= Ttl)
                TriggerStatusRefresh(dir);
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
            if (DateTime.UtcNow - _diffAt >= Ttl)
                TriggerDiffRefresh(full);
            return (_diffAdded, _diffModified);
        }
    }

    /// <inheritdoc/>
    public (IReadOnlySet<string> added, IReadOnlySet<string> modified) ChangedFiles(string? dirPath)
    {
        string? root = dirPath is null ? null : GitProcess.RepoRoot(dirPath);
        if (root is null)
            return (EmptyPaths, EmptyPaths);
        lock (_changesGate)
        {
            if (root != _changesRoot)
            {
                _changesRoot = root;
                _changesAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _changesModified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _changesAt = DateTime.MinValue; // new location — refresh immediately
            }
            if (DateTime.UtcNow - _changesAt >= Ttl)
                TriggerChangesRefresh(root);
            return (_changesAdded, _changesModified);
        }
    }

    /// <summary>Synchronous repo-changes query (for tests; also populates the cache).</summary>
    internal (IReadOnlySet<string> added, IReadOnlySet<string> modified) ChangedFilesSync(string? dirPath)
    {
        string? root = dirPath is null ? null : GitProcess.RepoRoot(dirPath);
        if (root is null)
            return (EmptyPaths, EmptyPaths);
        lock (_changesGate)
        {
            _changesRoot = root;
            _changesAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _changesModified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _changesAt = DateTime.MinValue;
        }
        try
        {
            return RefreshChangesAsync(root, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            return (EmptyPaths, EmptyPaths);
        }
    }

    /// <summary>Starts a cancellable background changes refresh, superseding the previous one.</summary>
    private void TriggerChangesRefresh(string root)
    {
        _changesCts?.Cancel();
        _changesCts?.Dispose();
        var cts = new CancellationTokenSource();
        _changesCts = cts;
        _ = RefreshChangesAsync(root, cts.Token);
    }

    /// <summary>
    /// Refreshes the repo-changes cache without blocking: queries git asynchronously
    /// and stores the result only if still current (not superseded or cancelled).
    /// Never throws.
    /// </summary>
    /// <param name="root">The repository root to query.</param>
    /// <param name="ct">Cancels the query (superseded refresh).</param>
    internal async Task<(IReadOnlySet<string> added, IReadOnlySet<string> modified)> RefreshChangesAsync(
        string root, CancellationToken ct)
    {
        (HashSet<string> added, HashSet<string> modified) fresh;
        try
        {
            fresh = await QueryChangesAsync(root, ct).ConfigureAwait(false);
        }
        catch
        {
            return (EmptyPaths, EmptyPaths);
        }
        lock (_changesGate)
        {
            if (ct.IsCancellationRequested || root != _changesRoot)
                return (EmptyPaths, EmptyPaths); // superseded — drop silently
            _changesAdded = fresh.added;
            _changesModified = fresh.modified;
            _changesAt = DateTime.UtcNow;
        }
        return fresh;
    }

    /// <summary>Synchronous status query (for tests; also populates the cache).</summary>
    internal string? StatusSegmentSync(string? filePath)
    {
        string? dir = DirOf(filePath);
        if (dir is null)
            return null;
        lock (_statusGate)
        {
            _statusDir = dir;
            _statusCached = null;
            _statusAt = DateTime.MinValue;
        }
        try
        {
            return RefreshStatusAsync(dir, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Synchronous diff query (for tests).</summary>
    internal (IReadOnlySet<int> added, IReadOnlySet<int> modified) DiffMarksSync(string? filePath)
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
        lock (_diffGate)
        {
            _diffFile = full;
            try
            {
                _diffMtime = File.GetLastWriteTimeUtc(full);
            }
            catch
            {
            }
            _diffAdded = [];
            _diffModified = [];
            _diffAt = DateTime.MinValue;
        }
        try
        {
            return RefreshDiffAsync(full, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            return (Empty, Empty);
        }
    }

    /// <summary>Starts a cancellable background status refresh, superseding the previous one.</summary>
    private void TriggerStatusRefresh(string dir)
    {
        _statusCts?.Cancel();
        _statusCts?.Dispose();
        var cts = new CancellationTokenSource();
        _statusCts = cts;
        _ = RefreshStatusAsync(dir, cts.Token);
    }

    /// <summary>Starts a cancellable background diff refresh, superseding the previous one.</summary>
    private void TriggerDiffRefresh(string full)
    {
        _diffCts?.Cancel();
        _diffCts?.Dispose();
        var cts = new CancellationTokenSource();
        _diffCts = cts;
        _ = RefreshDiffAsync(full, cts.Token);
    }

    /// <summary>
    /// Refreshes the status cache without blocking: queries git asynchronously and stores
    /// the result only if still current (not superseded or cancelled). Never throws.
    /// </summary>
    /// <param name="dir">The directory to query.</param>
    /// <param name="ct">Cancels the query (superseded refresh).</param>
    internal async Task<string?> RefreshStatusAsync(string dir, CancellationToken ct)
    {
        StatusInfo? fresh;
        try
        {
            fresh = await QueryStatusAsync(dir, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
        lock (_statusGate)
        {
            if (ct.IsCancellationRequested || dir != _statusDir)
                return null; // superseded — drop silently
            _statusCached = fresh;
            _statusAt = DateTime.UtcNow;
        }
        return fresh is null ? null : Segment(fresh.Branch, fresh.Dirty);
    }

    /// <summary>
    /// Refreshes the diff cache without blocking: queries git asynchronously and stores
    /// the result only if still current (not superseded or cancelled). Never throws.
    /// </summary>
    /// <param name="full">The full file path to query.</param>
    /// <param name="ct">Cancels the query (superseded refresh).</param>
    internal async Task<(IReadOnlySet<int> added, IReadOnlySet<int> modified)> RefreshDiffAsync(
        string full, CancellationToken ct)
    {
        (HashSet<int> added, HashSet<int> modified) fresh;
        try
        {
            fresh = await QueryDiffAsync(full, ct).ConfigureAwait(false);
        }
        catch
        {
            return (Empty, Empty);
        }
        lock (_diffGate)
        {
            if (ct.IsCancellationRequested || full != _diffFile)
                return (Empty, Empty); // superseded — drop silently
            _diffAdded = fresh.added;
            _diffModified = fresh.modified;
            _diffAt = DateTime.UtcNow;
        }
        return fresh;
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

    /// <summary>
    /// Single spawn: porcelain paths split into added (untracked/index-added)
    /// and modified (everything else listed). Renames resolve to the new path.
    /// </summary>
    private static async Task<(HashSet<string> added, HashSet<string> modified)> QueryChangesAsync(
        string root, CancellationToken ct)
    {
        var empty = (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        using var timeout = new CancellationTokenSource(GitProcess.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        string? output = await GitProcess.RunAsync(root, linked.Token,
            "-c", "core.quotepath=false",
            "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        if (output is null)
            return empty;
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int lines = 0;
        foreach (string raw in output.Split('\n'))
        {
            if (lines++ >= MaxStatusLines)
                break;
            string line = raw.TrimEnd('\r');
            if (line.Length < 4 || line[2] != ' ')
                continue; // short or malformed status line
            string xy = line[..2];
            string rel = line[3..];
            int arrow = rel.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                rel = rel[(arrow + 4)..]; // rename: the new path counts
            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(root, rel));
            }
            catch
            {
                continue;
            }
            if (xy == "??" || xy[0] == 'A')
                added.Add(full);
            else
                modified.Add(full);
        }
        return (added, modified);
    }

    private sealed record StatusInfo(string Branch, bool Dirty);

    /// <summary>Single spawn: branch from the ## header, dirt from non-empty lines below.</summary>
    private static async Task<StatusInfo?> QueryStatusAsync(string dir, CancellationToken ct)
    {
        if (!GitProcess.HasRepoRoot(dir))
            return null; // outside a repo a console-less git hangs — don't even spawn
        using var timeout = new CancellationTokenSource(GitProcess.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        string? output = await GitProcess.RunAsync(
            dir, linked.Token, "status", "-sb", "--porcelain=v1", "--untracked-files=no").ConfigureAwait(false);
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

    private static async Task<(HashSet<int> added, HashSet<int> modified)> QueryDiffAsync(
        string full, CancellationToken ct)
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
        using var timeout = new CancellationTokenSource(GitProcess.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        return GitDiff.Parse(await GitProcess.RunAsync(dir, linked.Token,
            "-c", "core.quotepath=false",
            "diff", "--no-color", "--no-ext-diff", "--unified=0", "--", full).ConfigureAwait(false));
    }
}
