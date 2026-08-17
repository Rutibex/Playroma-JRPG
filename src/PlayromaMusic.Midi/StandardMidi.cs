using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PlayromaMusic.Core.Analysis;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Persistence;
using PlayromaMusic.Core.Validation;

namespace PlayromaMusic.Midi;

public sealed record MidiExportResult(
    string FilePath,
    int TrackChunkCount,
    int NoteCount,
    int ControlChangeCount,
    int ProgramChangeCount,
    int TicksPerQuarterNote,
    string ProjectFingerprint)
{
    public string OutputPath => FilePath;
}

public sealed record MidiInspectionSummary(
    string FilePath,
    int Format,
    int TrackChunkCount,
    int TicksPerQuarterNote,
    long DurationTicks,
    int NoteCount,
    int ControlChangeCount,
    int ProgramChangeCount,
    int TempoChangeCount,
    int MeterChangeCount)
{
    public string SourcePath => FilePath;
    public bool IsMultiTrack => Format == 1 || TrackChunkCount > 1;
}

public sealed class MidiExportService
{
    public MidiExportResult Export(ProjectDocument project, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");

        List<SongTrack> playableTracks = GetPlayableTracks(project.Song.Tracks);
        List<byte[]> tracks = [BuildConductorTrack(project)];
        foreach (SongTrack track in playableTracks)
        {
            tracks.Add(BuildSongTrack(project, track));
        }

        using FileStream stream = File.Create(fullPath);
        WriteAscii(stream, "MThd");
        WriteUInt32BigEndian(stream, 6);
        WriteUInt16BigEndian(stream, 1);
        WriteUInt16BigEndian(stream, checked((ushort)tracks.Count));
        WriteUInt16BigEndian(stream, checked((ushort)project.Song.TicksPerQuarterNote));
        foreach (byte[] trackBytes in tracks)
        {
            WriteAscii(stream, "MTrk");
            WriteUInt32BigEndian(stream, checked((uint)trackBytes.Length));
            stream.Write(trackBytes);
        }

        return new MidiExportResult(
            fullPath,
            tracks.Count,
            playableTracks.Sum(track => track.NoteCount),
            playableTracks.Sum(track => track.ControlChangeCount),
            playableTracks.SelectMany(item => item.Clips).Sum(clip => clip.ProgramChanges.Count),
            project.Song.TicksPerQuarterNote,
            ProjectFingerprint.Compute(project));
    }

    private static List<SongTrack> GetPlayableTracks(IEnumerable<SongTrack> tracks)
    {
        List<SongTrack> available = tracks.Where(track => !track.Muted).ToList();
        return available.Any(track => track.Solo)
            ? available.Where(track => track.Solo).ToList()
            : available;
    }

    private static byte[] BuildConductorTrack(ProjectDocument project)
    {
        List<EncodedEvent> events = [];
        events.Add(EncodedEvent.Meta(0, 0, 0x03, Encoding.UTF8.GetBytes(project.Song.Title)));
        foreach (TempoChange tempo in project.Song.TempoMap)
        {
            int microseconds = checked((int)Math.Clamp(Math.Round(60_000_000d / Math.Max(1d, tempo.BeatsPerMinute)), 1, 0xFFFFFF));
            byte[] payload =
            [
                checked((byte)((microseconds >> 16) & 0xFF)),
                checked((byte)((microseconds >> 8) & 0xFF)),
                checked((byte)(microseconds & 0xFF))
            ];
            events.Add(EncodedEvent.Meta(tempo.Tick, 10, 0x51, payload));
        }
        foreach (MeterChange meter in project.Song.MeterMap)
        {
            int denominatorPower = 0;
            int denominator = Math.Max(1, meter.Denominator);
            while ((1 << denominatorPower) < denominator && denominatorPower < 7) denominatorPower++;
            events.Add(EncodedEvent.Meta(meter.Tick, 11, 0x58,
            [
                checked((byte)Math.Clamp(meter.Numerator, 1, 255)),
                checked((byte)denominatorPower),
                24,
                8
            ]));
        }
        foreach (SongSection section in project.Song.Sections)
        {
            events.Add(EncodedEvent.Meta(section.StartTick, 20, 0x06, Encoding.UTF8.GetBytes(section.Name)));
        }
        events.Add(EncodedEvent.Meta(Math.Max(0, project.Song.LengthTicks), 1000, 0x2F, []));
        return EncodeTrack(events);
    }

