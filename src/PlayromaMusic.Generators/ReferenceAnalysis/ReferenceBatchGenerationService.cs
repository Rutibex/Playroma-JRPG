using System.Collections.Concurrent;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.ReferenceAnalysis;

namespace PlayromaMusic.Generators.ReferenceAnalysis;

public sealed class ReferenceBatchGenerationService
{
    public ReferenceBatchReport Run(
        ReferenceCompositionBlueprint blueprint,
        ReferenceBatchRequest request,
        InstrumentCatalogDocument? catalog = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(request);
        int count = Math.Clamp(request.Count, 1, 100_000);
        ReferenceGenerationMode[] modes = request.Modes.Count == 0 ? [ReferenceGenerationMode.InspiredBy] : request.Modes.Distinct().ToArray();
        ConcurrentBag<ReferenceBatchSample> samples = new();
        ParallelOptions options = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(request.DegreeOfParallelism, 1, 64)
        };
        Parallel.For(0, count, options, index =>
        {
            ulong seed = SeedDerivation.Derive(request.RootSeed, "reference-batch", blueprint.Id, index.ToString());
            ReferenceGenerationMode mode = modes[index % modes.Length];
            try
            {
                ReferenceDescendantGenerationResult generated = new ReferenceDescendantGenerator().Generate(blueprint, new ReferenceDescendantRequest
                {
                    Seed = seed,
                    BlueprintId = blueprint.Id,
                    ModeOverride = mode,
                    CandidateCount = Math.Clamp(request.CandidatesPerSong, 1, 16)
                }, catalog);
                samples.Add(new ReferenceBatchSample
                {
                    Index = index,
                    Seed = seed,
                    Mode = mode,
                    Succeeded = true,
                    Evaluation = generated.Evaluation
                });
            }
            catch (Exception exception)
            {
                samples.Add(new ReferenceBatchSample
                {
                    Index = index,
                    Seed = seed,
                    Mode = mode,
                    Succeeded = false,
                    Error = exception.Message
                });
            }
        });

        ReferenceBatchSample[] ordered = samples.OrderBy(item => item.Index).ToArray();
        ReferenceDescendantEvaluation[] success = ordered.Where(item => item.Succeeded).Select(item => item.Evaluation).ToArray();
        ReferenceBatchReport report = new()
        {
            BlueprintId = blueprint.Id,
            RequestedCount = count,
            SuccessCount = success.Length,
            FailureCount = count - success.Length,
            FailureRate = (count - success.Length) / (double)count,
            MeanStructuralMatch = Mean(success, item => item.StructuralMatch),
            MeanHarmonicMatch = Mean(success, item => item.HarmonicMatch),
            MeanGrooveMatch = Mean(success, item => item.GrooveMatch),
            MeanArrangementMatch = Mean(success, item => item.ArrangementMatch),
            MeanTensionMatch = Mean(success, item => item.TensionMatch),
            MeanNovelty = Mean(success, item => item.NoveltyScore),
            MeanQuality = Mean(success, item => item.QualityScore),
            MeanExactNoteOverlap = Mean(success, item => item.ExactNoteOverlap),
            MeanIntervalNGramOverlap = Mean(success, item => item.IntervalNGramOverlap),
            OriginalityPassRate = success.Length == 0 ? 0 : success.Count(item => item.PassedOriginality) / (double)success.Length,
            Samples = ordered.ToList()
        };
        EvaluateExpectations(report, request.Expectations);
        return report;
    }

    private static double Mean(IReadOnlyCollection<ReferenceDescendantEvaluation> values, Func<ReferenceDescendantEvaluation, double> selector) =>
        values.Count == 0 ? 0 : values.Average(selector);

    private static void EvaluateExpectations(ReferenceBatchReport report, ReferenceBatchExpectations expectations)
    {
        if (report.FailureRate > expectations.MaximumFailureRate) report.FailedExpectations.Add($"Failure rate {report.FailureRate:P2} exceeds {expectations.MaximumFailureRate:P2}.");
        if (report.MeanStructuralMatch < expectations.MinimumMeanStructuralMatch) report.FailedExpectations.Add($"Mean structural match {report.MeanStructuralMatch:P1} is below {expectations.MinimumMeanStructuralMatch:P1}.");
        if (report.MeanGrooveMatch < expectations.MinimumMeanGrooveMatch) report.FailedExpectations.Add($"Mean groove match {report.MeanGrooveMatch:P1} is below {expectations.MinimumMeanGrooveMatch:P1}.");
        if (report.MeanArrangementMatch < expectations.MinimumMeanArrangementMatch) report.FailedExpectations.Add($"Mean arrangement match {report.MeanArrangementMatch:P1} is below {expectations.MinimumMeanArrangementMatch:P1}.");
        if (report.MeanNovelty < expectations.MinimumMeanNovelty) report.FailedExpectations.Add($"Mean novelty {report.MeanNovelty:P1} is below {expectations.MinimumMeanNovelty:P1}.");
        if (report.MeanExactNoteOverlap > expectations.MaximumMeanExactNoteOverlap) report.FailedExpectations.Add($"Mean exact-note overlap {report.MeanExactNoteOverlap:P1} exceeds {expectations.MaximumMeanExactNoteOverlap:P1}.");
        if (report.MeanIntervalNGramOverlap > expectations.MaximumMeanIntervalNGramOverlap) report.FailedExpectations.Add($"Mean interval overlap {report.MeanIntervalNGramOverlap:P1} exceeds {expectations.MaximumMeanIntervalNGramOverlap:P1}.");
        if (report.OriginalityPassRate < expectations.MinimumOriginalityPassRate) report.FailedExpectations.Add($"Originality pass rate {report.OriginalityPassRate:P1} is below {expectations.MinimumOriginalityPassRate:P1}.");
        report.PassedExpectations = report.FailedExpectations.Count == 0;
    }
}
