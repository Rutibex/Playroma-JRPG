using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.Randomness;

namespace PlayromaMusic.Generators.Orchestration;

public static class PresetAuditionPhraseFactory
{
    public static SongClip Create(
        ulong masterSeed,
        InstrumentPresetDescriptor preset,
        PresetAuditionPhraseKind phrase,
        int ticksPerQuarterNote = 960)
    {
        ArgumentNullException.ThrowIfNull(preset);
        int tpq = Math.Clamp(ticksPerQuarterNote, 24, 9600);
        SongClip clip = new()
        {
            Id = SeedDerivation.StableId("preset-audition", masterSeed, preset.Key, phrase.ToString()),
            Name = $"{preset.Name} - {phrase}",
            StartTick = 0,
            LengthTicks = tpq * 8L
        };
        clip.ProgramChanges.Add(new ProgramChangeEvent
        {
            Id = SeedDerivation.StableId("preset-audition-program", masterSeed, preset.Key),
            Bank = preset.Bank,
            Program = preset.Program,
            StartTick = 0,
            PresetKey = preset.Key,
            PresetName = preset.Name
        });

        if (preset.IsDrumKit || phrase == PresetAuditionPhraseKind.DrumPattern)
        {
            AddDrumPattern(clip, masterSeed, tpq);
            return clip;
        }

        int low = Math.Clamp(preset.SuggestedLowNote, 24, 84);
        int high = Math.Clamp(preset.SuggestedHighNote, low + 1, 108);
        int middle = Math.Clamp((low + high) / 2, 48, 84);
        switch (phrase)
        {
            case PresetAuditionPhraseKind.ChromaticRange:
                for (int index = 0; index < 16; index++) Add(clip, masterSeed, index, Math.Clamp(middle - 6 + index, low, high), index * tpq / 2L, tpq * 3L / 8, 88);
                break;
            case PresetAuditionPhraseKind.Chords:
                int[][] chords = { new[] { 0, 4, 7 }, new[] { 5, 9, 12 }, new[] { 7, 11, 14 }, new[] { 0, 4, 7, 11 } };
                for (int chord = 0; chord < chords.Length; chord++)
                    for (int tone = 0; tone < chords[chord].Length; tone++) Add(clip, masterSeed, chord * 8 + tone, Math.Clamp(middle - 12 + chords[chord][tone], low, high), chord * tpq * 2L, tpq * 3L / 2, 82 + tone * 4);
                break;
            case PresetAuditionPhraseKind.BassLine:
                int[] bass = { 0, 0, 7, 10, 12, 10, 7, 5, 3, 5, 7, 10, 12, 7, 5, 0 };
                int bassRoot = Math.Clamp(low + 7, 28, 52);
                for (int index = 0; index < bass.Length; index++) Add(clip, masterSeed, index, Math.Clamp(bassRoot + bass[index], low, high), index * tpq / 2L, tpq * 7L / 16, index % 4 == 0 ? 105 : 84);
                break;
            case PresetAuditionPhraseKind.LeadMelody:
                int[] melody = { 0, 2, 4, 7, 9, 7, 4, 2, 0, -3, 0, 4, 7, 5, 2, 0 };
                for (int index = 0; index < melody.Length; index++) Add(clip, masterSeed, index, Math.Clamp(middle + melody[index], low, high), index * tpq / 2L, tpq * 3L / 8, 78 + index % 4 * 7);
                break;
            case PresetAuditionPhraseKind.StaccatoPulse:
                int[] pulse = { 0, 7, 12, 7, 3, 10, 15, 10 };
                for (int index = 0; index < 32; index++) Add(clip, masterSeed, index, Math.Clamp(middle - 12 + pulse[index % pulse.Length], low, high), index * tpq / 4L, tpq / 8L, index % 4 == 0 ? 108 : 72);
                break;
            case PresetAuditionPhraseKind.SustainedPad:
                int[][] pads = { new[] { 0, 4, 7, 11 }, new[] { -2, 2, 5, 9 } };
                for (int chord = 0; chord < pads.Length; chord++)
                    for (int tone = 0; tone < pads[chord].Length; tone++) Add(clip, masterSeed, chord * 8 + tone, Math.Clamp(middle - 12 + pads[chord][tone], low, high), chord * tpq * 4L, tpq * 15L / 4, 72 + tone * 3);
                break;
            default:
                Add(clip, masterSeed, 0, middle, 0, tpq * 2L, 92);
                break;
        }
        return clip;
    }

    private static void AddDrumPattern(SongClip clip, ulong seed, int tpq)
    {
        int index = 0;
        for (int step = 0; step < 32; step++)
        {
            long tick = step * tpq / 4L;
            if (step % 4 == 0) Add(clip, seed, index++, 36, tick, tpq / 8L, 108);
            if (step % 8 == 4) Add(clip, seed, index++, 38, tick, tpq / 8L, 104);
            Add(clip, seed, index++, step % 8 == 7 ? 46 : 42, tick, tpq / 10L, step % 4 == 0 ? 86 : 64);
            if (step == 28) Add(clip, seed, index++, 49, tick, tpq / 4L, 112);
        }
    }

    private static void Add(SongClip clip, ulong seed, int index, int pitch, long start, long duration, int velocity)
    {
        clip.Notes.Add(new NoteEvent
        {
            Id = SeedDerivation.StableId("preset-audition-note", seed, clip.Id, index.ToString()),
            Pitch = Math.Clamp(pitch, 0, 127),
            StartTick = Math.Max(0, start),
            DurationTicks = Math.Max(1, duration),
            Velocity = Math.Clamp(velocity, 1, 127)
        });
    }
}
