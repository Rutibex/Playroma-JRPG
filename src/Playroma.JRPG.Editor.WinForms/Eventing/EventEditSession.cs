using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Application;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

public sealed record EventExternalConflict(
    string BaselineJson,
    string WorkingJson,
    string ExternalJson,
    DateTimeOffset DetectedUtc);

/// <summary>
/// Isolated working session for one map event, common event, or troop event. Changes stay local
/// until CommitAsync compiles the owned paths into one project transaction.
/// </summary>
public sealed class EventEditSession : IAsyncDisposable
{
    private const string MissingValue = "\u0000<missing>";

    private readonly MvProjectSession _project;
    private readonly MutableJsonProjectDocument _document;
    private readonly JsonPath _ownerPath;
    private readonly List<LocalHistoryEntry> _undo = [];
    private readonly List<LocalHistoryEntry> _redo = [];
    private JsonObject _workingOwner = [];
    private Dictionary<JsonPath, string> _baselineTargets = [];
    private string _baselineOwnerJson = "{}";
    private string? _externalOwnerJson;
    private bool _committing;
    private bool _disposed;
    private bool _hasConflict;
    private int _activePageIndex;

    public EventEditSession(
        MvProjectSession project,
        MutableJsonProjectDocument document,
        EventListAddress initialAddress)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        InitialAddress = initialAddress ?? throw new ArgumentNullException(nameof(initialAddress));
        Surface = initialAddress.Surface;
        RelativePath = initialAddress.NormalizedRelativePath;
        MapId = initialAddress.MapId;
        OwnerId = initialAddress.Surface == EventSurfaceKind.MapEvent
            ? initialAddress.EventId ?? 0
            : initialAddress.RecordId ?? 0;
        _ownerPath = Surface switch
        {
            EventSurfaceKind.MapEvent => JsonPath.Root.Append("events").Append(OwnerId),
            EventSurfaceKind.CommonEvent => JsonPath.Root.Append(OwnerId),
            EventSurfaceKind.TroopEvent => JsonPath.Root.Append(OwnerId),
            _ => throw new ArgumentOutOfRangeException(nameof(initialAddress))
        };
        _activePageIndex = Math.Max(0, initialAddress.PageIndex ?? 0);
        LoadFromDocument(clearHistory: true);
        _project.DocumentChanged += OnDocumentChanged;
    }

    public EventListAddress InitialAddress { get; }
    public EventSurfaceKind Surface { get; }
    public string RelativePath { get; }
    public int? MapId { get; }
    public int OwnerId { get; }
    public int ActivePageIndex => Surface == EventSurfaceKind.CommonEvent ? 0 : _activePageIndex;
    public int PageCount => Surface == EventSurfaceKind.CommonEvent ? 1 : GetPages().Count;
    public bool IsDirty => !TargetSetsEqual(_baselineTargets, CaptureWorkingTargets());
    public bool HasConflict => _hasConflict;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.Count == 0 ? null : _undo[^1].Description;
    public string? RedoDescription => _redo.Count == 0 ? null : _redo[^1].Description;
    public string Name => ReadString(_workingOwner, "name");
    public string Note => Surface == EventSurfaceKind.MapEvent ? ReadString(_workingOwner, "note") : string.Empty;
    public int X => Surface == EventSurfaceKind.MapEvent ? ReadInt(_workingOwner, "x") : 0;
    public int Y => Surface == EventSurfaceKind.MapEvent ? ReadInt(_workingOwner, "y") : 0;
    public string WorkingOwnerJson => FormatJson(_workingOwner.ToJsonString());
    public string BaselineOwnerJson => FormatJson(_baselineOwnerJson);
    public string BaselineFingerprint => HashText(_baselineOwnerJson);
    public string ExternalOwnerJson => FormatJson(_externalOwnerJson ?? ReadOwnerJsonFromDocument());

    public EventListAddress CurrentAddress => Surface switch
    {
        EventSurfaceKind.MapEvent => EventListAddress.ForMapEvent(RelativePath, MapId ?? 0, OwnerId, ActivePageIndex),
        EventSurfaceKind.CommonEvent => EventListAddress.ForCommonEvent(RelativePath, OwnerId),
        EventSurfaceKind.TroopEvent => EventListAddress.ForTroopEvent(RelativePath, OwnerId, ActivePageIndex),
        _ => throw new InvalidOperationException("Unknown event surface.")
    };

    public event EventHandler? Changed;
    public event EventHandler? DirtyStateChanged;
    public event EventHandler? ConflictChanged;
    public event EventHandler? ActivePageChanged;

    public void SetActivePage(int pageIndex)
    {
        ThrowIfDisposed();
        int clamped = Surface == EventSurfaceKind.CommonEvent
            ? 0
            : Math.Clamp(pageIndex, 0, Math.Max(0, PageCount - 1));
        if (_activePageIndex == clamped) return;
        _activePageIndex = clamped;
        ActivePageChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public MapEventHeaderSnapshot CaptureMapHeader()
    {
        EnsureSurface(EventSurfaceKind.MapEvent);
        return MapEventHeaderSnapshot.Parse(CurrentAddress, _workingOwner.ToJsonString());
    }

    public CommonEventSettingsSnapshot CaptureCommonSettings()
    {
        EnsureSurface(EventSurfaceKind.CommonEvent);
        return CommonEventSettingsSnapshot.Parse(CurrentAddress, _workingOwner.ToJsonString());
    }

    public EventPageSettingsSnapshot CaptureMapPageSettings()
    {
        EnsureSurface(EventSurfaceKind.MapEvent);
        return EventPageSettingsSnapshot.Parse(CurrentAddress, GetCurrentPage().ToJsonString());
    }

    public TroopEventPageSettingsSnapshot CaptureTroopPageSettings()
    {
        EnsureSurface(EventSurfaceKind.TroopEvent);
        return TroopEventPageSettingsSnapshot.Parse(CurrentAddress, GetCurrentPage().ToJsonString());
    }

    public EventListSnapshot CaptureCommandList()
    {
        JsonArray list = GetCurrentCommandList();
        string raw = list.ToJsonString();
        RawEventCommand[] commands = list
            .Select((node, index) => RawEventCommand.Parse(index, node?.ToJsonString() ?? "null"))
            .ToArray();
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return new EventListSnapshot(CurrentAddress, commands, raw, fingerprint, DateTimeOffset.UtcNow);
    }

    public string CurrentPageJson => Surface == EventSurfaceKind.CommonEvent
        ? WorkingOwnerJson
        : FormatJson(GetCurrentPage().ToJsonString());

    public string CurrentCommandJson(int rawIndex)
    {
        JsonArray list = GetCurrentCommandList();
        return rawIndex >= 0 && rawIndex < list.Count
            ? FormatJson(list[rawIndex]?.ToJsonString() ?? "null")
            : string.Empty;
    }

    public void UpdateMapHeader(string name, string note)
    {
        EnsureSurface(EventSurfaceKind.MapEvent);
        Mutate("Edit event name and note", "header", () =>
        {
            _workingOwner["name"] = name ?? string.Empty;
            _workingOwner["note"] = note ?? string.Empty;
        });
    }

    public void UpdateCommonSettings(CommonEventSettingsSnapshot snapshot)
    {
        EnsureSurface(EventSurfaceKind.CommonEvent);
        ArgumentNullException.ThrowIfNull(snapshot);
        Mutate("Edit common event settings", "common-settings", () =>
        {
            _workingOwner["name"] = snapshot.Name;
            _workingOwner["trigger"] = snapshot.Trigger;
            _workingOwner["switchId"] = snapshot.SwitchId;
        });
    }

    public void UpdateMapPageSettings(EventPageSettingsSnapshot snapshot)
    {
        EnsureSurface(EventSurfaceKind.MapEvent);
        ArgumentNullException.ThrowIfNull(snapshot);
        Mutate("Edit event page settings", $"map-page-settings:{ActivePageIndex}", () =>
        {
            JsonObject current = GetCurrentPage();
            JsonArray list = CloneArray(current["list"] as JsonArray ?? []);
            JsonObject replacement = JsonNode.Parse(snapshot.Compile()) as JsonObject
                ?? throw new InvalidDataException("The edited map event page is not an object.");
            replacement["list"] = list;
            ReplaceCurrentPage(replacement);
        });
    }

    public void UpdateTroopPageSettings(TroopEventPageSettingsSnapshot snapshot)
    {
        EnsureSurface(EventSurfaceKind.TroopEvent);
        ArgumentNullException.ThrowIfNull(snapshot);
        Mutate("Edit troop event page settings", $"troop-page-settings:{ActivePageIndex}", () =>
        {
            JsonObject current = GetCurrentPage();
            JsonArray list = CloneArray(current["list"] as JsonArray ?? []);
            JsonObject replacement = JsonNode.Parse(snapshot.Compile()) as JsonObject
                ?? throw new InvalidDataException("The edited troop event page is not an object.");
            replacement["list"] = list;
            ReplaceCurrentPage(replacement);
        });
    }

    public void AddPage(bool duplicateCurrent)
    {
        if (Surface == EventSurfaceKind.CommonEvent) return;
        Mutate(duplicateCurrent ? "Duplicate event page" : "Add event page", null, () =>
        {
            JsonArray pages = GetPages();
            int insertIndex = Math.Clamp(ActivePageIndex + 1, 0, pages.Count);
            JsonNode page = duplicateCurrent
                ? JsonNode.Parse(GetCurrentPage().ToJsonString())!
                : JsonNode.Parse(Surface == EventSurfaceKind.TroopEvent
                    ? EventPageDefaults.CreateTroopPageJson()
                    : EventPageDefaults.CreateMapPageJson())!;
            pages.Insert(insertIndex, page);
            _activePageIndex = insertIndex;
        });
        ActivePageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteCurrentPage()
    {
        if (Surface == EventSurfaceKind.CommonEvent || PageCount <= 1) return;
        Mutate("Delete event page", null, () =>
        {
            JsonArray pages = GetPages();
            pages.RemoveAt(ActivePageIndex);
            _activePageIndex = Math.Clamp(ActivePageIndex - 1, 0, pages.Count - 1);
        });
        ActivePageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearCurrentPage()
    {
        if (Surface == EventSurfaceKind.CommonEvent)
        {
            ReplaceCommandSpan(new EventCommandSpan(0, GetCurrentCommandList().Count),
                [RawEventCommand.CreateJson(0, 0, "[]")], "Clear common event commands");
            return;
        }
        Mutate("Clear event page", null, () =>
        {
            JsonObject page = JsonNode.Parse(Surface == EventSurfaceKind.TroopEvent
                ? EventPageDefaults.CreateTroopPageJson()
                : EventPageDefaults.CreateMapPageJson()) as JsonObject
                ?? throw new InvalidDataException("The default event page is invalid.");
            ReplaceCurrentPage(page);
        });
    }

    public void MoveCurrentPage(int direction)
    {
        if (Surface == EventSurfaceKind.CommonEvent || direction == 0) return;
        int target = ActivePageIndex + Math.Sign(direction);
        if (target < 0 || target >= PageCount) return;
        Mutate("Move event page", null, () =>
        {
            JsonArray pages = GetPages();
            JsonNode page = pages[ActivePageIndex]?.DeepClone()
                ?? throw new InvalidDataException("The selected event page is missing.");
            pages.RemoveAt(ActivePageIndex);
            pages.Insert(target, page);
            _activePageIndex = target;
        });
        ActivePageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string CopyCurrentPageJson() => Surface == EventSurfaceKind.CommonEvent
        ? GetCurrentCommandList().ToJsonString()
        : GetCurrentPage().ToJsonString();

    public void PasteCurrentPageJson(string rawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJson);
        if (Surface == EventSurfaceKind.CommonEvent)
        {
            JsonArray list = JsonNode.Parse(rawJson) as JsonArray
                ?? throw new InvalidDataException("The clipboard does not contain an event command array.");
            Mutate("Paste common event commands", null, () =>
            {
                _workingOwner["list"] = CloneArray(list);
                EnsureTerminator(GetCurrentCommandList());
            });
            return;
        }

        JsonObject page = JsonNode.Parse(rawJson) as JsonObject
            ?? throw new InvalidDataException("The clipboard does not contain an event page object.");
        Mutate("Paste event page", null, () => ReplaceCurrentPage(page));
    }

    public void ReplaceCommandSpan(
        EventCommandSpan span,
        IReadOnlyList<string> replacement,
        string description = "Edit event commands")
    {
        ArgumentNullException.ThrowIfNull(replacement);
        Mutate(description, null, () =>
        {
            JsonArray list = GetCurrentCommandList();
            if (span.StartIndex < 0 || span.EndExclusive > list.Count)
            {
                throw new InvalidOperationException(
                    $"Event command span {span.StartIndex}+{span.Length} exceeds list length {list.Count}.");
            }
            for (int index = 0; index < span.Length; index++) list.RemoveAt(span.StartIndex);
            for (int index = 0; index < replacement.Count; index++)
            {
                JsonNode command = JsonNode.Parse(replacement[index])
                    ?? throw new InvalidDataException("An event command is empty.");
                if (command is not JsonObject)
                {
                    throw new InvalidDataException("An event command must be a JSON object.");
                }
                list.Insert(span.StartIndex + index, command);
            }
            EnsureTerminator(list);
        });
    }

    public void InsertCommands(
        int rawIndex,
        int targetIndent,
        IReadOnlyList<string> commands,
        string description = "Insert event commands")
    {
        RawEventCommand[] parsed = commands.Select((raw, index) => RawEventCommand.Parse(index, raw)).ToArray();
        int sourceIndent = parsed.Length == 0 ? targetIndent : parsed.Min(row => row.Indent);
        IReadOnlyList<string> reindented = EventCommandFactory.Reindent(parsed, targetIndent - sourceIndent);
        ReplaceCommandSpan(new EventCommandSpan(rawIndex, 0), reindented, description);
    }

    public void Undo()
    {
        ThrowIfDisposed();
        if (_undo.Count == 0) return;
        LocalHistoryEntry entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
        RestoreHistory(entry.BeforeJson, entry.BeforePage);
    }

    public void Redo()
    {
        ThrowIfDisposed();
        if (_redo.Count == 0) return;
        LocalHistoryEntry entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
        RestoreHistory(entry.AfterJson, entry.AfterPage);
    }

    public void RestoreRecoveredWorkingState(
        string workingOwnerJson,
        int activePageIndex,
        string? expectedBaselineFingerprint)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(workingOwnerJson);
        bool dirtyBefore = IsDirty;
        string currentOwner = ReadOwnerJsonFromDocument();
        _baselineOwnerJson = currentOwner;
        _baselineTargets = CaptureDocumentTargets();
        _workingOwner = ParseOwner(workingOwnerJson);
        _activePageIndex = Surface == EventSurfaceKind.CommonEvent
            ? 0
            : Math.Clamp(activePageIndex, 0, Math.Max(0, PageCount - 1));
        _undo.Clear();
        _redo.Clear();
        NormalizeWorkingState();
        bool baselineMatches = string.IsNullOrWhiteSpace(expectedBaselineFingerprint)
            || string.Equals(HashText(currentOwner), expectedBaselineFingerprint, StringComparison.OrdinalIgnoreCase);
        SetConflict(!baselineMatches, baselineMatches ? null : currentOwner);
        Changed?.Invoke(this, EventArgs.Empty);
        ActivePageChanged?.Invoke(this, EventArgs.Empty);
        if (dirtyBefore != IsDirty) DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RevertToBaseline()
    {
        ThrowIfDisposed();
        _workingOwner = ParseOwner(_baselineOwnerJson);
        _activePageIndex = Math.Clamp(_activePageIndex, 0, Math.Max(0, PageCount - 1));
        _undo.Clear();
        _redo.Clear();
        SetConflict(false, null);
        RaiseChanged();
    }

    public void ReloadExternal()
    {
        ThrowIfDisposed();
        LoadFromDocument(clearHistory: true);
        RaiseChanged();
    }

    public void KeepLocalChanges()
    {
        ThrowIfDisposed();
        if (!_hasConflict) return;
        Dictionary<JsonPath, string> localTargets = CaptureWorkingTargets();
        JsonObject external = ParseOwner(ReadOwnerJsonFromDocument());
        ApplyTargetValues(external, localTargets);
        _workingOwner = external;
        _baselineTargets = CaptureDocumentTargets();
        _baselineOwnerJson = ReadOwnerJsonFromDocument();
        _activePageIndex = Math.Clamp(_activePageIndex, 0, Math.Max(0, PageCount - 1));
        SetConflict(false, null);
        RaiseChanged();
    }

    public EventExternalConflict CreateConflictComparison() => new(
        BaselineOwnerJson,
        WorkingOwnerJson,
        ExternalOwnerJson,
        DateTimeOffset.UtcNow);

    public async ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_hasConflict)
        {
            throw new InvalidOperationException(
                "The event changed outside this window. Resolve the conflict before applying edits.");
        }

        Dictionary<JsonPath, string> externalTargets = CaptureDocumentTargets();
        if (!TargetSetsEqual(_baselineTargets, externalTargets))
        {
            string externalOwner = ReadOwnerJsonFromDocument();
            SetConflict(true, externalOwner);
            throw new InvalidOperationException(
                "The event changed outside this window. Resolve the conflict before applying edits.");
        }

        Dictionary<JsonPath, string> working = CaptureWorkingTargets();
        TargetSpec[] changed = TargetSpecs()
            .Where(spec => !ValuesEqual(
                _baselineTargets.GetValueOrDefault(spec.Path, MissingValue),
                working.GetValueOrDefault(spec.Path, MissingValue)))
            .ToArray();
        if (changed.Length == 0) return false;

        _committing = true;
        try
        {
            await using ProjectTransaction transaction = _project.BeginTransaction(CommitDescription());
            transaction.Add(new AssertOwnedTargetsUnchangedCommand(
                RelativePath,
                _baselineTargets,
                "Verify the event has not changed externally"));
            foreach (TargetSpec spec in changed)
            {
                transaction.Add(new SetJsonValueCommand(
                    RelativePath,
                    spec.Path,
                    working[spec.Path],
                    spec.Description));
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(true);
            LoadFromDocument(clearHistory: true);
            RaiseChanged();
            return true;
        }
        catch
        {
            Dictionary<JsonPath, string> latestTargets = CaptureDocumentTargets();
            if (!TargetSetsEqual(_baselineTargets, latestTargets))
            {
                SetConflict(true, ReadOwnerJsonFromDocument());
            }
            throw;
        }
        finally
        {
            _committing = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _project.DocumentChanged -= OnDocumentChanged;
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private void Mutate(string description, string? mergeKey, Action operation)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(operation);
        string before = _workingOwner.ToJsonString();
        int beforePage = _activePageIndex;
        bool dirtyBefore = IsDirty;
        operation();
        NormalizeWorkingState();
        string after = _workingOwner.ToJsonString();
        int afterPage = _activePageIndex;
        if (string.Equals(before, after, StringComparison.Ordinal) && beforePage == afterPage) return;

        LocalHistoryEntry entry = new(description, mergeKey, before, after, beforePage, afterPage);
        if (mergeKey is not null && _undo.Count > 0 && string.Equals(_undo[^1].MergeKey, mergeKey, StringComparison.Ordinal))
        {
            LocalHistoryEntry previous = _undo[^1];
            _undo[^1] = previous with { AfterJson = after, AfterPage = afterPage, Description = description };
        }
        else
        {
            _undo.Add(entry);
        }
        _redo.Clear();
        bool dirtyAfter = IsDirty;
        Changed?.Invoke(this, EventArgs.Empty);
        if (dirtyBefore != dirtyAfter) DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreHistory(string raw, int page)
    {
        bool dirtyBefore = IsDirty;
        _workingOwner = ParseOwner(raw);
        _activePageIndex = Surface == EventSurfaceKind.CommonEvent
            ? 0
            : Math.Clamp(page, 0, Math.Max(0, PageCount - 1));
        NormalizeWorkingState();
        Changed?.Invoke(this, EventArgs.Empty);
        ActivePageChanged?.Invoke(this, EventArgs.Empty);
        if (dirtyBefore != IsDirty) DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadFromDocument(bool clearHistory)
    {
        bool dirtyBefore = _workingOwner.Count > 0 && IsDirty;
        string ownerRaw = ReadOwnerJsonFromDocument();
        _workingOwner = ParseOwner(ownerRaw);
        NormalizeWorkingState();
        _baselineOwnerJson = ownerRaw;
        _baselineTargets = CaptureDocumentTargets();
        if (clearHistory)
        {
            _undo.Clear();
            _redo.Clear();
        }
        SetConflict(false, null);
        if (dirtyBefore) DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NormalizeWorkingState()
    {
        if (Surface == EventSurfaceKind.CommonEvent)
        {
            EnsureTerminator(GetCurrentCommandList());
            _activePageIndex = 0;
            return;
        }
        JsonArray pages = GetPages();
        if (pages.Count == 0)
        {
            pages.Add(JsonNode.Parse(Surface == EventSurfaceKind.TroopEvent
                ? EventPageDefaults.CreateTroopPageJson()
                : EventPageDefaults.CreateMapPageJson()));
        }
        _activePageIndex = Math.Clamp(_activePageIndex, 0, pages.Count - 1);
        EnsureTerminator(GetCurrentCommandList());
    }

    private JsonArray GetPages()
    {
        if (_workingOwner["pages"] is JsonArray pages) return pages;
        pages = [];
        _workingOwner["pages"] = pages;
        return pages;
    }

    private JsonObject GetCurrentPage()
    {
        JsonArray pages = GetPages();
        if (pages.Count == 0) NormalizeWorkingState();
        if (pages[ActivePageIndex] is JsonObject page) return page;
        page = JsonNode.Parse(Surface == EventSurfaceKind.TroopEvent
            ? EventPageDefaults.CreateTroopPageJson()
            : EventPageDefaults.CreateMapPageJson()) as JsonObject
            ?? [];
        pages[ActivePageIndex] = page;
        return page;
    }

    private JsonArray GetCurrentCommandList()
    {
        JsonObject owner = Surface == EventSurfaceKind.CommonEvent ? _workingOwner : GetCurrentPage();
        if (owner["list"] is JsonArray list) return list;
        list = [];
        owner["list"] = list;
        return list;
    }

    private void ReplaceCurrentPage(JsonObject page)
    {
        JsonArray pages = GetPages();
        pages[ActivePageIndex] = JsonNode.Parse(page.ToJsonString());
    }

    private Dictionary<JsonPath, string> CaptureWorkingTargets()
    {
        Dictionary<JsonPath, string> result = [];
        foreach (TargetSpec spec in TargetSpecs())
        {
            if (!_workingOwner.TryGetPropertyValue(spec.PropertyName, out JsonNode? node))
            {
                result[spec.Path] = MissingValue;
                continue;
            }
            result[spec.Path] = Canonical(node?.ToJsonString() ?? "null");
        }
        return result;
    }

    private Dictionary<JsonPath, string> CaptureDocumentTargets()
    {
        Dictionary<JsonPath, string> result = [];
        foreach (TargetSpec spec in TargetSpecs())
        {
            string? raw = _document.GetRawJson(spec.Path);
            result[spec.Path] = raw is null ? MissingValue : Canonical(raw);
        }
        return result;
    }

    private TargetSpec[] TargetSpecs() => Surface switch
    {
        EventSurfaceKind.MapEvent =>
        [
            new(_ownerPath.Append("name"), "name", "Edit map event name"),
            new(_ownerPath.Append("note"), "note", "Edit map event note"),
            new(_ownerPath.Append("pages"), "pages", "Edit map event pages")
        ],
        EventSurfaceKind.CommonEvent =>
        [
            new(_ownerPath.Append("name"), "name", "Edit common event name"),
            new(_ownerPath.Append("trigger"), "trigger", "Edit common event trigger"),
            new(_ownerPath.Append("switchId"), "switchId", "Edit common event condition switch"),
            new(_ownerPath.Append("list"), "list", "Edit common event commands")
        ],
        EventSurfaceKind.TroopEvent =>
        [
            new(_ownerPath.Append("pages"), "pages", "Edit troop event pages")
        ],
        _ => []
    };

    private void ApplyTargetValues(JsonObject owner, IReadOnlyDictionary<JsonPath, string> values)
    {
        foreach (TargetSpec spec in TargetSpecs())
        {
            if (!values.TryGetValue(spec.Path, out string? raw) || raw == MissingValue) continue;
            owner[spec.PropertyName] = JsonNode.Parse(raw);
        }
    }

    private void OnDocumentChanged(object? sender, EditorCommandEvent e)
    {
        if (_disposed || _committing) return;
        if (!e.Targets.Any(target => string.Equals(
                target.NormalizedRelativePath,
                RelativePath,
                StringComparison.OrdinalIgnoreCase))) return;

        Dictionary<JsonPath, string> externalTargets = CaptureDocumentTargets();
        bool ownedChanged = !TargetSetsEqual(_baselineTargets, externalTargets);
        if (!ownedChanged)
        {
            MergeUnownedFieldsFromDocument();
            return;
        }

        if (IsDirty)
        {
            _externalOwnerJson = ReadOwnerJsonFromDocument();
            SetConflict(true, _externalOwnerJson);
        }
        else
        {
            LoadFromDocument(clearHistory: true);
            RaiseChanged();
        }
    }

    private void MergeUnownedFieldsFromDocument()
    {
        JsonObject external = ParseOwner(ReadOwnerJsonFromDocument());
        Dictionary<JsonPath, string> localTargets = CaptureWorkingTargets();
        ApplyTargetValues(external, localTargets);
        _workingOwner = external;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetConflict(bool value, string? externalOwner)
    {
        if (_hasConflict == value && string.Equals(_externalOwnerJson, externalOwner, StringComparison.Ordinal)) return;
        _hasConflict = value;
        _externalOwnerJson = externalOwner;
        ConflictChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        ActivePageChanged?.Invoke(this, EventArgs.Empty);
    }

    private string ReadOwnerJsonFromDocument() => _document.GetRawJson(_ownerPath)
        ?? throw new InvalidDataException($"Event owner '{_ownerPath}' no longer exists.");

    private string CommitDescription() => Surface switch
    {
        EventSurfaceKind.MapEvent => $"Edit map event {OwnerId:D3}",
        EventSurfaceKind.CommonEvent => $"Edit common event {OwnerId:D4}",
        EventSurfaceKind.TroopEvent => $"Edit troop event {OwnerId:D4}",
        _ => "Edit event"
    };

    private void EnsureSurface(EventSurfaceKind expected)
    {
        ThrowIfDisposed();
        if (Surface != expected)
        {
            throw new InvalidOperationException($"This edit session is for {Surface}, not {expected}.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static JsonObject ParseOwner(string raw) => JsonNode.Parse(raw) as JsonObject
        ?? throw new InvalidDataException("The event owner is not a JSON object.");

    private static JsonArray CloneArray(JsonArray source) => JsonNode.Parse(source.ToJsonString()) as JsonArray ?? [];

    private static void EnsureTerminator(JsonArray list)
    {
        if (list.Count == 0 || ReadCommandCode(list[list.Count - 1]) != 0)
        {
            list.Add(JsonNode.Parse(RawEventCommand.CreateJson(0, 0, "[]")));
        }
    }

    private static int ReadCommandCode(JsonNode? node)
    {
        if (node is not JsonObject value) return -1;
        return value["code"] is JsonValue code && code.TryGetValue(out int result) ? result : -1;
    }

    private static string ReadString(JsonObject owner, string property) =>
        owner[property] is JsonValue value && value.TryGetValue(out string? result) ? result ?? string.Empty : string.Empty;

    private static int ReadInt(JsonObject owner, string property) =>
        owner[property] is JsonValue value && value.TryGetValue(out int result) ? result : 0;

    private static string Canonical(string raw)
    {
        if (raw == MissingValue) return raw;
        JsonNode? node = JsonNode.Parse(raw);
        return node?.ToJsonString() ?? "null";
    }

    private static string HashText(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    private static string FormatJson(string raw)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static bool TargetSetsEqual(
        IReadOnlyDictionary<JsonPath, string> first,
        IReadOnlyDictionary<JsonPath, string> second)
    {
        if (first.Count != second.Count) return false;
        foreach ((JsonPath path, string value) in first)
        {
            if (!second.TryGetValue(path, out string? other) || !ValuesEqual(value, other)) return false;
        }
        return true;
    }

    private static bool ValuesEqual(string first, string second) => string.Equals(first, second, StringComparison.Ordinal);

    private sealed record TargetSpec(JsonPath Path, string PropertyName, string Description);

    /// <summary>
    /// Transaction precondition executed while the shared editor operation gate is held. This
    /// prevents another transaction from changing an owned event path between the UI preflight
    /// and the first event mutation.
    /// </summary>
    private sealed class AssertOwnedTargetsUnchangedCommand : IEditorCommand
    {
        private readonly IReadOnlyDictionary<JsonPath, string> _expected;

        public AssertOwnedTargetsUnchangedCommand(
            string relativePath,
            IReadOnlyDictionary<JsonPath, string> expected,
            string description)
        {
            RelativePath = relativePath.Replace('\\', '/');
            _expected = expected.ToDictionary(pair => pair.Key, pair => pair.Value);
            Description = description;
            Targets = _expected.Keys.Select(path => new EditorMutationTarget(RelativePath, path)).ToArray();
        }

        public Guid Id { get; } = Guid.NewGuid();
        public string RelativePath { get; }
        public string Description { get; }
        public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

        public async ValueTask ExecuteAsync(
            EditorCommandContext context,
            CancellationToken cancellationToken = default)
        {
            MutableJsonProjectDocument document = await context
                .OpenJsonDocumentAsync(RelativePath, cancellationToken)
                .ConfigureAwait(false);
            foreach ((JsonPath path, string expected) in _expected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? raw = document.GetRawJson(path);
                string current = raw is null ? MissingValue : Canonical(raw);
                if (!ValuesEqual(expected, current))
                {
                    throw new InvalidOperationException(
                        $"Event path '{path}' changed after the Event Editor began applying its working copy.");
                }
            }
        }

        public ValueTask UndoAsync(
            EditorCommandContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record LocalHistoryEntry(
        string Description,
        string? MergeKey,
        string BeforeJson,
        string AfterJson,
        int BeforePage,
        int AfterPage);
}
