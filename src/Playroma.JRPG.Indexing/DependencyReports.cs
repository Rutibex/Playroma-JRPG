namespace Playroma.JRPG.Indexing;

public sealed record DependencyReport(
    ReferenceTarget Target,
    IReadOnlyList<ProjectReference> ExactReferences,
    IReadOnlyList<ProjectReference> HeuristicReferences)
{
    public int ExactCount => ExactReferences.Count;
    public int HeuristicCount => HeuristicReferences.Count;
    public int TotalCount => ExactCount + HeuristicCount;
    public bool HasBlockingReferences => ExactCount > 0;
    public bool HasReviewReferences => HeuristicCount > 0;
}

public static class DependencyReportBuilder
{
    public static DependencyReport Build(ProjectReferenceIndex index, ReferenceTarget target)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(target);
        ProjectReference[] inbound = index.FindInbound(target, includeHeuristic: true).ToArray();
        return new DependencyReport(
            target,
            inbound.Where(reference => reference.Confidence == ReferenceConfidence.Exact).ToArray(),
            inbound.Where(reference => reference.Confidence == ReferenceConfidence.Heuristic).ToArray());
    }
}
