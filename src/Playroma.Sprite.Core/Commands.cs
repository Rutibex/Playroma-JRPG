namespace Playroma.Sprite.Core;

public interface ISpriteCommand
{
    string Description { get; }
    long EstimatedMemoryBytes { get; }
    void Execute(SpriteDocument document);
    void Undo(SpriteDocument document);
    bool TryMergeWith(ISpriteCommand next) => false;
}

public sealed class SpriteTransaction : IDisposable
{
    private readonly SpriteDocument document;
    private readonly List<ISpriteCommand> executed = [];
    private bool completed;

    public SpriteTransaction(SpriteDocument document, string description)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description;
    }

    public string Description { get; }
    public IReadOnlyList<ISpriteCommand> Commands => executed;

    public void Execute(ISpriteCommand command)
    {
        if (completed) throw new InvalidOperationException("The transaction is complete.");
        ArgumentNullException.ThrowIfNull(command);
        command.Execute(document);
        executed.Add(command);
    }

    public void Commit()
    {
        if (completed) throw new InvalidOperationException("The transaction is complete.");
        SpriteDocumentValidator.ThrowIfInvalid(document);
        completed = true;
    }

    public void Rollback()
    {
        if (completed) return;
        for (int index = executed.Count - 1; index >= 0; index--) executed[index].Undo(document);
        completed = true;
    }

    public void Dispose() { if (!completed) Rollback(); }
}