    private static byte[] BuildSongTrack(ProjectDocument project, SongTrack track)
    {
        List<EncodedEvent> events = [];
        int channel = Math.Clamp(track.MidiChannel, 0, 15);
        events.Add(EncodedEvent.Meta(0, 0, 0x03, Encoding.UTF8.GetBytes(track.Name)));
        events.Add(EncodedEvent.Channel(0, 1, 0xB0 | channel, 0, (track.MidiBank >> 7) & 0x7F));
        events.Add(EncodedEvent.Channel(0, 2, 0xB0 | channel, 32, track.MidiBank & 0x7F));
        events.Add(EncodedEvent.Channel(0, 3, 0xC0 | channel, track.GeneralMidiProgram));
        events.Add(EncodedEvent.Channel(0, 4, 0xB0 | channel, 7, checked((int)Math.Round(Math.Clamp(track.Volume, 0, 1) * 127))));
        events.Add(EncodedEvent.Channel(0, 5, 0xB0 | channel, 10, checked((int)Math.Round((Math.Clamp(track.Pan, -1, 1) + 1) * 63.5))));

        foreach (SongClip clip in track.Clips)
        {
            foreach (ProgramChangeEvent change in clip.ProgramChanges)
            {
                long tick = clip.StartTick + change.StartTick;
                events.Add(EncodedEvent.Channel(tick, 10, 0xB0 | channel, 0, (change.Bank >> 7) & 0x7F));
                events.Add(EncodedEvent.Channel(tick, 11, 0xB0 | channel, 32, change.Bank & 0x7F));
                events.Add(EncodedEvent.Channel(tick, 12, 0xC0 | channel, change.Program));
            }
            foreach (ControlChangeEvent control in clip.ControlChanges)
            {
                events.Add(EncodedEvent.Channel(
                    clip.StartTick + control.StartTick,
                    20,
                    0xB0 | channel,
                    control.ControllerNumber,
                    control.Value));
            }
            foreach (NoteEvent note in clip.Notes)
            {
                long start = clip.StartTick + note.StartTick;
                long end = start + Math.Max(1, note.DurationTicks);
                events.Add(EncodedEvent.Channel(end, 30, 0x80 | channel, note.Pitch, 0));
                events.Add(EncodedEvent.Channel(start, 40, 0x90 | channel, note.Pitch, note.Velocity));
            }
        }

        long endTick = Math.Max(project.Song.LengthTicks, events.Count == 0 ? 0 : events.Max(item => item.Tick));
        events.Add(EncodedEvent.Meta(endTick, 1000, 0x2F, []));
        return EncodeTrack(events);
    }

    private static byte[] EncodeTrack(IEnumerable<EncodedEvent> source)
    {
        using MemoryStream stream = new();
        long previousTick = 0;
        foreach (EncodedEvent item in source.OrderBy(item => item.Tick).ThenBy(item => item.Priority))
        {
            long tick = Math.Max(previousTick, item.Tick);
            WriteVariableLength(stream, checked((uint)(tick - previousTick)));
            stream.Write(item.Bytes);
            previousTick = tick;
        }
        return stream.ToArray();
    }

    internal static void WriteVariableLength(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[5];
        int index = 4;
        bytes[index] = checked((byte)(value & 0x7F));
        while ((value >>= 7) > 0)
        {
            index--;
            bytes[index] = checked((byte)((value & 0x7F) | 0x80));
        }
        stream.Write(bytes[index..]);
    }

