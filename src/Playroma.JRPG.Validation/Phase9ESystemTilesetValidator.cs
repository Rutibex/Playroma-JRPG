using System.Globalization;
using System.Text.Json;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Validation;

public sealed class Phase9ESystemTilesetValidator : IProjectValidator
{
    public string Name => "System, Types, Terms and Tilesets";

    public async ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default)
    {
        List<ProjectValidationDiagnostic> diagnostics = [];
        foreach (ProjectDocumentHandle handle in context.Workspace.Documents.Where(document => document.Kind is MvDocumentKind.System or MvDocumentKind.Tilesets))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMutableProjectDocument document = await context.Workspace.OpenDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> bytes = await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(Utf8JsonInput.ToBomlessArray(bytes.Span));
            if (handle.Kind == MvDocumentKind.System) ValidateSystem(json.RootElement, handle.RelativePath, context, diagnostics);
            else ValidateTilesets(json.RootElement, handle.RelativePath, context, diagnostics);
        }
        return diagnostics;
    }

    private static void ValidateTilesets(JsonElement root, string relativePath, ProjectValidationContext context, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidTilesetRecord, DiagnosticSeverity.Error, "Tilesets.json is not an array.", relativePath, "/");
            return;
        }
        foreach ((int index, JsonElement record) in root.EnumerateIndexedArray())
        {
            if (index == 0 || record.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (record.ValueKind != JsonValueKind.Object)
            {
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidTilesetRecord, DiagnosticSeverity.Error, $"Tileset slot {index} is not an object.", relativePath, $"/{index}", index);
                continue;
            }
            int id = ReadInt(record, "id", index);
            int mode = ReadInt(record, "mode", -1);
            if (mode is not 0 and not 1) Add(diagnostics, DiagnosticCodes.Phase9EInvalidTilesetMode, DiagnosticSeverity.Error, $"Tileset {id} has mode {mode}; MV supports World (0) and Area (1).", relativePath, $"/{index}/mode", id);
            if (!record.TryGetProperty("tilesetNames", out JsonElement names) || names.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidTilesetImages, DiagnosticSeverity.Error, $"Tileset {id} has no valid tilesetNames array.", relativePath, $"/{index}/tilesetNames", id);
            }
            else
            {
                if (names.GetArrayLength() != 9) Add(diagnostics, DiagnosticCodes.Phase9EInvalidTilesetImages, DiagnosticSeverity.Warning, $"Tileset {id} stores {names.GetArrayLength()} image slots; standard MV uses nine. Extra or missing values are preserved.", relativePath, $"/{index}/tilesetNames", id);
                foreach ((int sheet, JsonElement nameElement) in names.EnumerateIndexedArray())
                {
                    if (sheet >= 9 || nameElement.ValueKind == JsonValueKind.Null) continue;
                    if (nameElement.ValueKind != JsonValueKind.String)
                    {
                        Add(diagnostics, DiagnosticCodes.Phase9EInvalidTilesetImages, DiagnosticSeverity.Error, $"Tileset {id} image slot {sheet} is not a string.", relativePath, $"/{index}/tilesetNames/{sheet}", id);
                        continue;
                    }
                    string name = nameElement.GetString() ?? string.Empty;
                    if (name.Length > 0 && !context.Catalog.Exists(new ReferenceTarget(ReferenceTargetKind.Asset, Name: name, AssetCategory: AssetCategory.Tilesets)))
                        Add(diagnostics, DiagnosticCodes.Phase9EMissingTilesetImage, DiagnosticSeverity.Error, $"Tileset {id} refers to missing tileset image '{name}'.", relativePath, $"/{index}/tilesetNames/{sheet}", id);
                }
            }
            if (!record.TryGetProperty("flags", out JsonElement flags) || flags.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidTilesetFlags, DiagnosticSeverity.Error, $"Tileset {id} has no valid flags array.", relativePath, $"/{index}/flags", id);
                continue;
            }
            int count = flags.GetArrayLength();
            if (count < 8192) Add(diagnostics, DiagnosticCodes.Phase9ETilesetFlagArrayShort, DiagnosticSeverity.Error, $"Tileset {id} has {count} flags; standard MV tile IDs require 8192.", relativePath, $"/{index}/flags", id);
            else if (count > 8192) Add(diagnostics, DiagnosticCodes.Phase9ETilesetFlagArrayExtended, DiagnosticSeverity.Warning, $"Tileset {id} has {count} flags. The {count - 8192} extension values are preserved.", relativePath, $"/{index}/flags", id);
            int inspected = Math.Min(count, 8192);
            int[] values = new int[inspected];
            for (int flagIndex = 0; flagIndex < inspected; flagIndex++)
            {
                if (!flags[flagIndex].TryGetInt32(out values[flagIndex]))
                    Add(diagnostics, DiagnosticCodes.Phase9EInvalidTilesetFlags, DiagnosticSeverity.Error, $"Tileset {id} flag {flagIndex} is not a 32-bit integer.", relativePath, $"/{index}/flags/{flagIndex}", id);
            }
            int shapeSpecificKinds = 0;
            for (int first = 2048; first + 47 < inspected; first += 48)
            {
                int baseline = values[first];
                if (Enumerable.Range(first + 1, 47).Any(flagIndex => values[flagIndex] != baseline)) shapeSpecificKinds++;
            }
            if (shapeSpecificKinds > 0)
                Add(diagnostics, DiagnosticCodes.Phase9EAutotileFlagsDiffer, DiagnosticSeverity.Information, $"Tileset {id} contains {shapeSpecificKinds} autotile kind(s) with shape-specific flags. This is valid MV data and is preserved; logical passage painting retains wall-top edge masks.", relativePath, $"/{index}/flags", id);
        }
    }

    private static void ValidateSystem(JsonElement root, string relativePath, ProjectValidationContext context, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystem, DiagnosticSeverity.Error, "System.json is not an object.", relativePath, "/");
            return;
        }
        ValidatePosition(root, "startMapId", "startX", "startY", "Player", relativePath, context, diagnostics);
        foreach (string vehicle in new[] { "boat", "ship", "airship" })
        {
            if (!root.TryGetProperty(vehicle, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            {
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidVehicle, DiagnosticSeverity.Error, $"System {vehicle} definition is missing or invalid.", relativePath, "/" + vehicle);
                continue;
            }
            ValidatePosition(value, "startMapId", "startX", "startY", CultureInfo.InvariantCulture.TextInfo.ToTitleCase(vehicle), relativePath, context, diagnostics, "/" + vehicle);
            ValidateAsset(value, "characterName", AssetCategory.Characters, relativePath, "/" + vehicle, context, diagnostics, DiagnosticCodes.Phase9EMissingSystemAsset);
            ValidateAudio(value, "bgm", AssetCategory.BackgroundMusic, relativePath, "/" + vehicle, context, diagnostics);
        }
        ValidateAsset(root, "title1Name", AssetCategory.Titles1, relativePath, string.Empty, context, diagnostics, DiagnosticCodes.Phase9EMissingSystemAsset);
        ValidateAsset(root, "title2Name", AssetCategory.Titles2, relativePath, string.Empty, context, diagnostics, DiagnosticCodes.Phase9EMissingSystemAsset);
        ValidateAsset(root, "battleback1Name", AssetCategory.Battlebacks1, relativePath, string.Empty, context, diagnostics, DiagnosticCodes.Phase9EMissingSystemAsset);
        ValidateAsset(root, "battleback2Name", AssetCategory.Battlebacks2, relativePath, string.Empty, context, diagnostics, DiagnosticCodes.Phase9EMissingSystemAsset);
        ValidateAsset(root, "battlerName", AssetCategory.SideViewEnemies, relativePath, string.Empty, context, diagnostics, DiagnosticCodes.Phase9EMissingSystemAsset);
        ValidateAudio(root, "titleBgm", AssetCategory.BackgroundMusic, relativePath, string.Empty, context, diagnostics);
        ValidateAudio(root, "battleBgm", AssetCategory.BackgroundMusic, relativePath, string.Empty, context, diagnostics);
        ValidateAudio(root, "victoryMe", AssetCategory.MusicEffects, relativePath, string.Empty, context, diagnostics);
        ValidateAudio(root, "defeatMe", AssetCategory.MusicEffects, relativePath, string.Empty, context, diagnostics);
        ValidateAudio(root, "gameoverMe", AssetCategory.MusicEffects, relativePath, string.Empty, context, diagnostics);
        if (root.TryGetProperty("sounds", out JsonElement sounds) && sounds.ValueKind == JsonValueKind.Array)
        {
            if (sounds.GetArrayLength() != 24) Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Warning, $"System sounds contains {sounds.GetArrayLength()} rows; standard MV uses 24. Values are preserved.", relativePath, "/sounds");
            foreach ((int index, JsonElement sound) in sounds.EnumerateIndexedArray()) ValidateAudioElement(sound, AssetCategory.SoundEffects, relativePath, $"/sounds/{index}", context, diagnostics);
        }
        else Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, "System sounds is not an array.", relativePath, "/sounds");

        ValidateIdArray(root, "partyMembers", ReferenceTargetKind.Actor, relativePath, context, diagnostics, allowZero: false);
        ValidateIdArray(root, "magicSkills", ReferenceTargetKind.SkillType, relativePath, context, diagnostics, allowZero: false);
        ValidateBooleanArray(root, "menuCommands", 6, relativePath, diagnostics);
        ValidateIntegerArray(root, "windowTone", 4, -255, 255, relativePath, diagnostics);
        ValidateBooleanProperties(root,
        [
            "optSideView", "optTransparent", "optFollowers", "optSlipDeath",
            "optFloorDeath", "optDisplayTp", "optExtraExp", "optDrawTitle"
        ], relativePath, diagnostics);
        int battlerHue = ReadInt(root, "battlerHue", -1);
        if (battlerHue is < 0 or > 360)
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystem, DiagnosticSeverity.Warning, $"System battlerHue {battlerHue} is outside the standard 0–360 range.", relativePath, "/battlerHue");
        ValidateBattleTest(root, relativePath, context, diagnostics);
        ValidateStringArray(root, "elements", relativePath, diagnostics, true);
        ValidateStringArray(root, "skillTypes", relativePath, diagnostics, true);
        ValidateStringArray(root, "weaponTypes", relativePath, diagnostics, true);
        ValidateStringArray(root, "armorTypes", relativePath, diagnostics, true);
        ValidateStringArray(root, "equipTypes", relativePath, diagnostics, true);
        ValidateStringArray(root, "switches", relativePath, diagnostics, true);
        ValidateStringArray(root, "variables", relativePath, diagnostics, true);
        if (root.TryGetProperty("attackMotions", out JsonElement motions) && motions.ValueKind == JsonValueKind.Array)
        {
            int weaponTypeCount = root.TryGetProperty("weaponTypes", out JsonElement weaponTypes) && weaponTypes.ValueKind == JsonValueKind.Array ? Math.Max(0, weaponTypes.GetArrayLength() - 1) : 0;
            if (motions.GetArrayLength() != weaponTypeCount) Add(diagnostics, DiagnosticCodes.Phase9EAttackMotionCount, DiagnosticSeverity.Warning, $"System has {motions.GetArrayLength()} attack motions for {weaponTypeCount} weapon types. Extra rows are preserved; missing rows use runtime defaults.", relativePath, "/attackMotions");
            foreach ((int index, JsonElement motion) in motions.EnumerateIndexedArray())
            {
                int type = ReadInt(motion, "type", -1);
                if (motion.ValueKind != JsonValueKind.Object || type is < 0 or > 2 || ReadInt(motion, "weaponImageId", -1) < 0)
                    Add(diagnostics, DiagnosticCodes.Phase9EInvalidAttackMotion, DiagnosticSeverity.Error, $"Attack motion {index} is invalid.", relativePath, $"/attackMotions/{index}");
            }
        }
        if (!root.TryGetProperty("terms", out JsonElement terms) || terms.ValueKind != JsonValueKind.Object)
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidTerms, DiagnosticSeverity.Error, "System terms is missing or invalid.", relativePath, "/terms");
        else ValidateTerms(terms, relativePath, diagnostics);
    }

    private static void ValidateTerms(JsonElement terms, string relativePath, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        foreach (string arrayName in new[] { "basic", "params", "commands" })
        {
            if (!terms.TryGetProperty(arrayName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidTerms, DiagnosticSeverity.Error, $"Terms {arrayName} is not an array.", relativePath, $"/terms/{arrayName}");
                continue;
            }
            foreach ((int index, JsonElement value) in array.EnumerateIndexedArray())
            {
                if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    Add(diagnostics, DiagnosticCodes.Phase9EInvalidTerms, DiagnosticSeverity.Warning, $"Terms {arrayName} row {index} is neither a string nor null and is preserved as raw data.", relativePath, $"/terms/{arrayName}/{index}");
            }
        }
        if (!terms.TryGetProperty("messages", out JsonElement messages) || messages.ValueKind != JsonValueKind.Object)
        {
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidTerms, DiagnosticSeverity.Error, "Terms messages is not an object.", relativePath, "/terms/messages");
            return;
        }
        foreach (JsonProperty property in messages.EnumerateObject())
        {
            if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidTerms, DiagnosticSeverity.Warning, $"Term message {property.Name} is neither a string nor null and is preserved as raw data.", relativePath, "/terms/messages/" + property.Name);
                continue;
            }
            if (property.Value.ValueKind != JsonValueKind.String || !RequiredMessagePlaceholders.TryGetValue(property.Name, out string[] required)) continue;
            string text = property.Value.GetString() ?? string.Empty;
            foreach (string placeholder in required.Where(value => !text.Contains(value, StringComparison.Ordinal)))
                Add(diagnostics, DiagnosticCodes.Phase9ETermPlaceholderMissing, DiagnosticSeverity.Warning, $"Term message {property.Name} does not contain expected placeholder {placeholder}. This may make runtime text incomplete.", relativePath, "/terms/messages/" + property.Name);
        }
    }

    private static void ValidateBattleTest(JsonElement root, string relativePath, ProjectValidationContext context, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        int troopId = ReadInt(root, "testTroopId");
        if (troopId > 0 && !context.Catalog.Exists(new ReferenceTarget(ReferenceTargetKind.Troop, troopId)))
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemReference, DiagnosticSeverity.Error, $"Battle test refers to missing troop {troopId}.", relativePath, "/testTroopId");
        if (!root.TryGetProperty("testBattlers", out JsonElement battlers) || battlers.ValueKind != JsonValueKind.Array)
        {
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, "System testBattlers is not an array.", relativePath, "/testBattlers");
            return;
        }
        foreach ((int index, JsonElement battler) in battlers.EnumerateIndexedArray())
        {
            string path = $"/testBattlers/{index}";
            if (battler.ValueKind != JsonValueKind.Object)
            {
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, $"Battle-test actor row {index} is not an object.", relativePath, path);
                continue;
            }
            int actorId = ReadInt(battler, "actorId");
            if (actorId < 0 || actorId > 0 && !context.Catalog.Exists(new ReferenceTarget(ReferenceTargetKind.Actor, actorId)))
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemReference, DiagnosticSeverity.Error, $"Battle-test row {index} refers to missing actor {actorId}.", relativePath, path + "/actorId");
            else if (actorId == 0)
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemReference, DiagnosticSeverity.Warning, $"Battle-test row {index} uses actor ID 0. It is preserved but is not a standard populated actor.", relativePath, path + "/actorId");
            int level = ReadInt(battler, "level", -1);
            if (level is < 1 or > 99)
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystem, DiagnosticSeverity.Warning, $"Battle-test row {index} level {level} is outside the standard 1–99 range.", relativePath, path + "/level");
            if (!battler.TryGetProperty("equips", out JsonElement equips) || equips.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, $"Battle-test row {index} equips is not an array.", relativePath, path + "/equips");
                continue;
            }
            foreach ((int equipIndex, JsonElement value) in equips.EnumerateIndexedArray())
            {
                if (!value.TryGetInt32(out int equipmentId))
                {
                    Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, $"Battle-test equipment row {index}:{equipIndex} is not an integer.", relativePath, path + $"/equips/{equipIndex}");
                    continue;
                }
                if (equipmentId <= 0) continue;
                ReferenceTargetKind kind = equipIndex == 0 ? ReferenceTargetKind.Weapon : ReferenceTargetKind.Armor;
                if (!context.Catalog.Exists(new ReferenceTarget(kind, equipmentId)))
                    Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemReference, DiagnosticSeverity.Error, $"Battle-test row {index} refers to missing {kind} {equipmentId} in equipment slot {equipIndex + 1}.", relativePath, path + $"/equips/{equipIndex}");
            }
        }
    }

    private static void ValidateBooleanArray(JsonElement root, string property, int standardLength, string relativePath, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, $"System {property} is not an array.", relativePath, "/" + property);
            return;
        }
        if (array.GetArrayLength() != standardLength)
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Warning, $"System {property} contains {array.GetArrayLength()} rows; standard MV uses {standardLength}. Values are preserved.", relativePath, "/" + property);
        foreach ((int index, JsonElement value) in array.EnumerateIndexedArray())
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, $"System {property} row {index} is not Boolean.", relativePath, $"/{property}/{index}");
    }

    private static void ValidateIntegerArray(JsonElement root, string property, int standardLength, int minimum, int maximum, string relativePath, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, $"System {property} is not an array.", relativePath, "/" + property);
            return;
        }
        if (array.GetArrayLength() != standardLength)
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Warning, $"System {property} contains {array.GetArrayLength()} rows; standard MV uses {standardLength}. Values are preserved.", relativePath, "/" + property);
        foreach ((int index, JsonElement value) in array.EnumerateIndexedArray())
        {
            if (!value.TryGetInt32(out int number) || number < minimum || number > maximum)
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, $"System {property} row {index} is not an integer in the range {minimum}–{maximum}.", relativePath, $"/{property}/{index}");
        }
    }

    private static void ValidateBooleanProperties(JsonElement root, IEnumerable<string> properties, string relativePath, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        foreach (string property in properties)
            if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystem, DiagnosticSeverity.Error, $"System {property} is not Boolean.", relativePath, "/" + property);
    }

    private static readonly IReadOnlyDictionary<string, string[]> RequiredMessagePlaceholders = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["partyName"] = ["%1"], ["emerge"] = ["%1"], ["preemptive"] = ["%1"], ["surprise"] = ["%1"],
        ["escapeStart"] = ["%1"], ["victory"] = ["%1"], ["defeat"] = ["%1"], ["obtainExp"] = ["%1", "%2"],
        ["obtainGold"] = ["%1"], ["obtainItem"] = ["%1"], ["levelUp"] = ["%1", "%2", "%3"], ["obtainSkill"] = ["%1"],
        ["useItem"] = ["%1", "%2"], ["actorDamage"] = ["%1", "%2"], ["actorRecovery"] = ["%1", "%2", "%3"],
        ["actorGain"] = ["%1", "%2", "%3"], ["actorLoss"] = ["%1", "%2", "%3"], ["actorDrain"] = ["%1", "%2", "%3"],
        ["actorNoDamage"] = ["%1"], ["actorNoHit"] = ["%1"], ["enemyDamage"] = ["%1", "%2"],
        ["enemyRecovery"] = ["%1", "%2", "%3"], ["enemyGain"] = ["%1", "%2", "%3"], ["enemyLoss"] = ["%1", "%2", "%3"],
        ["enemyDrain"] = ["%1", "%2", "%3"], ["enemyNoDamage"] = ["%1"], ["enemyNoHit"] = ["%1"],
        ["evasion"] = ["%1"], ["magicEvasion"] = ["%1"], ["magicReflection"] = ["%1"], ["counterAttack"] = ["%1"],
        ["substitute"] = ["%1", "%2"], ["buffAdd"] = ["%1", "%2"], ["debuffAdd"] = ["%1", "%2"],
        ["buffRemove"] = ["%1", "%2"], ["actionFailure"] = ["%1"]
    };

    private static void ValidatePosition(JsonElement root, string mapName, string xName, string yName, string label, string relativePath, ProjectValidationContext context, ICollection<ProjectValidationDiagnostic> diagnostics, string prefix = "")
    {
        int mapId = ReadInt(root, mapName);
        int x = ReadInt(root, xName, -1);
        int y = ReadInt(root, yName, -1);
        if (mapId == 0 && label != "Player") return;
        if (!context.Catalog.TryGetMapDimensions(mapId, out MapDimensions dimensions))
        {
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidStartingPosition, DiagnosticSeverity.Error, $"{label} starts on missing map {mapId}.", relativePath, $"{prefix}/{mapName}");
            return;
        }
        if (x < 0 || y < 0 || x >= dimensions.Width || y >= dimensions.Height)
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidStartingPosition, DiagnosticSeverity.Error, $"{label} start ({x},{y}) is outside map {mapId} ({dimensions.Width}×{dimensions.Height}).", relativePath, $"{prefix}/{xName}");
    }

    private static void ValidateIdArray(JsonElement root, string property, ReferenceTargetKind kind, string relativePath, ProjectValidationContext context, ICollection<ProjectValidationDiagnostic> diagnostics, bool allowZero)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, $"System {property} is not an array.", relativePath, "/" + property);
            return;
        }
        foreach ((int index, JsonElement value) in array.EnumerateIndexedArray())
        {
            if (!value.TryGetInt32(out int id)) { Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, $"System {property} row {index} is not an integer.", relativePath, $"/{property}/{index}"); continue; }
            if (id == 0 && allowZero) continue;
            if (id <= 0 || !context.Catalog.Exists(new ReferenceTarget(kind, id))) Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemReference, DiagnosticSeverity.Error, $"System {property} row {index} refers to missing {kind} {id}.", relativePath, $"/{property}/{index}");
        }
    }

    private static void ValidateStringArray(JsonElement root, string property, string relativePath, ICollection<ProjectValidationDiagnostic> diagnostics, bool requireReservedZero)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Error, $"System {property} is not an array.", relativePath, "/" + property);
            return;
        }
        if (requireReservedZero && array.GetArrayLength() > 0 && array[0].ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidSystemArray, DiagnosticSeverity.Warning, $"System {property} index zero has an unusual type and is preserved.", relativePath, $"/{property}/0");
    }

    private static void ValidateAudio(JsonElement root, string property, AssetCategory category, string relativePath, string prefix, ProjectValidationContext context, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty(property, out JsonElement value)) return;
        ValidateAudioElement(value, category, relativePath, $"{prefix}/{property}", context, diagnostics);
    }

    private static void ValidateAudioElement(JsonElement value, AssetCategory category, string relativePath, string path, ProjectValidationContext context, ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidAudioSetting, DiagnosticSeverity.Error, $"Audio setting {path} is not an object.", relativePath, path);
            return;
        }
        string name = value.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() ?? string.Empty : string.Empty;
        int volume = ReadInt(value, "volume", -1);
        int pitch = ReadInt(value, "pitch", -1);
        int pan = ReadInt(value, "pan", int.MinValue);
        if (volume is < 0 or > 100 || pitch is < 50 or > 150 || pan is < -100 or > 100)
            Add(diagnostics, DiagnosticCodes.Phase9EInvalidAudioSetting, DiagnosticSeverity.Error, $"Audio setting {path} has volume, pitch, or pan outside MV ranges.", relativePath, path);
        if (name.Length > 0 && !context.Catalog.Exists(new ReferenceTarget(ReferenceTargetKind.Asset, Name: name, AssetCategory: category)))
            Add(diagnostics, DiagnosticCodes.Phase9EMissingSystemAsset, DiagnosticSeverity.Error, $"Audio setting {path} refers to missing asset '{name}'.", relativePath, path + "/name");
    }

    private static void ValidateAsset(JsonElement root, string property, AssetCategory category, string relativePath, string prefix, ProjectValidationContext context, ICollection<ProjectValidationDiagnostic> diagnostics, string code)
    {
        if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String) return;
        string name = value.GetString() ?? string.Empty;
        if (name.Length > 0 && !context.Catalog.Exists(new ReferenceTarget(ReferenceTargetKind.Asset, Name: name, AssetCategory: category)))
            Add(diagnostics, code, DiagnosticSeverity.Error, $"System {property} refers to missing asset '{name}'.", relativePath, $"{prefix}/{property}");
    }

    private static int ReadInt(JsonElement root, string property, int fallback = 0) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int parsed) ? parsed : fallback;
    private static void Add(ICollection<ProjectValidationDiagnostic> diagnostics, string code, DiagnosticSeverity severity, string message, string relativePath, string jsonPath, int? recordId = null)
    {
        ProjectDiagnostic diagnostic = new(code, severity, message, new ProjectLocation(relativePath, jsonPath, RecordId: recordId));
        diagnostics.Add(new ProjectValidationDiagnostic(diagnostic, DocumentationKey: "phase9e/system-tilesets"));
    }
}
