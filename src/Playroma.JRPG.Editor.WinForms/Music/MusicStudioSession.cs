using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.Persistence;
using PlayromaMusic.Core.ReferenceAnalysis;
using PlayromaMusic.Core.StylePacks;
using PlayromaMusic.Generators.Lab;
using PlayromaMusic.Generators.Orchestration;
using PlayromaMusic.Generators.ReferenceAnalysis;
using PlayromaMusic.Generators.Structured;
using PlayromaMusic.Midi;
using PlayromaMusic.Midi.ReferenceAnalysis;

namespace Playroma.JRPG.Editor.WinForms.Music;

/// <summary>
/// Owns a Playroma Music authoring document and exposes the same composition engines used by
/// the original MonoGame application to the native WinForms editor.
/// </summary>
public sealed class MusicStudioSession
{
    private readonly StructuredCompositionService _composer = new();
    private readonly LabGenerationService _lab = new();
    private readonly CompositionRandomizer _randomizer;
    private readonly ReferenceMidiAnalysisService _referenceAnalyzer = new();
    private readonly ReferenceDescendantGenerator _referenceGenerator = new();
    private readonly ProjectHistory _history = new(96);
    private string? _filePath;

    public MusicStudioSession(string projectRoot)
    {
        ProjectRoot = Path.GetFullPath(projectRoot ?? throw new ArgumentNullException(nameof(projectRoot)));
        _randomizer = new CompositionRandomizer(_composer);
        ReloadStylePacks();
        Project = CreateStructuredProject(NewSeed(), StructuredCompositionService.DefaultStyleId);
        MarkClean();
    }

    public string ProjectRoot { get; }
    public ProjectDocument Project { get; private set; }
    public string? FilePath => _filePath;
    public bool IsDirty { get; private set; }
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public string? NextUndoLabel => _history.NextUndoLabel;
    public string? NextRedoLabel => _history.NextRedoLabel;
    public string DefaultSoundFontPath => Path.Combine(AppContext.BaseDirectory, "SoundFonts", "default.sf2");

    public event EventHandler? ProjectChanged;
    public event Action<string>? StatusChanged;

    public void NewStructured(ulong seed, string styleId)
    {
        NewStructuredConfigured(seed, styleId, null, "Created structured composition");
    }

    public void NewStructuredConfigured(
        ulong seed,
        string styleId,
        Action<ProjectDocument>? configure,
        string status = "Created game music")
    {
        ProjectDocument project = CreateStructuredProject(seed, styleId);
        configure?.Invoke(project);
        PrepareAudio(project);
        Replace(project, status, clearHistory: true);
        _filePath = null;
    }

    public void NewStructuredConfiguredAndRebuilt(
        ulong seed,
        string styleId,
        Action<ProjectDocument> configureBeforeRebuild,
        Action<ProjectDocument>? finishAfterRebuild = null,
        string status = "Created game music")
    {
        ArgumentNullException.ThrowIfNull(configureBeforeRebuild);
        ProjectDocument project = CreateStructuredProject(seed, styleId);
        configureBeforeRebuild(project);
        _composer.RebuildFromCurrentPlans(project, replanOrchestration: false);
        finishAfterRebuild?.Invoke(project);
        PrepareAudio(project);
        Replace(project, status, clearHistory: true);
        _filePath = null;
    }

    public ProjectDocument CaptureSnapshot() => ProjectSerializer.Clone(Project);