    private static void WriteAscii(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));
    private static void WriteUInt16BigEndian(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }
    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private sealed record EncodedEvent(long Tick, int Priority, byte[] Bytes)
    {
        public static EncodedEvent Channel(long tick, int priority, int status, params int[] data)
        {
            byte[] bytes = new byte[data.Length + 1];
            bytes[0] = checked((byte)status);
            for (int index = 0; index < data.Length; index++)
            {
                bytes[index + 1] = checked((byte)Math.Clamp(data[index], 0, 127));
            }
            return new EncodedEvent(Math.Max(0, tick), priority, bytes);
        }

        public static EncodedEvent Meta(long tick, int priority, int type, byte[] payload)
        {
            using MemoryStream stream = new();
            stream.WriteByte(0xFF);
            stream.WriteByte(checked((byte)type));
            WriteVariableLength(stream, checked((uint)payload.Length));
            stream.Write(payload);
            return new EncodedEvent(Math.Max(0, tick), priority, stream.ToArray());
        }
    }
}

public static class MidiInspectionService
{
    public static MidiInspectionSummary Inspect(string midiPath)
    {
        StandardMidiFile file = StandardMidiReader.Read(midiPath);
        return new MidiInspectionSummary(
            Path.GetFullPath(midiPath),
            file.Format,
            file.Tracks.Count,
            file.TicksPerQuarterNote,
            file.Tracks.SelectMany(track => track.Events).Select(item => item.Tick).DefaultIfEmpty(0).Max(),
            file.Tracks.Sum(track => track.Events.Count(item => item.Kind == MidiEventKind.NoteOn && item.Data2 > 0)),
            file.Tracks.Sum(track => track.Events.Count(item => item.Kind == MidiEventKind.ControlChange)),
            file.Tracks.Sum(track => track.Events.Count(item => item.Kind == MidiEventKind.ProgramChange)),
            file.Tracks.Sum(track => track.Events.Count(item => item.Kind == MidiEventKind.Tempo)),
            file.Tracks.Sum(track => track.Events.Count(item => item.Kind == MidiEventKind.Meter)));
    }
}

