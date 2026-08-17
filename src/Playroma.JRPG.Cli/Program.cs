using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.Validation;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

return await CliApplication.RunAsync(args).ConfigureAwait(false);

internal static class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "probe" => await ProbeAsync(args[1..]).ConfigureAwait(false),
                "inspect" => await InspectAsync(args[1..]).ConfigureAwait(false),
                "roundtrip" => await RoundTripAsync(args[1..]).ConfigureAwait(false),
                "project" => await ProjectAsync(args[1..]).ConfigureAwait(false),
                "document" => await DocumentAsync(args[1..]).ConfigureAwait(false),
                "recovery" => await RecoveryAsync(args[1..]).ConfigureAwait(false),
                "references" => await ReferencesAsync(args[1..], requireTarget: false).ConfigureAwait(false),
                "dependencies" => await ReferencesAsync(args[1..], requireTarget: true).ConfigureAwait(false),
                "validate" => await ValidateAsync(args[1..]).ConfigureAwait(false),
                "diagnostics" => await ValidateAsync(args[1..]).ConfigureAwait(false),
                "assets" => await AssetsAsync(args[1..]).ConfigureAwait(false),
                "asset-audit" => await AssetAuditAsync(args[1..]).ConfigureAwait(false),
                "help" => PrintHelpAndReturn(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Fatal error: " + exception.Message);
            return 4;
        }
    }

    private static async Task<int> ProbeAsync(string[] args)
    {
        if (args.Length == 0) return Missing("probe requires a project path.");
        string path = args[0];
        bool json = Has(args, "--format", "json");
        MvProjectLoader loader = new();
        IReadOnlyList<MvProjectCandidate> candidates = await loader.ProbeAsync(path).ConfigureAwait(false);
        if (json)
        {
            WriteJson(new { schema = "playroma-jrpg-project-probe/1", selectedPath = Path.GetFullPath(path), candidates });
        }
        else
        {
            Console.WriteLine("Playroma jRPG Project Probe");
            Console.WriteLine($"Selected: {Path.GetFullPath(path)}");
            Console.WriteLine($"Candidates: {candidates.Count}");
            foreach (MvProjectCandidate candidate in candidates)
            {
                Console.WriteLine();
                Console.WriteLine(candidate.ProjectRoot);
                Console.WriteLine($"  Layout: {candidate.LayoutKind}");
                Console.WriteLine($"  Editability: {candidate.Editability}");
                Console.WriteLine($"  Confidence: {candidate.Confidence}");
                Console.WriteLine($"  Data: {candidate.DataRoot}");
                Console.WriteLine($"  Runtime: {candidate.RuntimeRoot ?? "Not found"}");
            }
        }

        return candidates.Count == 0 ? 3 : 0;
    }

    private static async Task<int> InspectAsync(string[] args)
    {
        if (args.Length == 0) return Missing("inspect requires a project path.");
        string path = args[0];
        bool complete = args.Contains("--complete", StringComparer.OrdinalIgnoreCase);
        bool json = Has(args, "--format", "json");
        MvProjectLoader loader = new();
        OperationResult<MvProjectSnapshot> result = await loader.LoadAsync(
            path,
            new ProjectLoadOptions(complete ? ProjectLoadDepth.Complete : ProjectLoadDepth.StandardData)).ConfigureAwait(false);
        if (json)
        {
            WriteJson(new { schema = "playroma-jrpg-project-report/1", snapshot = result.Value, diagnostics = result.Diagnostics });
        }
        else if (result.Value is not null)
        {
            PrintSnapshot(result.Value, complete);
        }
        else
        {
            PrintDiagnostics(result.Diagnostics);
        }

        return ExitCode(result.Diagnostics, result.Value is not null);
    }

    private static async Task<int> RoundTripAsync(string[] args)
    {
        if (args.Length < 2 || !args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
        {
            return Missing("Usage: roundtrip verify <project-path>");
        }

        OperationResult<MvProjectWorkspace> opened = await MvProjectWorkspace.OpenAsync(args[1]).ConfigureAwait(false);
        if (opened.Value is null)
        {
            PrintDiagnostics(opened.Diagnostics);
            return 3;
        }

        await using MvProjectWorkspace workspace = opened.Value;
        int checkedCount = 0;
        int failed = 0;
        foreach (ProjectDocumentHandle handle in workspace.Documents)
        {
            IMutableProjectDocument document = await workspace.OpenDocumentAsync(handle.RelativePath).ConfigureAwait(false);
            ReadOnlyMemory<byte> generated = await document.GenerateBytesAsync(CancellationToken.None).ConfigureAwait(false);
            bool equal = document.GetOriginalBytes().Span.SequenceEqual(generated.Span);
            checkedCount++;
            if (!equal)
            {
                failed++;
                Console.WriteLine("DIFFERENT " + handle.RelativePath);
            }
        }

        Console.WriteLine($"Verified {checkedCount:N0} documents; exact-byte failures: {failed:N0}.");
        return failed == 0 ? 0 : 2;
    }

    private static async Task<int> ProjectAsync(string[] args)
    {
        if (args.Length == 0) return Missing("project requires a subcommand: copy or compare.");
        return args[0].ToLowerInvariant() switch
        {
            "copy" => await ProjectCopyAsync(args[1..]).ConfigureAwait(false),
            "compare" => await ProjectCompareAsync(args[1..]).ConfigureAwait(false),
            _ => Missing("Unknown project subcommand. Use copy or compare.")
        };
    }

    private static async Task<int> ProjectCopyAsync(string[] args)
    {
        if (args.Length < 2) return Missing("Usage: project copy <source> <destination>");
        ProjectCopyService service = new();
        await service.CopyAsync(args[0], args[1], [], new ProjectCopyOptions()).ConfigureAwait(false);
        Console.WriteLine("Project copied and verified: " + Path.GetFullPath(args[1]));
        return 0;
    }

    private static async Task<int> ProjectCompareAsync(string[] args)
    {
        if (args.Length < 2) return Missing("Usage: project compare <left> <right> [--format json]");
        ProjectComparer comparer = new();
        IReadOnlyList<ProjectFileComparison> results = await comparer.CompareAsync(args[0], args[1]).ConfigureAwait(false);
        if (Has(args, "--format", "json"))
        {
            WriteJson(new { schema = "playroma-jrpg-project-comparison/1", files = results });
        }
        else
        {
            foreach (ProjectFileComparison result in results.Where(item => item.Kind != ProjectFileComparisonKind.IdenticalBytes))
            {
                Console.WriteLine($"{result.Kind,-18} {result.RelativePath}");
            }
            Console.WriteLine($"Compared {results.Count:N0} paths; {results.Count(item => item.Kind != ProjectFileComparisonKind.IdenticalBytes):N0} differences.");
        }

        return results.All(item => item.Kind == ProjectFileComparisonKind.IdenticalBytes) ? 0 : 1;
    }

    private static async Task<int> DocumentAsync(string[] args)
    {
        if (args.Length < 6 || !args[0].Equals("set-string", StringComparison.OrdinalIgnoreCase))
        {
            return Missing("Usage: document set-string <project> <relative-json-file> <json-pointer> <value> --save");
        }

        if (!args.Contains("--save", StringComparer.OrdinalIgnoreCase))
        {
            return Missing("The low-level mutation command requires the explicit --save flag.");
        }

        string project = args[1];
        string relative = args[2];
        JsonPath path = new(args[3]);
        string value = args[4];
        OperationResult<MvProjectWorkspace> opened = await MvProjectWorkspace.OpenAsync(project).ConfigureAwait(false);
        if (opened.Value is null)
        {
            PrintDiagnostics(opened.Diagnostics);
            return 3;
        }

        await using MvProjectWorkspace workspace = opened.Value;
        IMutableProjectDocument document = await workspace.OpenDocumentAsync(relative).ConfigureAwait(false);
        if (document is not MutableJsonProjectDocument json)
        {
            Console.Error.WriteLine("The selected document is not a mutable JSON document.");
            return 2;
        }

        json.SetString(path, value);
        ProjectSaveResult saved = await workspace.SaveAsync(new ProjectSaveOptions()).ConfigureAwait(false);
        PrintDiagnostics(saved.Diagnostics);
        Console.WriteLine(saved.Succeeded ? "Saved." : "Save failed; recovery data was retained when needed.");
        return saved.Succeeded ? 0 : 2;
    }

    private static async Task<int> RecoveryAsync(string[] args)
    {
        if (args.Length < 2) return Missing("Usage: recovery inspect|rollback|complete|cleanup <project> [transaction-id]");
        string command = args[0].ToLowerInvariant();
        string project = args[1];
        SaveRecoveryService service = new(project);
        IReadOnlyList<SaveRecoveryCandidate> candidates = await service.InspectAsync().ConfigureAwait(false);
        if (command == "inspect")
        {
            foreach (SaveRecoveryCandidate candidate in candidates)
            {
                Console.WriteLine($"{candidate.Journal.TransactionId:N} {candidate.Journal.State} {candidate.Recommendation}");
                Console.WriteLine("  " + candidate.TransactionDirectory);
            }
            if (candidates.Count == 0) Console.WriteLine("No incomplete save transactions were found.");
            return 0;
        }

        if (args.Length < 3) return Missing("rollback, complete, and cleanup require a transaction ID.");
        SaveRecoveryCandidate? selected = candidates.FirstOrDefault(candidate =>
            candidate.Journal.TransactionId.ToString("N").StartsWith(args[2], StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            Console.Error.WriteLine("Transaction not found.");
            return 3;
        }

        if (command == "rollback") await service.RollBackAsync(selected).ConfigureAwait(false);
        else if (command == "complete") await service.CompleteAsync(selected).ConfigureAwait(false);
        else if (command == "cleanup") await service.CleanupAsync(selected).ConfigureAwait(false);
        else return Missing("Unknown recovery subcommand.");
        Console.WriteLine("Recovery operation completed.");
        return 0;
    }

    private static async Task<int> ReferencesAsync(string[] args, bool requireTarget)
    {
        if (args.Length == 0) return Missing("references requires a project path.");
        string projectPath = args[0];
        bool json = Has(args, "--format", "json");
        bool exactOnly = args.Contains("--exact-only", StringComparer.OrdinalIgnoreCase);
        string? typeText = GetOption(args, "--type");
        string? idText = GetOption(args, "--id");
        string? name = GetOption(args, "--name");

        OperationResult<MvProjectWorkspace> opened = await MvProjectWorkspace.OpenAsync(projectPath).ConfigureAwait(false);
        if (opened.Value is null)
        {
            PrintDiagnostics(opened.Diagnostics);
            return 3;
        }

        await using MvProjectWorkspace workspace = opened.Value;
        ProjectReferenceIndex index = await new MvProjectReferenceIndexer().BuildAsync(workspace).ConfigureAwait(false);
        IReadOnlyList<ProjectReference> references;
        ReferenceTarget? target = null;
        if (!string.IsNullOrWhiteSpace(typeText))
        {
            if (!Enum.TryParse(typeText, ignoreCase: true, out ReferenceTargetKind kind))
            {
                return Missing("Unknown reference target type: " + typeText);
            }

            int? id = null;
            if (!string.IsNullOrWhiteSpace(idText))
            {
                if (!int.TryParse(idText, out int parsedId)) return Missing("--id must be an integer.");
                id = parsedId;
            }
            target = new ReferenceTarget(kind, id, name);
            if (requireTarget && !id.HasValue && string.IsNullOrWhiteSpace(name))
            {
                return Missing("dependencies requires --type and either --id or --name.");
            }

            references = !id.HasValue && string.IsNullOrWhiteSpace(name)
                ? index.All
                    .Where(reference => reference.Target.Kind == kind)
                    .Where(reference => !exactOnly || reference.Confidence == ReferenceConfidence.Exact)
                    .ToArray()
                : index.FindInbound(target, includeHeuristic: !exactOnly);
        }
        else
        {
            if (requireTarget) return Missing("dependencies requires --type and either --id or --name.");
            references = index.All
                .Where(reference => !exactOnly || reference.Confidence == ReferenceConfidence.Exact)
                .ToArray();
        }

        if (json)
        {
            WriteJson(new
            {
                schema = "playroma-jrpg-reference-report/1",
                target,
                statistics = index.Statistics,
                references
            });
        }
        else
        {
            Console.WriteLine("Playroma jRPG Reference Index");
            Console.WriteLine($"Documents: {index.Statistics.DocumentCount:N0}");
            Console.WriteLine($"Exact: {index.Statistics.ExactReferenceCount:N0}");
            Console.WriteLine($"Heuristic: {index.Statistics.HeuristicReferenceCount:N0}");
            if (target is not null) Console.WriteLine("Target: " + target);
            Console.WriteLine();
            foreach (ProjectReference reference in references.Take(10_000))
            {
                Console.WriteLine($"{reference.Confidence,-9} {reference.Target,-24} {reference.Source.RelativePath}{reference.Source.JsonPath}");
            }
            if (references.Count > 10_000) Console.WriteLine($"... {references.Count - 10_000:N0} more references omitted.");
            Console.WriteLine($"References shown: {Math.Min(references.Count, 10_000):N0} / {references.Count:N0}");
        }

        return 0;
    }

    private static async Task<int> ValidateAsync(string[] args)
    {
        if (args.Length == 0) return Missing("validate requires a project path.");
        string projectPath = args[0];
        bool json = Has(args, "--format", "json");
        OperationResult<MvProjectWorkspace> opened = await MvProjectWorkspace.OpenAsync(projectPath).ConfigureAwait(false);
        if (opened.Value is null)
        {
            PrintDiagnostics(opened.Diagnostics);
            return 3;
        }

        await using MvProjectWorkspace workspace = opened.Value;
        ProjectReferenceIndex references = await new MvProjectReferenceIndexer().BuildAsync(workspace).ConfigureAwait(false);
        ProjectValidationResult result = await new ProjectValidationEngine().ValidateAsync(workspace, references).ConfigureAwait(false);
        if (json)
        {
            WriteJson(new
            {
                schema = "playroma-jrpg-validation-report/1",
                project = workspace.Snapshot.Identity,
                references = references.Statistics,
                result
            });
        }
        else
        {
            Console.WriteLine("Playroma jRPG Project Validation");
            Console.WriteLine($"Errors: {result.ErrorCount:N0}");
            Console.WriteLine($"Warnings: {result.WarningCount:N0}");
            Console.WriteLine();
            foreach (ProjectValidationDiagnostic item in result.Diagnostics)
            {
                ProjectDiagnostic diagnostic = item.Diagnostic;
                string location = diagnostic.Location.RelativePath is null
                    ? string.Empty
                    : $" [{diagnostic.Location.RelativePath}{diagnostic.Location.JsonPath}]";
                Console.WriteLine($"{diagnostic.Severity,-11} {diagnostic.Code}{location} {diagnostic.Message}");
                if (item.Fix is not null) Console.WriteLine("  Safe fix: " + item.Fix.Title);
            }
        }

        return result.ErrorCount > 0 ? 2 : result.WarningCount > 0 ? 1 : 0;
    }

    private static async Task<int> AssetsAsync(string[] args)
    {
        if (args.Length == 0) return Missing("assets requires a project path.");
        string projectPath = args[0];
        bool json = Has(args, "--format", "json");
        string? queryText = GetOption(args, "--query");
        OperationResult<MvProjectSession> opened = await MvProjectSession.OpenAsync(
            projectPath,
            analysisLoadMode: ProjectAnalysisLoadMode.Blocking).ConfigureAwait(false);
        if (opened.Value is null)
        {
            PrintDiagnostics(opened.Diagnostics);
            return 3;
        }

        await using MvProjectSession session = opened.Value;
        AssetQuery query = string.IsNullOrWhiteSpace(queryText)
            ? new AssetQuery()
            : AssetQueryParser.Parse(queryText);
        IReadOnlyList<AssetRecord> assets = session.Assets.Search(query);
        if (json)
        {
            WriteJson(new
            {
                schema = "playroma-jrpg-asset-catalogue/1",
                project = session.Workspace.Snapshot.Identity,
                query,
                statistics = session.Assets.Statistics,
                assets
            });
        }
        else
        {
            AssetCatalogueStatistics statistics = session.Assets.Statistics;
            Console.WriteLine("Playroma jRPG Asset Catalogue");
            Console.WriteLine($"Logical assets: {statistics.LogicalAssetCount:N0}");
            Console.WriteLine($"Physical variants: {statistics.PhysicalVariantCount:N0}");
            Console.WriteLine($"Missing: {statistics.MissingAssetCount:N0}");
            Console.WriteLine($"Encrypted variants: {statistics.EncryptedVariantCount:N0}");
            Console.WriteLine();
            foreach (AssetRecord asset in assets.Take(20_000))
            {
                string variants = asset.Variants.Count == 0
                    ? "missing"
                    : string.Join(",", asset.Variants.Select(variant => variant.Extension.TrimStart('.')));
                Console.WriteLine($"{asset.Status,-18} {asset.Key.Category,-18} {asset.Key.LogicalName} [{variants}] exact:{asset.Usage.ExactReferenceCount} heuristic:{asset.Usage.HeuristicReferenceCount}");
            }
            if (assets.Count > 20_000) Console.WriteLine($"... {assets.Count - 20_000:N0} more assets omitted.");
            Console.WriteLine($"Assets shown: {Math.Min(assets.Count, 20_000):N0} / {assets.Count:N0}");
        }

        return assets.Any(asset => asset.IsMissing) ? 1 : 0;
    }

    private static async Task<int> AssetAuditAsync(string[] args)
    {
        if (args.Length == 0) return Missing("asset-audit requires a project path.");
        string projectPath = args[0];
        bool json = Has(args, "--format", "json");
        bool skipDuplicates = args.Contains("--skip-duplicates", StringComparer.OrdinalIgnoreCase);
        OperationResult<MvProjectSession> opened = await MvProjectSession.OpenAsync(
            projectPath,
            analysisLoadMode: ProjectAnalysisLoadMode.Blocking).ConfigureAwait(false);
        if (opened.Value is null)
        {
            PrintDiagnostics(opened.Diagnostics);
            return 3;
        }

        await using MvProjectSession session = opened.Value;
        AssetAuditReport report = await new AssetCatalogueAuditor().AuditAsync(
            session.Assets,
            includeDuplicateHashing: !skipDuplicates).ConfigureAwait(false);
        if (json)
        {
            WriteJson(new
            {
                schema = "playroma-jrpg-asset-audit/1",
                project = session.Workspace.Snapshot.Identity,
                statistics = session.Assets.Statistics,
                report
            });
        }
        else
        {
            Console.WriteLine("Playroma jRPG Asset Audit");
            Console.WriteLine($"Missing assets: {report.Missing.Count:N0}");
            Console.WriteLine($"No known references: {report.NoKnownReferences.Count:N0}");
            Console.WriteLine($"Exact duplicate groups: {report.ExactDuplicateGroups.Count:N0}");
            Console.WriteLine($"Case conflicts: {report.CaseConflicts.Count:N0}");
            Console.WriteLine($"Variant conflicts: {report.VariantConflicts.Count:N0}");
            Console.WriteLine();
            PrintDiagnostics(report.Diagnostics);
        }

        bool hasErrors = report.Diagnostics.Any(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
        return hasErrors ? 2 : report.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Warning) ? 1 : 0;
    }

    private static void PrintSnapshot(MvProjectSnapshot snapshot, bool complete)
    {
        Console.WriteLine("Playroma jRPG Project Inspector");
        Console.WriteLine($"Project: {snapshot.Identity.ProjectTitle ?? Path.GetFileName(snapshot.Identity.ProjectRoot)}");
        Console.WriteLine($"Layout: {snapshot.Layout.Kind}");
        Console.WriteLine($"Editability: {snapshot.Editability}");
        Console.WriteLine($"Maps: {snapshot.Maps.Count:N0}");
        Console.WriteLine($"Plugins: {snapshot.Plugins.EnabledCount:N0} enabled / {snapshot.Plugins.Entries.Count:N0} entries");
        Console.WriteLine($"Assets: {snapshot.Assets.Assets.Count:N0}");
        if (complete)
        {
            Console.WriteLine($"Map events: {snapshot.Maps.Sum(map => map.EventCount ?? 0):N0}");
            Console.WriteLine($"Event pages: {snapshot.Maps.Sum(map => map.EventPageCount ?? 0):N0}");
            Console.WriteLine($"Event commands: {snapshot.Maps.Sum(map => map.EventCommandCount ?? 0):N0}");
        }
        Console.WriteLine();
        PrintDiagnostics(snapshot.Diagnostics);
    }

    private static void PrintDiagnostics(IReadOnlyList<ProjectDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0) return;
        Console.WriteLine("Diagnostics");
        foreach (ProjectDiagnostic diagnostic in diagnostics)
        {
            string location = diagnostic.Location.RelativePath is null ? string.Empty : $" [{diagnostic.Location.RelativePath}]";
            Console.WriteLine($"  {diagnostic.Severity} {diagnostic.Code}{location} {diagnostic.Message}");
        }
    }

    private static string? GetOption(string[] args, string option)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }
        return null;
    }

    private static bool Has(string[] args, string option, string value)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase)
                && args[index + 1].Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static int ExitCode(IReadOnlyList<ProjectDiagnostic> diagnostics, bool hasValue)
    {
        if (!hasValue) return 3;
        if (diagnostics.Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)) return 2;
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)) return 1;
        return 0;
    }

    private static void WriteJson(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    private static bool IsHelp(string value) => value is "-h" or "--help" or "/?";
    private static int Missing(string message) { Console.Error.WriteLine(message); return 3; }
    private static int UnknownCommand(string command) { Console.Error.WriteLine("Unknown command: " + command); PrintHelp(); return 3; }
    private static int PrintHelpAndReturn() { PrintHelp(); return 0; }

    private static void PrintHelp()
    {
        Console.WriteLine("Playroma jRPG compatibility, editing, asset, and map-rendering tools");
        Console.WriteLine();
        Console.WriteLine("  probe <project> [--format json]");
        Console.WriteLine("  inspect <project> [--complete] [--format json]");
        Console.WriteLine("  roundtrip verify <project>");
        Console.WriteLine("  project copy <source> <destination>");
        Console.WriteLine("  project compare <left> <right> [--format json]");
        Console.WriteLine("  document set-string <project> <file> <json-pointer> <value> --save");
        Console.WriteLine("  recovery inspect <project>");
        Console.WriteLine("  recovery rollback|complete|cleanup <project> <transaction-id>");
        Console.WriteLine("  references <project> [--type skill] [--id 42] [--exact-only] [--format json]");
        Console.WriteLine("  dependencies <project> --type class --id 16 [--format json]");
        Console.WriteLine("  validate <project> [--format json]");
        Console.WriteLine("  assets <project> [--query \"category:characters used:true\"] [--format json]");
        Console.WriteLine("  asset-audit <project> [--skip-duplicates] [--format json]");
        Console.WriteLine();
        Console.WriteLine("Inspection, reference indexing, validation, catalogue browsing, and asset auditing are read-only. Save commands use external-change checks, backups, a transaction journal, and post-save verification.");
    }
}
