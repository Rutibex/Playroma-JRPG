namespace Playroma.JRPG.Editor.WinForms.Music;

internal enum PlaygroundMood
{
    Peaceful,
    Cheerful,
    Mysterious,
    Dark,
    Heroic,
    Sad,
    Magical,
    Romantic,
    Playful,
    Urgent
}

internal enum PlaygroundSoundWorld
{
    ClassicJrpg,
    EightBit,
    SixteenBit,
    Orchestral,
    Piano,
    MusicBox,
    Rock,
    Electronic,
    Ancient,
    Folk,
    Haunted,
    Dreamlike
}

internal enum PlaygroundLength
{
    Jingle,
    ShortScene,
    NormalLoop,
    LongTheme
}

internal enum MusicSceneCategory
{
    Battle,
    Place,
    Story,
    Travel,
    Celebration,
    Danger
}

internal sealed record MusicScenePreset(
    string Id,
    string Name,
    string Glyph,
    string Description,
    string BaseStyleId,
    MusicSceneCategory Category,
    PlaygroundMood DefaultMood,
    int DefaultEnergy);

internal static class MusicPlaygroundCatalog
{
    public static IReadOnlyList<MusicScenePreset> Scenes { get; } =
    [
        new("battle", "Battle", "⚔", "A lively encounter with monsters or rivals.", "jrpg-battle-heroic", MusicSceneCategory.Battle, PlaygroundMood.Heroic, 3),
        new("boss", "Boss", "♛", "A dangerous enemy with a larger-than-life theme.", "jrpg-boss-ominous", MusicSceneCategory.Battle, PlaygroundMood.Dark, 4),
        new("final-boss", "Final Boss", "✹", "A huge, dramatic final confrontation.", "jrpg-boss-final-transcendent", MusicSceneCategory.Battle, PlaygroundMood.Urgent, 4),
        new("town", "Peaceful Town", "⌂", "A welcoming settlement where the player can relax.", "jrpg-town-pastoral", MusicSceneCategory.Place, PlaygroundMood.Peaceful, 1),
        new("busy-town", "Busy Town", "♨", "Markets, crowds, shops, and cheerful activity.", "jrpg-town-bustling", MusicSceneCategory.Place, PlaygroundMood.Cheerful, 2),
        new("forest", "Forest", "♣", "An enchanted woodland filled with discovery.", "jrpg-forest", MusicSceneCategory.Place, PlaygroundMood.Magical, 2),
        new("dungeon", "Dungeon", "▦", "Exploration beneath the world, tense but adventurous.", "jrpg-occult-dungeon", MusicSceneCategory.Danger, PlaygroundMood.Mysterious, 2),
        new("ruins", "Ancient Ruins", "⌛", "Forgotten halls, old civilizations, and hidden knowledge.", "jrpg-ancient-ruins", MusicSceneCategory.Place, PlaygroundMood.Mysterious, 2),
        new("snow", "Snow and Ice", "❄", "A cold, beautiful journey across frozen lands.", "jrpg-ice", MusicSceneCategory.Place, PlaygroundMood.Magical, 2),
        new("desert", "Desert", "☀", "A wide, mysterious journey through heat and sand.", "jrpg-desert", MusicSceneCategory.Place, PlaygroundMood.Mysterious, 2),
        new("travel", "World Journey", "➤", "Travel across the overworld toward adventure.", "jrpg-overworld-heroic", MusicSceneCategory.Travel, PlaygroundMood.Heroic, 3),
        new("airship", "Airship", "✈", "A soaring voyage above the world.", "jrpg-airship", MusicSceneCategory.Travel, PlaygroundMood.Heroic, 3),
        new("mystery", "Mystery", "?", "Secrets, uncertainty, and clues waiting to be found.", "jrpg-suspense", MusicSceneCategory.Story, PlaygroundMood.Mysterious, 2),
        new("sad", "Sad Moment", "☂", "Loss, farewell, or a quiet emotional scene.", "jrpg-sorrow", MusicSceneCategory.Story, PlaygroundMood.Sad, 1),
        new("romance", "Romance", "♥", "Warm, intimate music for characters growing closer.", "jrpg-romance", MusicSceneCategory.Story, PlaygroundMood.Romantic, 1),
        new("revelation", "Big Revelation", "✦", "A truth is uncovered and everything changes.", "jrpg-revelation", MusicSceneCategory.Story, PlaygroundMood.Magical, 3),
        new("victory", "Victory", "★", "A short reward after success.", "jrpg-victory-fanfare", MusicSceneCategory.Celebration, PlaygroundMood.Cheerful, 4),
        new("defeat", "Defeat", "×", "A brief musical moment for failure or loss.", "jrpg-defeat-lament", MusicSceneCategory.Story, PlaygroundMood.Sad, 1),
        new("silly", "Comic Scene", "☺", "Playful music for jokes, oddballs, and harmless trouble.", "jrpg-comic-relief", MusicSceneCategory.Celebration, PlaygroundMood.Playful, 2),
        new("title", "Title Screen", "◆", "A grand introduction to the game and its world.", "game-adventure-title", MusicSceneCategory.Story, PlaygroundMood.Heroic, 3),
        new("ending", "Ending Credits", "∞", "A reflective final theme for the journey's end.", "jrpg-ending-credits", MusicSceneCategory.Story, PlaygroundMood.Magical, 2)
    ];


