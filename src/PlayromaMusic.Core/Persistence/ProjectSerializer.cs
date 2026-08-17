using System.Text.Json;
using System.Text.Json.Serialization;
using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Audio;
using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.ReferenceAnalysis;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Core.StylePacks;
using PlayromaMusic.Core.Validation;

namespace PlayromaMusic.Core.Persistence;

public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string ToJson(ProjectDocument project, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        JsonSerializerOptions options = new(Options) { WriteIndented = indented };
        return JsonSerializer.Serialize(project, options);
    }

    public static ProjectDocument FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        int sourceSchema = ReadSchemaVersion(json);
        if (sourceSchema is < 1 or > BuildInfo.ProjectSchemaVersion)
        {
            throw new NotSupportedException(
                $"Project schema {sourceSchema} is not supported by schema {BuildInfo.ProjectSchemaVersion}.");
        }

        ProjectDocument project = JsonSerializer.Deserialize<ProjectDocument>(json, Options)
            ?? throw new InvalidDataException("Project JSON did not contain a document.");

        switch (sourceSchema)
        {
            case 1:
                MigrateVersionOne(project);
                break;
            case 2:
                MigrateVersionTwo(project);
                break;
            case 3:
                MigrateVersionThree(project);
                break;
            case 4:
                MigrateVersionFour(project);
                break;
            case 5:
                MigrateVersionFive(project);
                break;
            case 6:
                MigrateVersionSix(project);
                break;
            case 7:
                MigrateVersionSeven(project);
                break;
        }

        if (project.SchemaVersion != BuildInfo.ProjectSchemaVersion)
        {
            throw new NotSupportedException(
                $"Project schema {project.SchemaVersion} is not supported by schema {BuildInfo.ProjectSchemaVersion}.");
        }

        NormalizeReferenceAnalysis(project);
        ProjectValidationResult validation = ProjectValidator.Validate(project);
        if (!validation.IsValid)
        {
            throw new InvalidDataException("Project validation failed: " + string.Join("; ", validation.Errors));
        }

        return project;
    }

    public static void Save(ProjectDocument project, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, ToJson(project));
        File.Move(temporaryPath, fullPath, true);
    }

    public static ProjectDocument Load(string filePath) =>
        FromJson(File.ReadAllText(filePath));

    public static ProjectDocument Clone(ProjectDocument project) =>
        FromJson(ToJson(project, indented: false));


    private static void NormalizeReferenceAnalysis(ProjectDocument project)
    {
        project.ReferenceAnalysis ??= new ReferenceAnalysisDocument();
        project.ReferenceAnalysis.ActiveBlueprint ??= new ReferenceCompositionBlueprint();
        project.ReferenceAnalysis.SavedBlueprints ??= new List<ReferenceCompositionBlueprint>();
        project.ReferenceAnalysis.Corpus ??= new ReferenceCorpusProfile();
        project.ReferenceAnalysis.RecentDescendants ??= new List<ReferenceDescendantEvaluation>();
        project.ReferenceAnalysis.Diagnostics ??= new List<string>();
        ReferenceBlueprintEditor.Normalize(project.ReferenceAnalysis.ActiveBlueprint);
        foreach (ReferenceCompositionBlueprint blueprint in project.ReferenceAnalysis.SavedBlueprints)
            ReferenceBlueprintEditor.Normalize(blueprint);
        project.ReferenceAnalysis.Corpus.BlueprintIds ??= new List<string>();
        project.ReferenceAnalysis.Corpus.Clusters ??= new List<ReferenceCorpusStyleCluster>();
        project.ReferenceAnalysis.Corpus.StyleWeights ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        project.ReferenceAnalysis.Corpus.FormWeights ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        project.ReferenceAnalysis.Corpus.HarmonicTransitionWeights ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        project.ReferenceAnalysis.Corpus.RhythmCellWeights ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        project.ReferenceAnalysis.Corpus.RoleWeights ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (ReferenceCorpusStyleCluster cluster in project.ReferenceAnalysis.Corpus.Clusters)
            cluster.BlueprintIds ??= new List<string>();
        foreach (ReferenceDescendantEvaluation evaluation in project.ReferenceAnalysis.RecentDescendants)
            evaluation.Diagnostics ??= new List<string>();
    }

    private static int ReadSchemaVersion(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (!document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement) ||
            !schemaElement.TryGetInt32(out int schemaVersion))
        {
            return 1;
        }

        return schemaVersion;
    }

    private static void MigrateVersionOne(ProjectDocument project)
    {
        project.SchemaVersion = BuildInfo.ProjectSchemaVersion;
        project.EngineVersion = BuildInfo.Version;
        project.GenerationLab ??= new GenerationLabDocument();
        project.GenerationLab.Enabled = false;
        project.StructuredComposition ??= new StructuredCompositionDocument();
        project.StructuredComposition.HybridStyle ??= new StyleBlendSettings();
        project.StructuredComposition.Enabled = false;
        project.AdvancedComposition ??= new AdvancedCompositionDocument();
        project.Orchestration ??= new ProceduralOrchestrationDocument();
        project.ReferenceAnalysis ??= new ReferenceAnalysisDocument();
        project.Audio ??= new AudioProjectSettings();
        InitializeTrackMix(project);
        if (!project.Tags.Contains("migrated-stage1", StringComparer.OrdinalIgnoreCase))
        {
            project.Tags.Add("migrated-stage1");
        }
    }

    private static void MigrateVersionTwo(ProjectDocument project)
    {
        project.SchemaVersion = BuildInfo.ProjectSchemaVersion;
        project.EngineVersion = BuildInfo.Version;
        project.StructuredComposition ??= new StructuredCompositionDocument();
        project.StructuredComposition.HybridStyle ??= new StyleBlendSettings();
        project.StructuredComposition.Enabled = false;
        project.AdvancedComposition ??= new AdvancedCompositionDocument();
        project.Orchestration ??= new ProceduralOrchestrationDocument();
        project.ReferenceAnalysis ??= new ReferenceAnalysisDocument();
        project.Audio ??= new AudioProjectSettings();
        InitializeTrackMix(project);
        if (!project.Tags.Contains("migrated-stage2", StringComparer.OrdinalIgnoreCase))
        {
            project.Tags.Add("migrated-stage2");
        }
    }

    private static void MigrateVersionThree(ProjectDocument project)
    {
        project.SchemaVersion = BuildInfo.ProjectSchemaVersion;
        project.EngineVersion = BuildInfo.Version;
        project.StructuredComposition ??= new StructuredCompositionDocument();
        project.StructuredComposition.HybridStyle ??= new StyleBlendSettings();
        project.StructuredComposition.ResolvedStylePackIds ??= new List<string>();
        if (project.StructuredComposition.ResolvedStylePackIds.Count == 0 && !string.IsNullOrWhiteSpace(project.StructuredComposition.ActiveStyleId))
        {
            project.StructuredComposition.ResolvedStylePackIds.Add(project.StructuredComposition.ActiveStyleId);
        }
        project.AdvancedComposition ??= new AdvancedCompositionDocument();
        project.Orchestration ??= new ProceduralOrchestrationDocument();
        project.ReferenceAnalysis ??= new ReferenceAnalysisDocument();
        project.Audio ??= new AudioProjectSettings();
        InitializeTrackMix(project);
        if (!project.Tags.Contains("migrated-stage3", StringComparer.OrdinalIgnoreCase))
        {
            project.Tags.Add("migrated-stage3");
        }
    }


    private static void MigrateVersionFour(ProjectDocument project)
    {
        project.SchemaVersion = BuildInfo.ProjectSchemaVersion;
        project.EngineVersion = BuildInfo.Version;
        project.StructuredComposition ??= new StructuredCompositionDocument();
        project.StructuredComposition.HybridStyle ??= new StyleBlendSettings();
        project.StructuredComposition.ResolvedStylePackIds ??= new List<string>();
        if (string.IsNullOrWhiteSpace(project.StructuredComposition.HybridStyle.PrimaryPackId))
        {
            project.StructuredComposition.HybridStyle.PrimaryPackId = project.StructuredComposition.ActiveStyleId;
        }
        if (project.StructuredComposition.ResolvedStylePackIds.Count == 0 &&
            !string.IsNullOrWhiteSpace(project.StructuredComposition.ActiveStyleId))
        {
            project.StructuredComposition.ResolvedStylePackIds.Add(project.StructuredComposition.ActiveStyleId);
        }
        project.AdvancedComposition ??= new AdvancedCompositionDocument();
        project.Orchestration ??= new ProceduralOrchestrationDocument();
        project.ReferenceAnalysis ??= new ReferenceAnalysisDocument();
        project.Audio ??= new AudioProjectSettings();
        InitializeTrackMix(project);
        if (!project.Tags.Contains("migrated-stage4", StringComparer.OrdinalIgnoreCase))
        {
            project.Tags.Add("migrated-stage4");
        }
    }

    private static void MigrateVersionFive(ProjectDocument project)
    {
        project.SchemaVersion = BuildInfo.ProjectSchemaVersion;
        project.EngineVersion = BuildInfo.Version;
        project.StructuredComposition ??= new StructuredCompositionDocument();
        project.StructuredComposition.HybridStyle ??= new StyleBlendSettings();
        project.StructuredComposition.ResolvedStylePackIds ??= new List<string>();
        project.AdvancedComposition ??= new AdvancedCompositionDocument();
        project.Orchestration ??= new ProceduralOrchestrationDocument();
        project.ReferenceAnalysis ??= new ReferenceAnalysisDocument();
        project.Audio ??= new AudioProjectSettings();
        InitializeTrackMix(project);
        if (!project.Tags.Contains("migrated-stage5", StringComparer.OrdinalIgnoreCase))
        {
            project.Tags.Add("migrated-stage5");
        }
    }

    private static void MigrateVersionSix(ProjectDocument project)
    {
        project.SchemaVersion = BuildInfo.ProjectSchemaVersion;
        project.EngineVersion = BuildInfo.Version;
        project.StructuredComposition ??= new StructuredCompositionDocument();
        project.StructuredComposition.HybridStyle ??= new StyleBlendSettings();
        project.StructuredComposition.ResolvedStylePackIds ??= new List<string>();
        project.AdvancedComposition ??= new AdvancedCompositionDocument();
        project.Orchestration ??= new ProceduralOrchestrationDocument();
        project.ReferenceAnalysis ??= new ReferenceAnalysisDocument();
        project.Audio ??= new AudioProjectSettings();
        InitializeTrackMix(project);
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles)
        {
            role.InstrumentSelector ??= new InstrumentSelectorSettings();
        }
        if (!project.Tags.Contains("migrated-stage6", StringComparer.OrdinalIgnoreCase))
        {
            project.Tags.Add("migrated-stage6");
        }
    }

    private static void MigrateVersionSeven(ProjectDocument project)
    {
        project.SchemaVersion = BuildInfo.ProjectSchemaVersion;
        project.EngineVersion = BuildInfo.Version;
        project.StructuredComposition ??= new StructuredCompositionDocument();
        project.StructuredComposition.HybridStyle ??= new StyleBlendSettings();
        project.StructuredComposition.ResolvedStylePackIds ??= new List<string>();
        project.AdvancedComposition ??= new AdvancedCompositionDocument();
        project.Orchestration ??= new ProceduralOrchestrationDocument();
        project.ReferenceAnalysis ??= new ReferenceAnalysisDocument();
        project.Audio ??= new AudioProjectSettings();
        InitializeTrackMix(project);
        if (!project.Tags.Contains("migrated-stage7", StringComparer.OrdinalIgnoreCase))
        {
            project.Tags.Add("migrated-stage7");
        }
    }

    private static void InitializeTrackMix(ProjectDocument project)
    {
        foreach (SongTrack track in project.Song.Tracks)
        {
            track.Volume = track.Volume <= 0 ? 1.0 : Math.Clamp(track.Volume, 0.0, 1.5);
            track.Pan = Math.Clamp(track.Pan, -1.0, 1.0);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