public sealed class MidiImportService
{
    public ProjectDocument Import(string midiPath, ulong? seed = null)
    {
        StandardMidiFile file = StandardMidiReader.Read(midiPath);
        string fullPath = Path.GetFullPath(midiPath);
        List<MidiEventRecord> allEvents = file.Tracks.SelectMany(track => track.Events).OrderBy(item => item.Tick).ToList();
        double tempo = allEvents.FirstOrDefault(item => item.Kind == MidiEventKind.Tempo)?.TempoBpm ?? 120;
        MidiEventRecord? meterEvent = allEvents.FirstOrDefault(item => item.Kind == MidiEventKind.Meter);
        int numerator = meterEvent?.Data1 > 0 ? meterEvent.Data1 : 4;
        int denominator = meterEvent?.Data2 > 0 ? meterEvent.Data2 : 4;
        long maxTick = Math.Max(1, allEvents.Select(item => item.Tick).DefaultIfEmpty(file.TicksPerQuarterNote * 4L).Max());
        long barTicks = Math.Max(1, file.TicksPerQuarterNote * 4L / denominator * numerator);
        int barCount = checked((int)Math.Max(1, (maxTick + barTicks - 1) / barTicks));

        ProjectDocument project = new()
        {
            ProjectId = "midi-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath))).ToLowerInvariant()[..20],
            Title = Path.GetFileNameWithoutExtension(fullPath),
            MasterSeed = seed ?? SeedFromPath(fullPath),
            Tags = ["midi-import", "playroma-music"],
            Song = new SongDocument
            {
                Title = Path.GetFileNameWithoutExtension(fullPath),
                TicksPerQuarterNote = file.TicksPerQuarterNote,
                KeyRoot = PitchClass.C,
                Scale = ScaleKind.Major,
                TempoMap = [new TempoChange { Tick = 0, BeatsPerMinute = tempo }],
                MeterMap = [new MeterChange { Tick = 0, Numerator = numerator, Denominator = denominator }],
                Sections = BuildSections(file, barCount, barTicks)
            }
        };

        int generatedChannel = 0;
        foreach (StandardMidiTrack midiTrack in file.Tracks)
        {
            if (!midiTrack.Events.Any(item => item.Kind is MidiEventKind.NoteOn or MidiEventKind.ControlChange or MidiEventKind.ProgramChange))
            {
                continue;
            }

            List<MidiEventRecord> channelEvents = midiTrack.Events
                .Where(item => item.Channel >= 0)
                .OrderBy(item => item.Tick)
                .ToList();
            int channel = channelEvents.FirstOrDefault()?.Channel ?? NextMelodicChannel(ref generatedChannel);
            int bankMsb = 0;
            int bankLsb = 0;
            int program = 0;
            foreach (MidiEventRecord item in channelEvents)
            {
                if (item.Kind == MidiEventKind.ControlChange && item.Data1 == 0) bankMsb = item.Data2;
                if (item.Kind == MidiEventKind.ControlChange && item.Data1 == 32) bankLsb = item.Data2;
                if (item.Kind == MidiEventKind.ProgramChange) { program = item.Data1; break; }
            }
            int bank = Math.Clamp((bankMsb << 7) | bankLsb, 0, 16383);
            string trackId = $"track-{project.Song.Tracks.Count + 1:00}";
            SongTrack track = new()
            {
                Id = trackId,
                Name = string.IsNullOrWhiteSpace(midiTrack.Name) ? $"MIDI Track {project.Song.Tracks.Count + 1}" : midiTrack.Name,
                MidiChannel = Math.Clamp(channel, 0, 15),
                MidiBank = bank,
                GeneralMidiProgram = Math.Clamp(program, 0, 127),
                IsPercussion = channel == 9,
                Role = InferRole(channel == 9, program, channelEvents),
                SourceVoiceId = string.Empty,
                GeneratorId = "midi-import"
            };
            SongClip clip = new()
            {
                Id = trackId + "-clip-1",
                Name = track.Name,
                StartTick = 0,
                LengthTicks = Math.Max(1, maxTick + 1)
            };
            PopulateClip(channelEvents, clip, trackId);
            if (clip.ProgramChanges.Count == 0)
            {
                clip.ProgramChanges.Add(new ProgramChangeEvent
                {
                    Id = trackId + "-program-1",
                    Bank = bank,
                    Program = program,
                    StartTick = 0
                });
            }
            track.Clips.Add(clip);
            project.Song.Tracks.Add(track);
        }

        if (project.Song.Tracks.Count == 0)
        {
            project.Song.Tracks.Add(new SongTrack
            {
                Id = "track-01",
                Name = "Empty MIDI Track",
                Role = TrackRole.Lead,
                MidiChannel = 0,
                Clips =
                [
                    new SongClip { Id = "track-01-clip-1", Name = "Empty", StartTick = 0, LengthTicks = Math.Max(1, maxTick + 1) }
                ]
            });
        }

        ProjectValidationResult validation = ProjectValidator.Validate(project);
        if (!validation.IsValid)
        {
            throw new InvalidDataException("Imported MIDI failed validation: " + string.Join("; ", validation.Errors));
        }
        return project;
    }

    private static List<SongSection> BuildSections(StandardMidiFile file, int barCount, long barTicks)
    {
        List<MidiEventRecord> markers = file.Tracks.SelectMany(track => track.Events)
            .Where(item => item.Kind == MidiEventKind.Marker && !string.IsNullOrWhiteSpace(item.Text))
            .OrderBy(item => item.Tick)
            .ToList();
        if (markers.Count == 0)
        {
            return [new SongSection { Id = "section-01", Name = "Song", StartTick = 0, LengthTicks = Math.Max(barTicks, barCount * barTicks), Energy = 0.5 }];
        }
        List<SongSection> sections = [];
        for (int index = 0; index < markers.Count; index++)
        {
            long start = Math.Max(0, markers[index].Tick);
            long end = index + 1 < markers.Count ? Math.Max(start + 1, markers[index + 1].Tick) : Math.Max(start + barTicks, barCount * barTicks);
            sections.Add(new SongSection
            {
                Id = $"section-{index + 1:00}",
                Name = markers[index].Text,
                StartTick = start,
                LengthTicks = end - start,
                Energy = 0.5
            });
        }
        return sections;
    }