    public static string FriendlyName(PlaygroundMood value) => value switch
    {
        PlaygroundMood.Peaceful => "Peaceful",
        PlaygroundMood.Cheerful => "Cheerful",
        PlaygroundMood.Mysterious => "Mysterious",
        PlaygroundMood.Dark => "Dark and Sinister",
        PlaygroundMood.Heroic => "Heroic",
        PlaygroundMood.Sad => "Sad and Emotional",
        PlaygroundMood.Magical => "Magical",
        PlaygroundMood.Romantic => "Romantic",
        PlaygroundMood.Playful => "Playful and Silly",
        PlaygroundMood.Urgent => "Urgent",
        _ => value.ToString()
    };

    public static string FriendlyName(PlaygroundSoundWorld value) => value switch
    {
        PlaygroundSoundWorld.ClassicJrpg => "Classic JRPG",
        PlaygroundSoundWorld.EightBit => "8-Bit Console",
        PlaygroundSoundWorld.SixteenBit => "16-Bit Console",
        PlaygroundSoundWorld.Orchestral => "Orchestral",
        PlaygroundSoundWorld.Piano => "Piano",
        PlaygroundSoundWorld.MusicBox => "Music Box",
        PlaygroundSoundWorld.Rock => "Rock Band",
        PlaygroundSoundWorld.Electronic => "Electronic",
        PlaygroundSoundWorld.Ancient => "Ancient Instruments",
        PlaygroundSoundWorld.Folk => "Folk Ensemble",
        PlaygroundSoundWorld.Haunted => "Haunted",
        PlaygroundSoundWorld.Dreamlike => "Dreamlike",
        _ => value.ToString()
    };

    public static string FriendlyName(PlaygroundLength value) => value switch
    {
        PlaygroundLength.Jingle => "Jingle (very short)",
        PlaygroundLength.ShortScene => "Short Scene",
        PlaygroundLength.NormalLoop => "Normal Game Loop",
        PlaygroundLength.LongTheme => "Long Theme",
        _ => value.ToString()
    };

    public static IReadOnlyList<string> CandidateStyles(
        MusicScenePreset scene,
        PlaygroundMood mood,
        PlaygroundSoundWorld soundWorld)
    {
        List<string> candidates = [];

        switch (soundWorld)
        {
            case PlaygroundSoundWorld.EightBit:
                candidates.Add(scene.Category == MusicSceneCategory.Battle ? "retro-8bit-boss" :
                    scene.Id == "dungeon" ? "retro-8bit-dungeon" : "retro-8bit-adventure");
                break;
            case PlaygroundSoundWorld.SixteenBit:
                candidates.Add(scene.Category == MusicSceneCategory.Battle ? "retro-16bit-action" : "retro-16bit-metroidvania");
                break;
            case PlaygroundSoundWorld.Rock:
                candidates.Add(scene.Category == MusicSceneCategory.Battle ? "arena-rock" : "indie-folk");
                break;
            case PlaygroundSoundWorld.Electronic:
                candidates.Add(scene.Category == MusicSceneCategory.Battle ? "drum-and-bass" : "synthwave-night");
                break;
            case PlaygroundSoundWorld.Piano:
                candidates.Add(mood == PlaygroundMood.Sad ? "jrpg-sorrow" : "neo-classical-drift");
                break;
            case PlaygroundSoundWorld.MusicBox:
                candidates.Add(mood == PlaygroundMood.Dark ? "dark-ambient" : "neo-classical-drift");
                break;
            case PlaygroundSoundWorld.Ancient:
                candidates.Add(scene.Id == "town" || scene.Id == "busy-town" ? "jrpg-tavern" : "jrpg-ancient-ruins");
                break;
            case PlaygroundSoundWorld.Folk:
                candidates.Add(scene.Id == "town" || scene.Id == "busy-town" ? "jrpg-town-pastoral" : "indie-folk");
                break;
            case PlaygroundSoundWorld.Haunted:
                candidates.Add(scene.Category == MusicSceneCategory.Battle ? "jrpg-boss-ominous" : "dark-ambient");
                candidates.Add("jrpg-occult-dungeon");
                break;
            case PlaygroundSoundWorld.Dreamlike:
                candidates.Add("dream-pop");
                candidates.Add("ambient-drifting-light");
                break;
            case PlaygroundSoundWorld.Orchestral:
            case PlaygroundSoundWorld.ClassicJrpg:
                break;
        }

        candidates.Add(mood switch
        {
            PlaygroundMood.Peaceful => scene.Category == MusicSceneCategory.Place ? "jrpg-town-pastoral" : "ambient-drifting-light",
            PlaygroundMood.Cheerful => scene.Category == MusicSceneCategory.Battle ? "jrpg-battle-heroic" : "jrpg-town-bustling",
            PlaygroundMood.Mysterious => scene.Id == "ruins" ? "jrpg-ancient-ruins" : "jrpg-suspense",
            PlaygroundMood.Dark => scene.Category == MusicSceneCategory.Battle ? "jrpg-boss-ominous" : "jrpg-occult-dungeon",
            PlaygroundMood.Heroic => scene.Category == MusicSceneCategory.Battle ? "jrpg-battle-heroic" : "jrpg-overworld-heroic",
            PlaygroundMood.Sad => "jrpg-sorrow",
            PlaygroundMood.Magical => scene.Id == "forest" ? "jrpg-forest" : "jrpg-revelation",
            PlaygroundMood.Romantic => "jrpg-romance",
            PlaygroundMood.Playful => "jrpg-comic-relief",
            PlaygroundMood.Urgent => scene.Category == MusicSceneCategory.Battle ? "jrpg-battle-desperate" : "jrpg-suspense",
            _ => scene.BaseStyleId
        });
        candidates.Add(scene.BaseStyleId);
        candidates.Add("jrpg-foundation");
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
