namespace Playroma.JRPG.Editing;

public sealed class CompositeEditorCommand : IEditorCommand
{
    private readonly IReadOnlyList<IEditorCommand> _commands;

    public CompositeEditorCommand(string description, IEnumerable<IEditorCommand> commands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands.ToArray();
        if (_commands.Count == 0)
        {
            throw new ArgumentException("A composite command requires at least one child command.", nameof(commands));
        }

        Description = description;
        Targets = _commands.SelectMany(command => command.Targets)
            .Distinct()
            .ToArray();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Description { get; }
    public IReadOnlyList<IEditorCommand> Commands => _commands;
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default)
    {
        List<IEditorCommand> executed = [];
        try
        {
            foreach (IEditorCommand command in _commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await command.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                executed.Add(command);
            }
        }
        catch (Exception exception)
        {
            List<Exception> rollbackFailures = [];
            for (int index = executed.Count - 1; index >= 0; index--)
            {
                try
                {
                    await executed[index].UndoAsync(context, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    rollbackFailures.Add(rollbackException);
                }
            }

            throw new EditorTransactionException(
                $"Composite command '{Description}' failed and was rolled back.",
                exception,
                rollbackFailures);
        }

    }

    public async ValueTask UndoAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default)
    {
        for (int index = _commands.Count - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _commands[index].UndoAsync(context, cancellationToken).ConfigureAwait(false);
        }

    }
}