    private static void PopulateClip(List<MidiEventRecord> events, SongClip clip, string trackId)
    {
        Dictionary<(int Channel, int Pitch), Queue<MidiEventRecord>> active = new();
        int noteIndex = 0;
        int ccIndex = 0;
        int programIndex = 0;
        int bankMsb = 0;
        int bankLsb = 0;
        foreach (MidiEventRecord item in events)
        {
            switch (item.Kind)
            {
                case MidiEventKind.NoteOn when item.Data2 > 0:
                    if (!active.TryGetValue((item.Channel, item.Data1), out Queue<MidiEventRecord>? queue))
                    {
                        queue = new Queue<MidiEventRecord>();
                        active[(item.Channel, item.Data1)] = queue;
                    }
                    queue.Enqueue(item);
                    break;
                case MidiEventKind.NoteOff:
                case MidiEventKind.NoteOn when item.Data2 == 0:
                    if (active.TryGetValue((item.Channel, item.Data1), out Queue<MidiEventRecord>? noteQueue) && noteQueue.Count > 0)
                    {
                        MidiEventRecord start = noteQueue.Dequeue();
                        clip.Notes.Add(new NoteEvent
                        {
                            Id = $"{trackId}-note-{++noteIndex:00000}",
                            Pitch = start.Data1,
                            Velocity = Math.Clamp(start.Data2, 1, 127),
                            StartTick = start.Tick,
                            DurationTicks = Math.Max(1, item.Tick - start.Tick)
                        });
                    }
                    break;
                case MidiEventKind.ControlChange:
                    if (item.Data1 == 0) bankMsb = item.Data2;
                    else if (item.Data1 == 32) bankLsb = item.Data2;
                    clip.ControlChanges.Add(new ControlChangeEvent
                    {
                        Id = $"{trackId}-cc-{++ccIndex:00000}",
                        ControllerNumber = item.Data1,
                        Value = item.Data2,
                        StartTick = Math.Min(item.Tick, clip.LengthTicks - 1)
                    });
                    break;
                case MidiEventKind.ProgramChange:
                    clip.ProgramChanges.Add(new ProgramChangeEvent
                    {
                        Id = $"{trackId}-program-{++programIndex:00000}",
                        Bank = Math.Clamp((bankMsb << 7) | bankLsb, 0, 16383),
                        Program = item.Data1,
                        StartTick = Math.Min(item.Tick, clip.LengthTicks - 1)
                    });
                    break;
            }
        }
        foreach (Queue<MidiEventRecord> queue in active.Values)
        {
            while (queue.Count > 0)
            {
                MidiEventRecord start = queue.Dequeue();
                clip.Notes.Add(new NoteEvent
                {
                    Id = $"{trackId}-note-{++noteIndex:00000}",
                    Pitch = start.Data1,
                    Velocity = Math.Clamp(start.Data2, 1, 127),
                    StartTick = start.Tick,
                    DurationTicks = Math.Max(1, clip.LengthTicks - start.Tick - 1)
                });
            }
        }
        clip.Notes.Sort((left, right) => left.StartTick.CompareTo(right.StartTick));
    }

    private static TrackRole InferRole(bool percussion, int program, IReadOnlyCollection<MidiEventRecord> events)
    {
        if (percussion) return TrackRole.Drums;
        double average = events.Where(item => item.Kind == MidiEventKind.NoteOn && item.Data2 > 0).Select(item => item.Data1).DefaultIfEmpty(60).Average();
        if (average < 48 || program is >= 32 and <= 39) return TrackRole.Bass;
        if (program is >= 88 and <= 103) return TrackRole.Texture;
        if (program is >= 40 and <= 55) return TrackRole.Harmony;
        if (average > 72) return TrackRole.Lead;
        return TrackRole.Chords;
    }

    private static int NextMelodicChannel(ref int channel)
    {
        while (channel == 9) channel++;
        int result = channel % 16;
        channel++;
        return result;
    }

    private static ulong SeedFromPath(string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(0, 8));
    }
}

public sealed class MidiStyleAnalysisService
{
    public MidiStyleAnalysisReport Analyze(string midiPath) => Analyze(new MidiImportService().Import(midiPath), Path.GetFullPath(midiPath));
    public MidiStyleAnalysisReport Analyze(ProjectDocument project) => Analyze(project, string.Empty);

