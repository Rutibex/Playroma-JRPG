using System.Collections.Concurrent;
using System.Diagnostics;
using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Batch;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.Persistence;
using PlayromaMusic.Core.Validation;
using PlayromaMusic.Generators.Structured;

namespace PlayromaMusic.Generators.Batch;

public sealed class BatchGenerationService
{
    public BatchGenerationResult Run(BatchGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string[] styles = (request.StyleIds.Count == 0
                ? StructuredStyleLibrary.StylePacks.ConcretePacks.Select(pack => pack.Id)
                : request.StyleIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (styles.Length == 0) throw new InvalidOperationException("No concrete styles are available for batch generation.");
        int seedCount = Math.Clamp(request.SeedCount, 1, 100_000);
        List<(ulong Seed, string Style)> jobs = new(seedCount * styles.Length);
        for (int index = 0; index < seedCount; index++)
        foreach (string style in styles)
            jobs.Add((request.StartingSeed + (ulong)index, style));

        DateTimeOffset started = DateTimeOffset.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();
        ConcurrentBag<BatchSampleMetrics> samples = new();
        ConcurrentBag<string> failures = new();
        ParallelOptions options = new()
        {
            MaxDegreeOfParallelism = Math.Clamp(request.MaximumDegreeOfParallelism, 1, Environment.ProcessorCount),
            CancellationToken = cancellationToken
        };
        Parallel.ForEach(jobs, options, job =>
        {
            BatchSampleMetrics metrics = GenerateOne(job.Seed, job.Style);
            samples.Add(metrics);
            if (!metrics.Succeeded && request.KeepFailures) failures.Add($"{job.Style} seed {job.Seed}: {metrics.Error}");
        });
        stopwatch.Stop();

        List<BatchSampleMetrics> ordered = samples.OrderBy(item => item.StyleId, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Seed).ToList();
        BatchGenerationResult result = new()
        {
            StartedAtUtc = started,
            Duration = stopwatch.Elapsed,
            AttemptCount = ordered.Count,
            SuccessCount = ordered.Count(item => item.Succeeded),
            FailureCount = ordered.Count(item => !item.Succeeded),
            Samples = ordered,
            Failures = failures.OrderBy(item => item, StringComparer.Ordinal).ToList()
        };
        foreach (IGrouping<string, BatchSampleMetrics> group in ordered.GroupBy(item => item.StyleId, StringComparer.OrdinalIgnoreCase))
        {
            BatchSampleMetrics[] valid = group.Where(item => item.Succeeded).ToArray();
            result.Styles[group.Key] = Summarize(group.Key, group.Count(), group.Count(item => !item.Succeeded), valid);
        }
        result.StatisticalTests = EvaluateStatisticalTests(request.Expectations, result.Styles).ToList();
        return result;
    }

    private static BatchSampleMetrics GenerateOne(ulong seed, string style)
    {
        try
        {
            ProjectDocument project = new StructuredCompositionService().CreateProject(seed, style);
            ProjectValidationResult validation = ProjectValidator.Validate(project);
            AbsoluteNote[] notes = project.Song.EnumerateNotes().ToArray();
            double beats = Math.Max(1.0, project.Song.LengthTicks / (double)Math.Max(1, project.Song.TicksPerQuarterNote));
            double motifRecurrence = project.AdvancedComposition.MotifOccurrences.Count == 0
                ? 0
                : project.AdvancedComposition.MotifOccurrences.Count(item => item.Required) / (double)project.AdvancedComposition.MotifOccurrences.Count;
            double contrast = CalculateArrangementContrast(project);
            return new BatchSampleMetrics
            {
                Seed = seed,
                StyleId = style,
                Succeeded = validation.IsValid,
                NoteCount = notes.Length,
                TrackCount = project.Song.Tracks.Count,
                SectionCount = project.Song.Sections.Count,
                DurationBeats = beats,
                NotesPerBeat = notes.Length / beats,
                AveragePitch = notes.Length == 0 ? 0 : notes.Average(note => note.Note.Pitch),
                PitchRange = notes.Length == 0 ? 0 : notes.Max(note => note.Note.Pitch) - notes.Min(note => note.Note.Pitch),
                AverageVelocity = notes.Length == 0 ? 0 : notes.Average(note => note.Note.Velocity),
                MotifRecurrence = motifRecurrence,
                ArrangementContrast = contrast,
                AvailablePresetCount = project.Orchestration.Catalog.EnabledPresetCount,
                UsedPresetKeys = project.Orchestration.SectionPlans
                    .SelectMany(section => section.Assignments)
                    .Where(assignment => assignment.Active && !string.IsNullOrWhiteSpace(assignment.PresetKey))
                    .Select(assignment => assignment.PresetKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                DuplicatePresetRate = project.Orchestration.LastCoverage.DuplicatePresetRate,
                MeanRoleSuitability = project.Orchestration.LastCoverage.MeanRoleSuitability,
                OrchestrationContrast = project.Orchestration.LastCoverage.MeanSectionContrast,
                TimbralDiversity = project.Orchestration.LastCoverage.MeanTimbralDiversity,
                RegisterCollisionRate = project.Orchestration.LastCoverage.RegisterCollisionRate,
                ValidationScore = validation.IsValid ? 1.0 : 0.0,
                Fingerprint = ProjectFingerprint.Compute(project),
                Error = validation.IsValid ? string.Empty : string.Join("; ", validation.Errors)
            };
        }
        catch (Exception exception)
        {
            return new BatchSampleMetrics { Seed = seed, StyleId = style, Succeeded = false, Error = exception.Message };
        }
    }

    private static double CalculateArrangementContrast(ProjectDocument project)
    {
        IReadOnlyList<ArrangementSectionPlan> plan = project.AdvancedComposition.ArrangementPlan;
        if (plan.Count < 2) return 0;
        double total = 0;
        for (int index = 1; index < plan.Count; index++)
        {
            HashSet<string> previous = plan[index - 1].Roles.Where(role => role.Active).Select(role => role.RoleId).ToHashSet(StringComparer.Ordinal);
            HashSet<string> current = plan[index].Roles.Where(role => role.Active).Select(role => role.RoleId).ToHashSet(StringComparer.Ordinal);
            int union = previous.Union(current).Count();
            int changed = previous.SymmetricExceptCount(current);
            total += union == 0 ? 0 : changed / (double)union;
        }
        return total / (plan.Count - 1);
    }


    private static IEnumerable<BatchStatisticalTestResult> EvaluateStatisticalTests(
        IReadOnlyList<BatchStatisticalExpectation> expectations,
        IReadOnlyDictionary<string, BatchStyleSummary> summaries)
    {
        foreach (BatchStatisticalExpectation expectation in expectations)
        {
            string[] styleIds = expectation.StyleId == "*"
                ? summaries.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray()
                : summaries.Keys.Where(id => id.Equals(expectation.StyleId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (styleIds.Length == 0)
            {
                yield return new BatchStatisticalTestResult
                {
                    StyleId = expectation.StyleId,
                    Metric = expectation.Metric,
                    Minimum = expectation.Minimum,
                    Maximum = expectation.Maximum,
                    Passed = false,
                    Message = $"No generated style matched '{expectation.StyleId}'."
                };
                continue;
            }

            foreach (string styleId in styleIds)
            {
                BatchStyleSummary summary = summaries[styleId];
                double observed = ReadMetric(summary, expectation.Metric);
                bool boundsValid = double.IsFinite(expectation.Minimum) && double.IsFinite(expectation.Maximum) && expectation.Minimum <= expectation.Maximum;
                bool passed = boundsValid && double.IsFinite(observed) && observed >= expectation.Minimum && observed <= expectation.Maximum;
                yield return new BatchStatisticalTestResult
                {
                    StyleId = styleId,
                    Metric = expectation.Metric,
                    ObservedValue = observed,
                    Minimum = expectation.Minimum,
                    Maximum = expectation.Maximum,
                    Passed = passed,
                    Message = boundsValid
                        ? $"{expectation.Metric} observed {observed:0.####}; expected [{expectation.Minimum:0.####}, {expectation.Maximum:0.####}]."
                        : "Expectation bounds are invalid."
                };
            }
        }
    }

    private static double ReadMetric(BatchStyleSummary summary, BatchMetric metric) => metric switch
    {
        BatchMetric.FailureRate => summary.SampleCount == 0 ? 1.0 : summary.FailureCount / (double)summary.SampleCount,
        BatchMetric.MeanNotesPerBeat => summary.MeanNotesPerBeat,
        BatchMetric.StandardDeviationNotesPerBeat => summary.StandardDeviationNotesPerBeat,
        BatchMetric.MeanPitch => summary.MeanPitch,
        BatchMetric.MeanPitchRange => summary.MeanPitchRange,
        BatchMetric.MeanVelocity => summary.MeanVelocity,
        BatchMetric.MeanMotifRecurrence => summary.MeanMotifRecurrence,
        BatchMetric.MeanArrangementContrast => summary.MeanArrangementContrast,
        BatchMetric.PresetCoverageRatio => summary.PresetCoverageRatio,
        BatchMetric.MeanUniquePresetsPerSong => summary.MeanUniquePresetsPerSong,
        BatchMetric.MeanDuplicatePresetRate => summary.MeanDuplicatePresetRate,
        BatchMetric.MeanRoleSuitability => summary.MeanRoleSuitability,
        BatchMetric.MeanOrchestrationContrast => summary.MeanOrchestrationContrast,
        BatchMetric.MeanTimbralDiversity => summary.MeanTimbralDiversity,
        BatchMetric.MeanRegisterCollisionRate => summary.MeanRegisterCollisionRate,
        _ => double.NaN
    };

    private static BatchStyleSummary Summarize(string style, int sampleCount, int failureCount, IReadOnlyList<BatchSampleMetrics> valid)
    {
        double Mean(Func<BatchSampleMetrics, double> selector) => valid.Count == 0 ? 0 : valid.Average(selector);
        double meanDensity = Mean(item => item.NotesPerBeat);
        double variance = valid.Count == 0 ? 0 : valid.Select(item => Math.Pow(item.NotesPerBeat - meanDensity, 2)).Average();
        Dictionary<string, int> presetUsage = valid
            .SelectMany(item => item.UsedPresetKeys)
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        int availablePresets = valid.Select(item => item.AvailablePresetCount).DefaultIfEmpty(0).Max();
        return new BatchStyleSummary
        {
            StyleId = style,
            SampleCount = sampleCount,
            FailureCount = failureCount,
            MeanNotesPerBeat = meanDensity,
            StandardDeviationNotesPerBeat = Math.Sqrt(variance),
            MeanPitch = Mean(item => item.AveragePitch),
            MeanPitchRange = Mean(item => item.PitchRange),
            MeanVelocity = Mean(item => item.AverageVelocity),
            MeanMotifRecurrence = Mean(item => item.MotifRecurrence),
            MeanArrangementContrast = Mean(item => item.ArrangementContrast),
            PresetCoverageRatio = availablePresets <= 0 ? 0 : presetUsage.Count / (double)availablePresets,
            MeanUniquePresetsPerSong = Mean(item => item.UsedPresetKeys.Count),
            MeanDuplicatePresetRate = Mean(item => item.DuplicatePresetRate),
            MeanRoleSuitability = Mean(item => item.MeanRoleSuitability),
            MeanOrchestrationContrast = Mean(item => item.OrchestrationContrast),
            MeanTimbralDiversity = Mean(item => item.TimbralDiversity),
            MeanRegisterCollisionRate = Mean(item => item.RegisterCollisionRate),
            PresetUsage = presetUsage
        };
    }
}

internal static class SetExtensions
{
    public static int SymmetricExceptCount<T>(this IReadOnlySet<T> left, IReadOnlySet<T> right)
    {
        int count = left.Count(item => !right.Contains(item));
        count += right.Count(item => !left.Contains(item));
        return count;
    }
}
