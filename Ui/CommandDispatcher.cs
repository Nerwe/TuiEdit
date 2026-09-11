namespace TuiEdit;

/// <summary>
/// Dispatches editor commands to their handlers.
/// </summary>
internal interface ICommandDispatcher
{
    /// <summary>
    /// Runs the handler registered for <paramref name="command"/>, if any.
    /// Unregistered commands (including <see cref="EditorCommand.None"/>) are ignored.
    /// </summary>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="key">The original key event (some handlers inspect it).</param>
    void Execute(EditorCommand command, ConsoleKeyInfo key);

    /// <summary>
    /// Whether <paramref name="command"/> has a registered handler.
    /// </summary>
    /// <param name="command">The command to check.</param>
    /// <returns>
    /// <see langword="true"/> if the command would be handled; otherwise, <see langword="false"/>.
    /// </returns>
    bool CanDispatch(EditorCommand command);
}

/// <summary>
/// Table-driven <see cref="ICommandDispatcher"/>: a flat command-to-handler map
/// instead of a switch, so dispatch coverage can be asserted exhaustively.
/// </summary>
internal sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly IReadOnlyDictionary<EditorCommand, Action<ConsoleKeyInfo>> _handlers;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDispatcher"/> class.
    /// </summary>
    /// <param name="handlers">The command-to-handler map (ownership stays with the caller).</param>
    public CommandDispatcher(IReadOnlyDictionary<EditorCommand, Action<ConsoleKeyInfo>> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers;
    }

    /// <inheritdoc/>
    public void Execute(EditorCommand command, ConsoleKeyInfo key)
    {
        if (_handlers.TryGetValue(command, out Action<ConsoleKeyInfo>? handler))
        {
            InputLog.Command(command.ToString(), handled: true);
            handler(key);
        }
        else
        {
            InputLog.Command(command.ToString(), handled: false);
        }
    }

    /// <inheritdoc/>
    public bool CanDispatch(EditorCommand command) => _handlers.ContainsKey(command);
}