    private static MidiStyleAnalysisReport Analyze(ProjectDocument project, string sourcePath)
    {
        List<AbsoluteNote> notes = project.Song.EnumerateNotes().OrderBy(item => item.AbsoluteStartTick).ToList();
        int tpq = Math.Max(1, project.Song.TicksPerQuarterNote);
        double beats = Math.Max(1, project.Song.LengthTicks / (double)tpq);
        int minPitch = notes.Count == 0 ? 0 : notes.Min(item => item.Note.Pitch);
        int maxPitch = notes.Count == 0 ? 0 : notes.Max(item => item.Note.Pitch);
        double density = notes.Count / beats;
        double polyphony = CalculatePolyphony(notes);
        double syncopation = notes.Count == 0 ? 0 : notes.Count(item => Math.Abs((item.AbsoluteStartTick / (double)tpq) % 1.0) is > 0.20 and < 0.80) / (double)notes.Count;
        double repetition = CalculateRepetition(notes);
        double entropy = CalculatePitchEntropy(notes);
        double tempo = project.Song.TempoMap.FirstOrDefault()?.BeatsPerMinute ?? 120;
        MeterChange meter = project.Song.MeterMap.FirstOrDefault() ?? new MeterChange();

        Dictionary<string, double> scores = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ambient-drifting-light"] = ClampScore((1.2 - density / 2.5) * 0.45 + (1 - syncopation) * 0.25 + (maxPitch - minPitch < 30 ? 0.2 : 0.05)),
            ["dance-neon-floor"] = ClampScore((tempo is >= 110 and <= 150 ? 0.45 : 0.15) + syncopation * 0.25 + density / 12),
            ["jazz-after-hours"] = ClampScore(polyphony * 0.25 + entropy * 0.35 + syncopation * 0.25),
            ["pop-modern"] = ClampScore((tempo is >= 80 and <= 145 ? 0.35 : 0.15) + repetition * 0.35 + (density is >= 0.8 and <= 5 ? 0.25 : 0.1)),
            ["jrpg-battle-heroic"] = ClampScore((tempo >= 125 ? 0.4 : 0.15) + density / 10 + (maxPitch - minPitch > 30 ? 0.25 : 0.1)),
            ["jrpg-town-pastoral"] = ClampScore((tempo is >= 65 and <= 125 ? 0.35 : 0.15) + (1 - syncopation) * 0.2 + repetition * 0.25),
            ["algo-minimalist"] = ClampScore(repetition * 0.45 + (density < 3 ? 0.3 : 0.1) + (entropy < 0.7 ? 0.2 : 0.05)),
            ["retro-16bit-metroidvania"] = ClampScore((maxPitch - minPitch > 24 ? 0.25 : 0.1) + density / 8 + repetition * 0.25)
        };
        string suggested = scores.OrderByDescending(pair => pair.Value).First().Key;
        MidiStyleAnalysisReport report = new()
        {
            SourcePath = sourcePath,
            TrackCount = project.Song.Tracks.Count,
            NoteCount = notes.Count,
            EstimatedTempo = tempo,
            MeterNumerator = meter.Numerator,
            MeterDenominator = meter.Denominator,
            MinimumPitch = minPitch,
            MaximumPitch = maxPitch,
            NotesPerBeat = density,
            Polyphony = polyphony,
            Syncopation = syncopation,
            Repetition = repetition,
            PitchClassEntropy = entropy,
            SuggestedStyleId = suggested,
            StyleScores = scores
        };
        report.Observations.Add($"Detected {notes.Count} notes across {project.Song.Tracks.Count} musical tracks.");
        report.Observations.Add($"Estimated density is {density:0.00} notes per beat with {polyphony:0.00} average polyphony.");
        report.Observations.Add($"The closest built-in style heuristic is {suggested}.");
        return report;
    }

    private static double CalculatePolyphony(List<AbsoluteNote> notes)
    {
        if (notes.Count == 0) return 0;
        List<(long Tick, int Delta)> points = [];
        foreach (AbsoluteNote note in notes)
        {
            points.Add((note.AbsoluteStartTick, 1));
            points.Add((note.AbsoluteEndTick, -1));
        }
        points.Sort((left, right) => left.Tick != right.Tick ? left.Tick.CompareTo(right.Tick) : left.Delta.CompareTo(right.Delta));
        long previous = points[0].Tick;
        int active = 0;
        double weighted = 0;
        long span = 0;
        foreach ((long tick, int delta) in points)
        {
            long duration = Math.Max(0, tick - previous);
            weighted += active * duration;
            span += duration;
            active += delta;
            previous = tick;
        }
        return span == 0 ? 1 : weighted / span;
    }

    private static double CalculateRepetition(List<AbsoluteNote> notes)
    {
        if (notes.Count < 8) return 0;
        List<string> windows = [];
        for (int index = 0; index <= notes.Count - 4; index++)
        {
            windows.Add(string.Join(',', notes.Skip(index).Take(4).Select(item => item.Note.Pitch % 12)));
        }
        int unique = windows.Distinct(StringComparer.Ordinal).Count();
        return Math.Clamp(1 - unique / (double)Math.Max(1, windows.Count), 0, 1);
    }

    private static double CalculatePitchEntropy(List<AbsoluteNote> notes)
    {
        if (notes.Count == 0) return 0;
        double entropy = 0;
        foreach (IGrouping<int, AbsoluteNote> group in notes.GroupBy(item => item.Note.Pitch % 12))
        {
            double probability = group.Count() / (double)notes.Count;
            entropy -= probability * Math.Log(probability, 2);
        }
        return Math.Clamp(entropy / Math.Log(12, 2), 0, 1);
    }

    private static double ClampScore(double value) => Math.Clamp(value, 0, 1);
}

