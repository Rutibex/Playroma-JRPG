using Playroma.JRPG.Core;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Indexing;

namespace Playroma.JRPG.Validation;

public sealed class MissingReferenceValidator : IProjectValidator
{
    public string Name => "Missing references";

    public ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default)
    {
        List<ProjectValidationDiagnostic> diagnostics = [];
        foreach (ProjectReference reference in context.References.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.Confidence != ReferenceConfidence.Exact) continue;
            if (reference.Target.Kind is ReferenceTargetKind.Unknown or ReferenceTargetKind.Equipment or ReferenceTargetKind.TroopMember) continue;
            if (reference.Target.Id is <= 0) continue;
            if (context.Catalog.Exists(reference.Target)) continue;

            DiagnosticSeverity severity = reference.Target.Kind is ReferenceTargetKind.Asset or ReferenceTargetKind.Plugin
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error;
            ProjectLocation location = ToLocation(reference.Source);
            ProjectDiagnostic diagnostic = new(
                DiagnosticCodes.Phase3MissingReference,
                severity,
                $"{reference.Target} does not exist.",
                location,
                reference.Context);
            DiagnosticFix? fix = CreateSafeFix(reference);
            diagnostics.Add(new ProjectValidationDiagnostic(
                diagnostic,
                fix,
                "phase3/missing-reference"));
        }

        return ValueTask.FromResult<IReadOnlyList<ProjectValidationDiagnostic>>(diagnostics);
    }

    private static DiagnosticFix? CreateSafeFix(ProjectReference reference)
    {
        if (reference.Target.Kind == ReferenceTargetKind.Asset)
        {
            return new DiagnosticFix(
                "clear-missing-asset",
                "Clear missing asset reference",
                new SetJsonStringCommand(
                    reference.Source.RelativePath,
                    reference.Source.JsonPath,
                    string.Empty,
                    "Clear missing asset reference"));
        }

        if (reference.Target.Kind == ReferenceTargetKind.Animation)
        {
            return new DiagnosticFix(
                "clear-missing-animation",
                "Set animation to None",
                new SetJsonInt32Command(
                    reference.Source.RelativePath,
                    reference.Source.JsonPath,
                    0,
                    "Set missing animation to None"));
        }

        return null;
    }

    private static ProjectLocation ToLocation(ReferenceSource source) => new(
        source.RelativePath,
        source.JsonPath,
        source.MapId,
        source.RecordId,
        source.EventId,
        source.PageIndex,
        source.CommandIndex);
}