    public void RestoreSnapshot(ProjectDocument snapshot, string status = "Restored saved version")
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _history.Capture(Project, status);
        Project = ProjectSerializer.Clone(snapshot);
        PrepareAudio(Project);
        _filePath = null;
        MarkChanged(status);
    }

    public void NewLaboratory(ulong seed, string presetId)
    {
        ProjectDocument project = _lab.CreateProject(seed, presetId);
        PrepareAudio(project);
        Replace(project, "Created generation-lab composition", clearHistory: true);
        _filePath = null;
    }

    public void NewUniverse()
    {
        ProjectDocument project = _randomizer.CreateNewUniverse(deterministicSeed: NewSeed());
        PrepareAudio(project);
        Replace(project, "Created a new musical universe", clearHistory: true);
        _filePath = null;
    }

    public void ApplyStyle(string styleId) => MutateReplacement("Apply style", project => _composer.ApplyStyle(project, styleId));

    public void ApplyHybrid(StyleBlendSettings settings) =>
        MutateReplacement("Apply hybrid style", project => _composer.ApplyHybrid(project, settings));

    public void RegenerateStructure() => Mutate("Regenerate form and harmony", _composer.RegenerateStructure);
    public void RegenerateMotif() => Mutate("Regenerate motif", _composer.RegenerateMotif);
    public void RegenerateAdvanced() => Mutate("Re-run advanced composition", _composer.RegenerateAdvanced);
    public void RebuildCurrentPlans() => Mutate("Rebuild current plans", project => _composer.RebuildFromCurrentPlans(project));
    public void RegenerateUnlockedRoles() => Mutate("Regenerate unlocked roles", _composer.RegenerateUnlocked);
    public void RegenerateRole(string roleId) => Mutate("Regenerate selected role", project => _composer.RegenerateRole(project, roleId, forceLocked: true));

    public void ApplyLabPreset(string presetId) => MutateReplacement("Apply generation-lab preset", project => _lab.ApplyPreset(project, presetId));
    public void RegenerateUnlockedVoices() =>
        Mutate("Regenerate unlocked voices", project => _lab.RegenerateUnlocked(project));
    public void RegenerateVoice(string voiceId) => Mutate("Regenerate selected voice", project => _lab.RegenerateVoice(project, voiceId, forceLocked: true));
    public void RebuildLab() => Mutate("Rebuild generation laboratory", _lab.RebuildAll);

    public void Randomize(RandomizationDomain domain) => Mutate("Randomize " + domain, project => _randomizer.RandomizeDomain(project, domain));

    public void Edit(string label, Action<ProjectDocument> mutation) => Mutate(label, mutation);

    public void EditAndRegenerateUnlockedRoles(
        string label,
        Action<ProjectDocument> setup,
        Action<ProjectDocument>? finish = null)
    {
        ArgumentNullException.ThrowIfNull(setup);
        Mutate(label, project =>
        {
            setup(project);
            _composer.RegenerateUnlocked(project);
            finish?.Invoke(project);
        });
    }

    public void EditAndRegenerateRole(
        string label,
        string roleId,
        Action<ProjectDocument> setup,
        Action<ProjectDocument>? finish = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentNullException.ThrowIfNull(setup);
        Mutate(label, project =>
        {
            setup(project);
            _composer.RegenerateRole(project, roleId, forceLocked: true);
            finish?.Invoke(project);
        });
    }

    public void ImportMidi(string path)
    {
        ProjectDocument imported = new MidiImportService().Import(path, NewSeed());
        PrepareAudio(imported);
        Replace(imported, "Imported MIDI", clearHistory: true);
        _filePath = null;
    }

    public ReferenceCompositionBlueprint AnalyzeReference(string midiPath)
    {
        _history.Capture(Project, "Analyze reference MIDI");
        ReferenceCompositionBlueprint result = _referenceAnalyzer.AnalyzeAndAttach(Project, midiPath);
        MarkChanged($"Analyzed reference MIDI: {Path.GetFileName(midiPath)}");
        return result;
    }


    public void SaveActiveBlueprint(string path)
    {
        ReferenceCompositionBlueprint blueprint = Project.ReferenceAnalysis.ActiveBlueprint;
        if (string.IsNullOrWhiteSpace(blueprint.Id))
        {
            throw new InvalidOperationException("Analyze or load a reference MIDI blueprint first.");
        }

        ReferenceBlueprintSerializer.Save(blueprint, path);
        StatusChanged?.Invoke($"Saved reference blueprint: {Path.GetFileName(path)}");
    }

    public void LoadBlueprint(string path)
    {
        ReferenceCompositionBlueprint loaded = ReferenceBlueprintSerializer.Load(path);
        Mutate("Load reference blueprint", project =>
        {
            project.ReferenceAnalysis.Enabled = true;
            project.ReferenceAnalysis.AnalysisRevision++;
            project.ReferenceAnalysis.ActiveBlueprint = loaded;
            project.ReferenceAnalysis.ActiveBlueprintId = loaded.Id;
            int existingIndex = project.ReferenceAnalysis.SavedBlueprints.FindIndex(item =>
                string.Equals(item.Id, loaded.Id, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                project.ReferenceAnalysis.SavedBlueprints[existingIndex] = ReferenceBlueprintSerializer.Clone(loaded);
            }
            else
            {
                project.ReferenceAnalysis.SavedBlueprints.Add(ReferenceBlueprintSerializer.Clone(loaded));
            }
        });
        StatusChanged?.Invoke($"Loaded reference blueprint: {Path.GetFileName(path)}");
    }

    public ReferenceDescendantGenerationResult GenerateDescendant(
        ReferenceGenerationMode mode,
        string styleId,
        ulong seed,
        int candidates)
    {
        ReferenceCompositionBlueprint blueprint = Project.ReferenceAnalysis.ActiveBlueprint;
        if (string.IsNullOrWhiteSpace(blueprint.Id))
        {
            throw new InvalidOperationException("Analyze or load a reference MIDI blueprint first.");
        }
        _history.Capture(Project, "Generate reference descendant");
        ReferenceDescendantGenerationResult result = _referenceGenerator.Generate(
            blueprint,
            new ReferenceDescendantRequest
            {
                Seed = seed,
                BlueprintId = blueprint.Id,
                ModeOverride = mode,
                TargetStyleId = styleId,
                CandidateCount = candidates
            },
            Project.Orchestration.Catalog);
        PrepareAudio(result.Project);
        Project = result.Project;
        MarkChanged($"Generated {mode} descendant; quality {result.Evaluation.QualityScore:0.000}.");
        return result;
    }

    public ReferenceCorpusAnalysisResult AnalyzeCorpus(string folder, int clusterCount)
    {
        string[] files = Directory.EnumerateFiles(folder, "*.mid", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(folder, "*.midi", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) throw new InvalidOperationException("The selected folder does not contain MIDI files.");
        return new ReferenceCorpusAnalysisService().Analyze(new ReferenceCorpusAnalysisRequest
        {
            Name = new DirectoryInfo(folder).Name,
            MidiPaths = files.ToList(),
            ClusterCount = Math.Clamp(clusterCount, 1, 32)
        });
    }

    public void Save(string? path = null)
    {
        string destination = Path.GetFullPath(path ?? _filePath ?? throw new InvalidOperationException("Choose a file name for the Playroma Music project."));
        ProjectSerializer.Save(Project, destination);
        _filePath = destination;
        MarkClean();
        StatusChanged?.Invoke($"Saved {Path.GetFileName(destination)}");
    }

    public void Load(string path)
    {
        ProjectDocument project = ProjectSerializer.Load(path);
        PrepareAudio(project);
        Project = project;
        _filePath = Path.GetFullPath(path);
        _history.Clear();
        MarkClean();
        RaiseProjectChanged();
        StatusChanged?.Invoke($"Loaded {Path.GetFileName(path)}");
    }

    public void Undo()
    {
        if (!_history.CanUndo) return;
        Project = _history.Undo(Project);
        PrepareAudio(Project);
        IsDirty = true;
        RaiseProjectChanged();
        StatusChanged?.Invoke("Undo complete.");
    }

    public void Redo()
    {
        if (!_history.CanRedo) return;
        Project = _history.Redo(Project);
        PrepareAudio(Project);
        IsDirty = true;
        RaiseProjectChanged();
        StatusChanged?.Invoke("Redo complete.");
    }

    public void ReloadStylePacks()
    {
        string outputPacks = Path.Combine(AppContext.BaseDirectory, "StylePacks");
        string sourcePacks = Path.Combine(ProjectRoot, "StylePacks");
        StructuredStyleLibrary.ReloadStylePacks([outputPacks, sourcePacks]);
        StatusChanged?.Invoke($"Loaded {StructuredStyleLibrary.All.Count} styles.");
    }

    public void NotifyPropertyGridEdit(string label)
    {
        IsDirty = true;
        RaiseProjectChanged();
        StatusChanged?.Invoke(label);
    }

    private ProjectDocument CreateStructuredProject(ulong seed, string styleId)
    {
        ProjectDocument project = _composer.CreateProject(seed, styleId);
        PrepareAudio(project);
        return project;
    }

    private void Mutate(string label, Action<ProjectDocument> mutation)
    {
        _history.Capture(Project, label);
        try
        {
            mutation(Project);
            PrepareAudio(Project);
            MarkChanged(label);
        }
        catch
        {
            Project = _history.Undo(Project);
            throw;
        }
    }

    private void MutateReplacement(string label, Func<ProjectDocument, ProjectDocument> mutation)
    {
        _history.Capture(Project, label);
        try
        {
            ProjectDocument replacement = mutation(Project);
            PrepareAudio(replacement);
            Project = replacement;
            MarkChanged(label);
        }
        catch
        {
            Project = _history.Undo(Project);
            throw;
        }
    }

    private void Replace(ProjectDocument replacement, string status, bool clearHistory)
    {
        Project = replacement;
        PrepareAudio(Project);
        if (clearHistory) _history.Clear();
        IsDirty = true;
        RaiseProjectChanged();
        StatusChanged?.Invoke(status);
    }

    private void PrepareAudio(ProjectDocument project)
    {
        if (string.IsNullOrWhiteSpace(project.Audio.SoundFontPath) || !File.Exists(project.Audio.SoundFontPath))
        {
            project.Audio.SoundFontPath = DefaultSoundFontPath;
        }
        project.Audio.PreferredBackend = PlayromaMusic.Core.Audio.SynthBackendPreference.MeltySynth;
    }

    private void MarkChanged(string status)
    {
        IsDirty = true;
        RaiseProjectChanged();
        StatusChanged?.Invoke(status);
    }

    private void MarkClean() => IsDirty = false;

    private void RaiseProjectChanged() => ProjectChanged?.Invoke(this, EventArgs.Empty);

    private static ulong NewSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }
}
