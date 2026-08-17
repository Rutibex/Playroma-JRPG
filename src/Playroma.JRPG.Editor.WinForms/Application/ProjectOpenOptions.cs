using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Application;

public sealed record ProjectOpenOptions(
    RecoveryCandidate? RecoveryCandidate = null,
    bool RestoreDocuments = true);
