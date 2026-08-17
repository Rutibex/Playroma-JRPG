namespace PlayromaMusic.Core.Batch;

public enum BatchMetric
{
    FailureRate,
    MeanNotesPerBeat,
    StandardDeviationNotesPerBeat,
    MeanPitch,
    MeanPitchRange,
    MeanVelocity,
    MeanMotifRecurrence,
    MeanArrangementContrast,
    PresetCoverageRatio,
    MeanUniquePresetsPerSong,
    MeanDuplicatePresetRate,
    MeanRoleSuitability,
    MeanOrchestrationContrast,
    MeanTimbralDiversity,
    MeanRegisterCollisionRate
}

public sealed class BatchStatisticalExpectation
{
    public string StyleId { get; set; } = "*";
    public BatchMetric Metric { get; set; }
    public double Minimum { get; set; } = -double.MaxValue;
    public double Maximum { get; set; } = double.MaxValue;
}

public sealed class BatchStatisticalTestResult
{
    public string StyleId { get; set; } = string.Empty;
    public BatchMetric Metric { get; set; }
    public double ObservedValue { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; }
    public bool Passed { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class BatchGenerationRequest
{
    public ulong StartingSeed { get; set; } = 1;
    public int SeedCount { get; set; } = 100;
    public List<string> StyleIds { get; set; } = new();
    public int MaximumDegreeOfParallelism { get; set; } = 1;
    public bool KeepFailures { get; set; } = true;
    public List<BatchStatisticalExpectation> Expectations { get; set; } = new();
}

public sealed class BatchGenerationResult
{
    public DateTimeOffset StartedAtUtc { get; set; }
    public TimeSpan Duration { get; set; }
    public int AttemptCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<BatchSampleMetrics> Samples { get; set; } = new();
    public Dictionary<string, BatchStyleSummary> Styles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Failures { get; set; } = new();
    public List<BatchStatisticalTestResult> StatisticalTests { get; set; } = new();
    public bool StatisticalTestsPassed => StatisticalTests.All(test => test.Passed);
}

public sealed class BatchSampleMetrics
{
    public ulong Seed { get; set; }
    public string StyleId { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public int NoteCount { get; set; }
    public int TrackCount { get; set; }
    public int SectionCount { get; set; }
    public double DurationBeats { get; set; }
    public double NotesPerBeat { get; set; }
    public double AveragePitch { get; set; }
    public double PitchRange { get; set; }
    public double AverageVelocity { get; set; }
    public double MotifRecurrence { get; set; }
    public double ArrangementContrast { get; set; }
    public int AvailablePresetCount { get; set; }
    public List<string> UsedPresetKeys { get; set; } = new();
    public double DuplicatePresetRate { get; set; }
    public double MeanRoleSuitability { get; set; }
    public double OrchestrationContrast { get; set; }
    public double TimbralDiversity { get; set; }
    public double RegisterCollisionRate { get; set; }
    public double ValidationScore { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public sealed class BatchStyleSummary
{
    public string StyleId { get; set; } = string.Empty;
    public int SampleCount { get; set; }
    public int FailureCount { get; set; }
    public double MeanNotesPerBeat { get; set; }
    public double StandardDeviationNotesPerBeat { get; set; }
    public double MeanPitch { get; set; }
    public double MeanPitchRange { get; set; }
    public double MeanVelocity { get; set; }
    public double MeanMotifRecurrence { get; set; }
    public double MeanArrangementContrast { get; set; }
    public double PresetCoverageRatio { get; set; }
    public double MeanUniquePresetsPerSong { get; set; }
    public double MeanDuplicatePresetRate { get; set; }
    public double MeanRoleSuitability { get; set; }
    public double MeanOrchestrationContrast { get; set; }
    public double MeanTimbralDiversity { get; set; }
    public double MeanRegisterCollisionRate { get; set; }
    public Dictionary<string, int> PresetUsage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
