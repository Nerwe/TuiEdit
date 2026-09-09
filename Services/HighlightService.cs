namespace TuiEdit;

/// <summary>
/// Syntax highlight access with background prefetch: the UI thread serves rows from
/// a synchronous cache while a shadow highlighter tokenizes the whole buffer off-thread;
/// on completion the caches swap (only if the buffer did not change meanwhile).
/// Superseded prefetches are cancelled.
/// </summary>
internal interface IHighlightService
{
    /// <summary>
    /// Gets the tokens for a row (synchronous, incremental; warms the UI-thread cache).
    /// </summary>
    /// <param name="buf">The buffer to highlight.</param>
    /// <param name="grammar">The grammar (null disables highlighting).</param>
    /// <param name="row">The 0-based row.</param>
    IReadOnlyList<SyntaxToken> GetLine(TextBuffer buf, CompiledGrammar? grammar, int row);

    /// <summary>
    /// Starts a background full-buffer prefetch unless one is already running for this
    /// exact buffer version and grammar. Fire-and-forget; never throws.
    /// </summary>
    /// <param name="buf">The buffer to highlight.</param>
    /// <param name="grammar">The grammar (null disables highlighting).</param>
    void EnsurePrefetched(TextBuffer buf, CompiledGrammar? grammar);
}

/// <summary>Default <see cref="IHighlightService"/>: sync cache plus cancellable shadow prefetch.</summary>
internal sealed class HighlightService : IHighlightService
{
    private readonly object _gate = new();
    private SyntaxHighlighter _sync = new(); // UI thread only
    private SyntaxHighlighter _shadow = new(); // background prefetch only
    private int _prefetchedVersion = -1;
    private CompiledGrammar? _prefetchedGrammar;
    private CancellationTokenSource? _prefetchCts;

    /// <inheritdoc/>
    public IReadOnlyList<SyntaxToken> GetLine(TextBuffer buf, CompiledGrammar? grammar, int row)
    {
        ArgumentNullException.ThrowIfNull(buf);
        lock (_gate)
        {
            return _sync.GetLine(buf, grammar, row);
        }
    }

    /// <inheritdoc/>
    public void EnsurePrefetched(TextBuffer buf, CompiledGrammar? grammar)
    {
        ArgumentNullException.ThrowIfNull(buf);
        lock (_gate)
        {
            if (buf.Version == _prefetchedVersion && ReferenceEquals(grammar, _prefetchedGrammar))
                return;
            _prefetchCts?.Cancel();
            _prefetchCts?.Dispose();
            var cts = new CancellationTokenSource();
            _prefetchCts = cts;
            _prefetchedVersion = buf.Version;
            _prefetchedGrammar = grammar;
            _ = PrefetchAsync(buf, grammar, cts.Token);
        }
    }

    /// <summary>
    /// Tokenizes the whole buffer off-thread, then swaps the caches if the buffer
    /// did not change meanwhile. A cancelled token leaves everything untouched.
    /// </summary>
    /// <param name="buf">The buffer to highlight.</param>
    /// <param name="grammar">The grammar.</param>
    /// <param name="ct">Cancels the prefetch (superseded work).</param>
    internal Task PrefetchAsync(TextBuffer buf, CompiledGrammar? grammar, CancellationToken ct) =>
        Task.Run(() => PrefetchCore(buf, grammar, ct), ct);

    private void PrefetchCore(TextBuffer buf, CompiledGrammar? grammar, CancellationToken ct)
    {
        try
        {
            int version = buf.Version;
            int count = buf.Count;
            for (int r = 0; r < count; r++)
            {
                if (ct.IsCancellationRequested)
                    return;
                lock (_gate)
                {
                    _shadow.GetLine(buf, grammar, r);
                }
            }
            lock (_gate)
            {
                if (!ct.IsCancellationRequested && buf.Version == version)
                    (_sync, _shadow) = (_shadow, _sync);
            }
        }
        catch
        {
            // Background warming must never break editing.
        }
    }
}
