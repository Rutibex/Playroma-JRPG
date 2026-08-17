using Playroma.Tile.Core.Models;
using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Serialization;

namespace Playroma.Tile.Materials.Validation;

public sealed record MaterialProjectIssue(MaterialIssueSeverity Severity, string MaterialName, string Message);

public sealed class MaterialProjectValidator
{
    private readonly MaterialNodeRegistry _registry = new();

    public IReadOnlyList<MaterialProjectIssue> Validate(TileProject project)
    {
        var issues = new List<MaterialProjectIssue>();
        foreach (var reference in project.Materials)
        {
            try
            {
                var recipe = MaterialSerializer.Load(project, reference);
                foreach (var issue in new MaterialGraphValidator(_registry).Validate(recipe.Graph))
                    issues.Add(new MaterialProjectIssue(issue.Severity, recipe.Name, issue.Message));
                if (recipe.ExposedParameters.Any(x => recipe.Graph.Find(x.NodeId) is null))
                    issues.Add(new MaterialProjectIssue(MaterialIssueSeverity.Warning, recipe.Name, "One or more exposed controls point to a missing node."));
            }
            catch (Exception ex)
            {
                issues.Add(new MaterialProjectIssue(MaterialIssueSeverity.Error, reference.Name, ex.Message));
            }
        }
        return issues;
    }
}