internal enum MidiEventKind
{
    NoteOff,
    NoteOn,
    ControlChange,
    ProgramChange,
    Tempo,
    Meter,
    TrackName,
    Marker,
    Other
}

internal sealed class MidiEventRecord
{
    public long Tick { get; init; }
    public MidiEventKind Kind { get; init; }
    public int Channel { get; init; } = -1;
    public int Data1 { get; init; }
    public int Data2 { get; init; }
    public double TempoBpm { get; init; }
    public string Text { get; init; } = string.Empty;
}

internal sealed class StandardMidiTrack
{
    public string Name { get; set; } = string.Empty;
    public List<MidiEventRecord> Events { get; } = [];
}

internal sealed class StandardMidiFile
{
    public int Format { get; init; }
    public int TicksPerQuarterNote { get; init; }
    public List<StandardMidiTrack> Tracks { get; } = [];
}

internal static class StandardMidiReader
{
    public static StandardMidiFile Read(string midiPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(midiPath);
        using FileStream stream = File.OpenRead(midiPath);
        if (ReadAscii(stream, 4) != "MThd") throw new InvalidDataException("The file does not begin with a Standard MIDI header.");
        uint headerLength = ReadUInt32BigEndian(stream);
        if (headerLength < 6) throw new InvalidDataException("The MIDI header is too short.");
        int format = ReadUInt16BigEndian(stream);
        int trackCount = ReadUInt16BigEndian(stream);
        int division = ReadUInt16BigEndian(stream);
        if ((division & 0x8000) != 0) throw new NotSupportedException("SMPTE MIDI time divisions are not supported.");
        if (headerLength > 6) stream.Seek(headerLength - 6, SeekOrigin.Current);
        StandardMidiFile result = new() { Format = format, TicksPerQuarterNote = Math.Max(24, division) };
        for (int index = 0; index < trackCount; index++)
        {
            string id = ReadAscii(stream, 4);
            uint length = ReadUInt32BigEndian(stream);
            byte[] payload = new byte[checked((int)length)];
            stream.ReadExactly(payload);
            if (id == "MTrk") result.Tracks.Add(ParseTrack(payload));
        }
        return result;
    }

