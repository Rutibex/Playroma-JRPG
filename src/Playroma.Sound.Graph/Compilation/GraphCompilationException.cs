using Playroma.Sound.Graph.Validation;

namespace Playroma.Sound.Graph.Compilation;

public sealed class GraphCompilationException : Exception
{
    public GraphCompilationException(GraphValidationReport report)
        : base($"The sound graph contains {report.ErrorCount} validation error(s).")
    {
        Report = report ?? throw new ArgumentNullException(nameof(report));
    }

    public GraphValidationReport Report { get; }
}
