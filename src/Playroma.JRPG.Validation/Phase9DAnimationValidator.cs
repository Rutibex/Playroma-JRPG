using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Validation;

public sealed class Phase9DAnimationValidator : IProjectValidator
{
    public string Name => "Animations";

    public async ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default)
    {
        List<ProjectValidationDiagnostic> diagnostics = [];
        foreach (ProjectDocumentHandle handle in context.Workspace.Documents.Where(document => document.Kind == MvDocumentKind.Animations))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMutableProjectDocument document = await context.Workspace.OpenDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> bytes = await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(Utf8JsonInput.ToBomlessArray(bytes.Span));
            ValidateRoot(json.RootElement, handle.RelativePath, diagnostics);
        }
        return diagnostics;
    }

    private static void ValidateRoot(JsonElement root, string relativePath, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        foreach ((int index, JsonElement animation) in root.EnumerateIndexedArray())
        {
            if (index == 0 || animation.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (animation.ValueKind != JsonValueKind.Object)
            {
                Add(diagnostics, DiagnosticCodes.Phase9DInvalidAnimationRecord, DiagnosticSeverity.Error, $"Animation record {index} is not an object.", relativePath, $"/{index}", index);
                continue;
            }
            int id = ReadInt(animation, "id", index);
            int position = ReadInt(animation, "position", -1);
            if (position is < 0 or > 3)
            {
                Add(diagnostics, DiagnosticCodes.Phase9DInvalidAnimationRecord, DiagnosticSeverity.Error, $"Animation {id} has invalid position value {position}.", relativePath, $"/{index}/position", id);
            }

            int frameCount = 0;
            bool anyVisible = false;
            if (!animation.TryGetProperty("frames", out JsonElement frames) || frames.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, DiagnosticCodes.Phase9DInvalidAnimationFrame, DiagnosticSeverity.Error, $"Animation {id} has no valid frames array.", relativePath, $"/{index}/frames", id);
            }
            else
            {
                frameCount = frames.GetArrayLength();
                if (frameCount == 0)
                {
                    Add(diagnostics, DiagnosticCodes.Phase9DAnimationHasNoFrames, DiagnosticSeverity.Warning, $"Animation {id} has no frames.", relativePath, $"/{index}/frames", id);
                }
                foreach ((int frameIndex, JsonElement frame) in frames.EnumerateIndexedArray())
                {
                    if (frame.ValueKind != JsonValueKind.Array)
                    {
                        Add(diagnostics, DiagnosticCodes.Phase9DInvalidAnimationFrame, DiagnosticSeverity.Error, $"Animation {id} frame {frameIndex + 1} is not an array.", relativePath, $"/{index}/frames/{frameIndex}", id);
                        continue;
                    }
                    if (frame.GetArrayLength() > 16)
                    {
                        Add(diagnostics, DiagnosticCodes.Phase9DExtensionCellsPreserved, DiagnosticSeverity.Warning, $"Animation {id} frame {frameIndex + 1} stores {frame.GetArrayLength()} cells. MV Core renders only lanes 0–15; Playroma preserves the extra rows.", relativePath, $"/{index}/frames/{frameIndex}", id);
                    }
                    foreach ((int lane, JsonElement cell) in frame.EnumerateIndexedArray())
                    {
                        if (!ValidateCell(cell, out int pattern, out bool visible))
                        {
                            Add(diagnostics, DiagnosticCodes.Phase9DInvalidAnimationCell, DiagnosticSeverity.Error, $"Animation {id} frame {frameIndex + 1} lane {lane + 1} is not a valid eight-value MV cell array.", relativePath, $"/{index}/frames/{frameIndex}/{lane}", id);
                            continue;
                        }
                        anyVisible |= visible && lane < 16;
                        if (pattern > 199)
                        {
                            Add(diagnostics, DiagnosticCodes.Phase9DPatternOutOfRange, DiagnosticSeverity.Warning, $"Animation {id} frame {frameIndex + 1} lane {lane + 1} uses nonstandard pattern {pattern}; it is preserved unchanged.", relativePath, $"/{index}/frames/{frameIndex}/{lane}/0", id);
                        }
                    }
                }
            }

            if (frameCount > 0 && !anyVisible)
            {
                Add(diagnostics, DiagnosticCodes.Phase9DAnimationHasNoVisibleCells, DiagnosticSeverity.Warning, $"Animation {id} has frames but no visible cells in MV Core lanes 0–15.", relativePath, $"/{index}/frames", id);
            }

            if (!animation.TryGetProperty("timings", out JsonElement timings) || timings.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, DiagnosticCodes.Phase9DInvalidAnimationTiming, DiagnosticSeverity.Error, $"Animation {id} has no valid timings array.", relativePath, $"/{index}/timings", id);
                continue;
            }
            foreach ((int timingIndex, JsonElement timing) in timings.EnumerateIndexedArray())
            {
                ValidateTiming(timing, frameCount, id, index, timingIndex, relativePath, diagnostics);
            }
        }
    }

    private static bool ValidateCell(JsonElement cell, out int pattern, out bool visible)
    {
        pattern = -1;
        visible = false;
        if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() < 8) return false;
        JsonElement[] values = cell.EnumerateArray().Take(8).ToArray();
        if (!values[0].TryGetInt32(out pattern)) return false;
        bool numeric = values[1].ValueKind == JsonValueKind.Number
            && values[2].ValueKind == JsonValueKind.Number
            && values[3].ValueKind == JsonValueKind.Number
            && values[4].ValueKind == JsonValueKind.Number
            && values[6].ValueKind == JsonValueKind.Number
            && values[7].TryGetInt32(out _);
        bool mirror = values[5].ValueKind is JsonValueKind.True or JsonValueKind.False
            || values[5].ValueKind == JsonValueKind.Number && values[5].TryGetInt32(out int mirrorNumber) && mirrorNumber is 0 or 1;
        visible = pattern >= 0 && values[6].TryGetDouble(out double opacity) && opacity > 0;
        return numeric && mirror;
    }

    private static void ValidateTiming(
        JsonElement timing,
        int frameCount,
        int animationId,
        int recordIndex,
        int timingIndex,
        string relativePath,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        string path = $"/{recordIndex}/timings/{timingIndex}";
        if (timing.ValueKind != JsonValueKind.Object)
        {
            Add(diagnostics, DiagnosticCodes.Phase9DInvalidAnimationTiming, DiagnosticSeverity.Error, $"Animation {animationId} timing {timingIndex + 1} is not an object.", relativePath, path, animationId);
            return;
        }
        int frame = ReadInt(timing, "frame", -1);
        int scope = ReadInt(timing, "flashScope", -1);
        int duration = ReadInt(timing, "flashDuration", -1);
        bool validColor = timing.TryGetProperty("flashColor", out JsonElement color)
            && color.ValueKind == JsonValueKind.Array
            && color.GetArrayLength() >= 4
            && color.EnumerateArray().Take(4).All(component => component.ValueKind == JsonValueKind.Number);
        bool validSe = !timing.TryGetProperty("se", out JsonElement se)
            || se.ValueKind == JsonValueKind.Null
            || se.ValueKind == JsonValueKind.Object;
        if (scope is < 0 or > 3 || duration < 0 || !validColor || !validSe)
        {
            Add(diagnostics, DiagnosticCodes.Phase9DInvalidAnimationTiming, DiagnosticSeverity.Error, $"Animation {animationId} timing {timingIndex + 1} has invalid flash or sound data.", relativePath, path, animationId);
        }
        if (frame < 0 || frame >= frameCount)
        {
            Add(diagnostics, DiagnosticCodes.Phase9DTimingFrameOutOfRange, DiagnosticSeverity.Error, $"Animation {animationId} timing {timingIndex + 1} refers to frame {frame + 1}, outside the {frameCount}-frame animation.", relativePath, path + "/frame", animationId);
        }
    }

    private static void Add(
        ICollection<ProjectValidationDiagnostic> diagnostics,
        string code,
        DiagnosticSeverity severity,
        string message,
        string relativePath,
        string jsonPath,
        int recordId)
    {
        ProjectDiagnostic diagnostic = new(code, severity, message, new ProjectLocation(relativePath, jsonPath, RecordId: recordId));
        diagnostics.Add(new ProjectValidationDiagnostic(diagnostic, DocumentationKey: "phase9d/animations"));
    }

    private static int ReadInt(JsonElement element, string name, int fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement property) && property.TryGetInt32(out int value) ? value : fallback;
}