    private static StandardMidiTrack ParseTrack(byte[] payload)
    {
        StandardMidiTrack track = new();
        int position = 0;
        long tick = 0;
        int runningStatus = 0;
        while (position < payload.Length)
        {
            uint delta = ReadVariableLength(payload, ref position);
            tick += delta;
            if (position >= payload.Length) break;
            int status = payload[position];
            if (status < 0x80)
            {
                if (runningStatus == 0) throw new InvalidDataException("MIDI running status was used before a channel status byte.");
                status = runningStatus;
            }
            else
            {
                position++;
                if (status < 0xF0) runningStatus = status;
            }

            if (status == 0xFF)
            {
                if (position >= payload.Length) break;
                int type = payload[position++];
                int length = checked((int)ReadVariableLength(payload, ref position));
                if (position + length > payload.Length) throw new InvalidDataException("A MIDI meta event extends past its track chunk.");
                ReadOnlySpan<byte> data = payload.AsSpan(position, length);
                position += length;
                MidiEventRecord? record = ParseMeta(tick, type, data);
                if (record is not null)
                {
                    track.Events.Add(record);
                    if (record.Kind == MidiEventKind.TrackName) track.Name = record.Text;
                }
                if (type == 0x2F) break;
                continue;
            }
            if (status is 0xF0 or 0xF7)
            {
                int length = checked((int)ReadVariableLength(payload, ref position));
                position = Math.Min(payload.Length, position + length);
                continue;
            }

            int kind = status & 0xF0;
            int channel = status & 0x0F;
            int data1 = ReadData(payload, ref position);
            int data2 = kind is 0xC0 or 0xD0 ? 0 : ReadData(payload, ref position);
            MidiEventKind eventKind = kind switch
            {
                0x80 => MidiEventKind.NoteOff,
                0x90 => MidiEventKind.NoteOn,
                0xB0 => MidiEventKind.ControlChange,
                0xC0 => MidiEventKind.ProgramChange,
                _ => MidiEventKind.Other
            };
            track.Events.Add(new MidiEventRecord { Tick = tick, Kind = eventKind, Channel = channel, Data1 = data1, Data2 = data2 });
        }
        return track;
    }

    private static MidiEventRecord? ParseMeta(long tick, int type, ReadOnlySpan<byte> data)
    {
        return type switch
        {
            0x03 => new MidiEventRecord { Tick = tick, Kind = MidiEventKind.TrackName, Text = DecodeText(data) },
            0x06 => new MidiEventRecord { Tick = tick, Kind = MidiEventKind.Marker, Text = DecodeText(data) },
            0x51 when data.Length >= 3 => new MidiEventRecord
            {
                Tick = tick,
                Kind = MidiEventKind.Tempo,
                TempoBpm = 60_000_000d / Math.Max(1, (data[0] << 16) | (data[1] << 8) | data[2])
            },
            0x58 when data.Length >= 2 => new MidiEventRecord
            {
                Tick = tick,
                Kind = MidiEventKind.Meter,
                Data1 = data[0],
                Data2 = 1 << Math.Min(7, (int)data[1])
            },
            _ => null
        };
    }

    private static string DecodeText(ReadOnlySpan<byte> data)
    {
        try { return Encoding.UTF8.GetString(data); }
        catch { return Encoding.Latin1.GetString(data); }
    }

    private static int ReadData(byte[] payload, ref int position)
    {
        if (position >= payload.Length) throw new EndOfStreamException();
        return payload[position++];
    }

    private static uint ReadVariableLength(byte[] payload, ref int position)
    {
        uint value = 0;
        int count = 0;
        while (position < payload.Length)
        {
            byte current = payload[position++];
            value = checked((value << 7) | (uint)(current & 0x7F));
            if ((current & 0x80) == 0) return value;
            if (++count >= 4) throw new InvalidDataException("A MIDI variable-length number is too long.");
        }
        throw new EndOfStreamException();
    }

    private static string ReadAscii(Stream stream, int length)
    {
        byte[] buffer = new byte[length];
        stream.ReadExactly(buffer);
        return Encoding.ASCII.GetString(buffer);
    }
    private static ushort ReadUInt16BigEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[2];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }
    private static uint ReadUInt32BigEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }
}
