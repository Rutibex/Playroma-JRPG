namespace Playroma.JRPG.PluginTags;

public sealed record PluginTextCommit(string Before, string After, PluginTagContext Context)
{
    public bool HasChanges => !string.Equals(Before, After, StringComparison.Ordinal);
}

public sealed class PluginTextChangedEventArgs : EventArgs
{
    public PluginTextChangedEventArgs(string previousText, string currentText, bool isDirty)
    {
        PreviousText = previousText;
        CurrentText = currentText;
        IsDirty = isDirty;
    }

    public string PreviousText { get; }
    public string CurrentText { get; }
    public bool IsDirty { get; }
}

public sealed class PluginTextMutationException : Exception
{
    public PluginTextMutationException(string message) : base(message)
    {
    }
}

public sealed class PluginTextEditingSession
{
    private readonly PluginTagCatalog _catalog;
    private readonly PluginTextParser _parser;
    private readonly PluginTextRenderer _renderer;
    private string _originalText;
    private string _workingText;

    public PluginTextEditingSession(PluginTagCatalog catalog, PluginTagContext context, string? text = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        _parser = new PluginTextParser(catalog);
        _renderer = new PluginTextRenderer();
        _originalText = text ?? string.Empty;
        _workingText = _originalText;
        ParsedDocument = _parser.Parse(_workingText, Context);
    }

    public event EventHandler<PluginTextChangedEventArgs>? WorkingTextChanged;
    public event EventHandler? ParsedDocumentChanged;
    public event EventHandler? DirtyStateChanged;

    public PluginTagContext Context { get; }
    public string OriginalText => _originalText;
    public string WorkingText => _workingText;
    public PluginTextDocument ParsedDocument { get; private set; }
    public bool IsDirty => !string.Equals(_originalText, _workingText, StringComparison.Ordinal);

    public void ReplaceText(string? text)
    {
        ApplyWorkingText(text ?? string.Empty);
    }

    public void UpsertFeature(
        PluginFeatureKey key,
        PluginFeatureValues values,
        int occurrenceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (occurrenceIndex < 0) throw new ArgumentOutOfRangeException(nameof(occurrenceIndex));
        PluginFeatureDefinition definition = _catalog.GetRequiredFeature(key);
        EnsureApplicable(definition);
        string lineEnding = DetectPreferredLineEnding(_workingText);
        string rendered = _renderer.RenderFeature(definition, values, lineEnding);
        IReadOnlyList<PluginTextNode> occurrences = ParsedDocument.Find(key);

        if (occurrenceIndex < occurrences.Count)
        {
            PluginTextNode existing = occurrences[occurrenceIndex];
            string replacement = PreserveTrailingLineEnding(existing.RawText, rendered);
            ApplyWorkingText(ReplaceSpan(_workingText, existing.Span, replacement));
            return;
        }

        if (occurrenceIndex > occurrences.Count)
            throw new PluginTextMutationException("Cannot create a sparse feature occurrence.");
        if (definition.Multiplicity == PluginFeatureMultiplicity.Singleton && occurrences.Count > 0)
            throw new PluginTextMutationException("Feature is a singleton and already exists: " + key);

        string prefix = string.Empty;
        if (_workingText.Length > 0 && !EndsWithLineEnding(_workingText)) prefix = lineEnding;
        ApplyWorkingText(_workingText + prefix + rendered);
    }

    public int RemoveFeature(PluginFeatureKey key, int occurrenceIndex = 0, bool removeAll = false)
    {
        if (occurrenceIndex < 0) throw new ArgumentOutOfRangeException(nameof(occurrenceIndex));
        IReadOnlyList<PluginTextNode> occurrences = ParsedDocument.Find(key);
        if (occurrences.Count == 0) return 0;

        IReadOnlyList<PluginTextNode> selected;
        if (removeAll)
            selected = occurrences.OrderByDescending(node => node.Span.Start).ToArray();
        else
        {
            if (occurrenceIndex >= occurrences.Count) return 0;
            selected = [occurrences[occurrenceIndex]];
        }

        string updated = _workingText;
        foreach (PluginTextNode node in selected.OrderByDescending(node => node.Span.Start))
            updated = ReplaceSpan(updated, node.Span, string.Empty);
        ApplyWorkingText(updated);
        return selected.Count;
    }

    public void Revert() => ApplyWorkingText(_originalText);

    public PluginTextCommit Commit()
    {
        bool wasDirty = IsDirty;
        PluginTextCommit commit = new(_originalText, _workingText, Context);
        _originalText = _workingText;
        if (wasDirty) DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        return commit;
    }

    public IReadOnlyList<PluginTagDiagnostic> Validate(
        IReadOnlyDictionary<string, InstalledPluginStatus>? installedPlugins = null,
        IEnumerable<IPluginTagValidator>? additionalValidators = null)
    {
        PluginTagValidationContext validationContext = new(
            Context,
            installedPlugins ?? new Dictionary<string, InstalledPluginStatus>(StringComparer.OrdinalIgnoreCase),
            PluginInventoryAvailable: installedPlugins is not null);
        return new PluginTagValidator(additionalValidators).Validate(ParsedDocument, validationContext);
    }

    private void EnsureApplicable(PluginFeatureDefinition definition)
    {
        if (definition.Surface != Context.Surface)
            throw new PluginTextMutationException($"Feature {definition.Key} belongs to {definition.Surface}, not {Context.Surface}.");
        if (definition.DocumentKinds.Count > 0 &&
            !definition.DocumentKinds.Contains(PluginDocumentKind.Unknown) &&
            !definition.DocumentKinds.Contains(Context.DocumentKind))
            throw new PluginTextMutationException($"Feature {definition.Key} is not valid for {Context.DocumentKind}.");
    }

    private void ApplyWorkingText(string next)
    {
        if (string.Equals(_workingText, next, StringComparison.Ordinal)) return;
        bool wasDirty = IsDirty;
        string previous = _workingText;
        _workingText = next;
        ParsedDocument = _parser.Parse(_workingText, Context);
        bool isDirty = IsDirty;
        WorkingTextChanged?.Invoke(this, new PluginTextChangedEventArgs(previous, _workingText, isDirty));
        ParsedDocumentChanged?.Invoke(this, EventArgs.Empty);
        if (wasDirty != isDirty) DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string ReplaceSpan(string source, PluginTextSpan span, string replacement)
    {
        if (span.Start < 0 || span.End > source.Length) throw new ArgumentOutOfRangeException(nameof(span));
        return source.Substring(0, span.Start) + replacement + source.Substring(span.End);
    }

    private static string PreserveTrailingLineEnding(string original, string replacement)
    {
        string? ending = GetTrailingLineEnding(original);
        if (ending is null || EndsWithLineEnding(replacement)) return replacement;
        return replacement + ending;
    }

    private static string DetectPreferredLineEnding(string text)
    {
        int crlf = text.IndexOf("\r\n", StringComparison.Ordinal);
        if (crlf >= 0) return "\r\n";
        int lf = text.IndexOf('\n');
        if (lf >= 0) return "\n";
        int cr = text.IndexOf('\r');
        return cr >= 0 ? "\r" : Environment.NewLine;
    }

    private static string? GetTrailingLineEnding(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal)) return "\r\n";
        if (text.EndsWith("\n", StringComparison.Ordinal)) return "\n";
        if (text.EndsWith("\r", StringComparison.Ordinal)) return "\r";
        return null;
    }

    private static bool EndsWithLineEnding(string text) => GetTrailingLineEnding(text) is not null;
}
