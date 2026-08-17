using System.Text.Json.Serialization;
using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Audio;
using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.ReferenceAnalysis;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Core.Model;

public sealed class ProjectDocument
{
    public int SchemaVersion { get; set; } = BuildInfo.ProjectSchemaVersion;
    public string EngineVersion { get; set; } = BuildInfo.Version;
    public string ProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = "Untitled Song";
    public ulong MasterSeed { get; set; }
    public SongDocument Song { get; set; } = new();
    public GenerationLabDocument GenerationLab { get; set; } = new();
    public StructuredCompositionDocument StructuredComposition { get; set; } = new();
    public AdvancedCompositionDocument AdvancedComposition { get; set; } = new();
    public ProceduralOrchestrationDocument Orchestration { get; set; } = new();
    public ReferenceAnalysisDocument ReferenceAnalysis { get; set; } = new();
    public AudioProjectSettings Audio { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public sealed class SongDocument
{
    public string Title { get; set; } = "Untitled Song";
    public int TicksPerQuarterNote { get; set; } = BuildInfo.DefaultTicksPerQuarterNote;
    public PitchClass KeyRoot { get; set; } = PitchClass.C;
    public ScaleKind Scale { get; set; } = ScaleKind.Major;
    public List<TempoChange> TempoMap { get; set; } = new();
    public List<MeterChange> MeterMap { get; set; } = new();
    public List<SongSection> Sections { get; set; } = new();
    public List<SongTrack> Tracks { get; set; } = new();

    [JsonIgnore]
    public long LengthTicks
    {
        get
        {
            long sectionEnd = Sections.Count == 0 ? 0 : Sections.Max(section => section.EndTick);
            long clipEnd = Tracks.SelectMany(track => track.Clips).Select(clip => clip.EndTick).DefaultIfEmpty(0).Max();
            return Math.Max(sectionEnd, clipEnd);
        }
    }

    public IEnumerable<AbsoluteNote> EnumerateNotes()
    {
        foreach (SongTrack track in Tracks)
        {
            foreach (SongClip clip in track.Clips)
            {
                foreach (NoteEvent note in clip.Notes)
                {
                    yield return new AbsoluteNote(track, clip, note, clip.StartTick + note.StartTick);
                }
            }
        }
    }

    public IEnumerable<AbsoluteControlChange> EnumerateControlChanges()
    {
        foreach (SongTrack track in Tracks)
        {
            foreach (SongClip clip in track.Clips)
            {
                foreach (ControlChangeEvent controlChange in clip.ControlChanges)
                {
                    yield return new AbsoluteControlChange(track, clip, controlChange, clip.StartTick + controlChange.StartTick);
                }
            }
        }
    }
}

public sealed class TempoChange
{
    public long Tick { get; set; }
    public double BeatsPerMinute { get; set; } = 120.0;
}

public sealed class MeterChange
{
    public long Tick { get; set; }
    public int Numerator { get; set; } = 4;
    public int Denominator { get; set; } = 4;
}

public sealed class SongSection
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long StartTick { get; set; }
    public long LengthTicks { get; set; }
    public double Energy { get; set; }

    [JsonIgnore]
    public long EndTick => StartTick + LengthTicks;
}

public enum TrackRole
{
    Lead,
    Harmony,
    Chords,
    Arpeggio,
    Bass,
    Drums,
    CounterMelody,
    Texture
}

public sealed class SongTrack
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TrackRole Role { get; set; }
    public int MidiChannel { get; set; }
    public int GeneralMidiProgram { get; set; }
    public int MidiBank { get; set; }
    public string PresetKey { get; set; } = string.Empty;
    public string SoundFontId { get; set; } = string.Empty;
    public bool IsPercussion { get; set; }
    public bool Muted { get; set; }
    public bool Solo { get; set; }
    public double Volume { get; set; } = 1.0;
    public double Pan { get; set; }
    public string SourceVoiceId { get; set; } = string.Empty;
    public string GeneratorId { get; set; } = string.Empty;
    public int GenerationRevision { get; set; }
    public List<SongClip> Clips { get; set; } = new();

    [JsonIgnore]
    public int NoteCount => Clips.Sum(clip => clip.Notes.Count);

    [JsonIgnore]
    public int ControlChangeCount => Clips.Sum(clip => clip.ControlChanges.Count);
}

public sealed class SongClip
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long StartTick { get; set; }
    public long LengthTicks { get; set; }
    public List<NoteEvent> Notes { get; set; } = new();
    public List<ControlChangeEvent> ControlChanges { get; set; } = new();
    public List<ProgramChangeEvent> ProgramChanges { get; set; } = new();

    [JsonIgnore]
    public long EndTick => StartTick + LengthTicks;
}

public sealed class NoteEvent
{
    public string Id { get; set; } = string.Empty;
    public int Pitch { get; set; }
    public long StartTick { get; set; }
    public long DurationTicks { get; set; }
    public int Velocity { get; set; } = 80;

    [JsonIgnore]
    public long EndTick => StartTick + DurationTicks;
}

public sealed class ControlChangeEvent
{
    public string Id { get; set; } = string.Empty;
    public int ControllerNumber { get; set; }
    public int Value { get; set; }
    public long StartTick { get; set; }
}

public sealed class ProgramChangeEvent
{
    public string Id { get; set; } = string.Empty;
    public int Bank { get; set; }
    public int Program { get; set; }
    public long StartTick { get; set; }
    public string PresetKey { get; set; } = string.Empty;
    public string PresetName { get; set; } = string.Empty;
}

public readonly record struct AbsoluteNote(
    SongTrack Track,
    SongClip Clip,
    NoteEvent Note,
    long AbsoluteStartTick)
{
    public long AbsoluteEndTick => AbsoluteStartTick + Note.DurationTicks;
}

public readonly record struct AbsoluteControlChange(
    SongTrack Track,
    SongClip Clip,
    ControlChangeEvent ControlChange,
    long AbsoluteStartTick);
