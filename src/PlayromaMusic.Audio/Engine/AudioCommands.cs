using PlayromaMusic.Audio.Scheduling;
using PlayromaMusic.Core.Audio;

namespace PlayromaMusic.Audio.Engine;

internal abstract record AudioCommand;
internal sealed record PlayCommand : AudioCommand;
internal sealed record PauseCommand : AudioCommand;
internal sealed record StopCommand : AudioCommand;
internal sealed record SeekCommand(long Sample) : AudioCommand;
internal sealed record ReplaceSnapshotCommand(SongPlaybackSnapshot Snapshot, bool PreservePosition, long? AuditionFromSample) : AudioCommand;
internal sealed record SetLoopCommand(bool Enabled) : AudioCommand;
internal sealed record SetMasterVolumeCommand(double Volume) : AudioCommand;
internal sealed record SetTrackMixCommand(string TrackId, bool Muted, bool Solo, double Volume, double Pan) : AudioCommand;
internal sealed record RecreateBackendCommand(AudioProjectSettings Settings, string? FluidSynthLibraryPath) : AudioCommand;
internal sealed record ReportSubmittedCommand : AudioCommand;
internal sealed record ReportUnderrunCommand : AudioCommand;
internal sealed record ShutdownCommand : AudioCommand;
