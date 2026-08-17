using Playroma.JRPG.Core;
using Playroma.JRPG.Testing;

string root = RepositoryRootLocator.Find();
RepositoryAuditResult result = new RepositoryAuditEngine().Audit(root);
foreach (RepositoryAuditFinding finding in result.Findings)
{
    Console.WriteLine($"{finding.Severity.ToUpperInvariant()} {finding.Code} {finding.Path}: {finding.Message}");
}

if (!result.Success)
{
    Console.Error.WriteLine("Repository audit failed.");
    return 1;
}

Console.WriteLine("Repository audit passed.");
return 0;
