namespace Playroma.JRPG.Editor.WinForms.Application;

public enum EditorApplicationState
{
    Starting,
    HubVisible,
    OpeningProject,
    ProjectOpen,
    ClosingProject,
    Exiting,
    Faulted
}

public sealed class EditorApplicationStateChangedEventArgs : EventArgs
{
    public EditorApplicationStateChangedEventArgs(
        EditorApplicationState previous,
        EditorApplicationState current)
    {
        Previous = previous;
        Current = current;
    }

    public EditorApplicationState Previous { get; }
    public EditorApplicationState Current { get; }
}
