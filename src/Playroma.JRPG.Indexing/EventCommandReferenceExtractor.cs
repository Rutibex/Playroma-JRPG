using System.Globalization;
using System.Text.Json;
using Playroma.JRPG.Assets;

namespace Playroma.JRPG.Indexing;

internal static class EventCommandReferenceExtractor
{
    public static void Scan(
        int code,
        JsonElement parameters,
        MvReferenceExtractor.EventCommandContext context)
    {
        switch (code)
        {
            case 101: // Show Text
                context.AddAsset(0, GetString(parameters, 0), AssetCategory.Faces);
                break;
            case 111:
                ScanConditionalBranch(parameters, context);
                break;
            case 117:
                context.AddId(0, ReferenceTargetKind.CommonEvent);
                break;
            case 121:
                AddRange(parameters, 0, 1, ReferenceTargetKind.Switch, context);
                break;
            case 122:
                ScanControlVariables(parameters, context);
                break;
            case 126:
                context.AddId(0, ReferenceTargetKind.Item);
                break;
            case 127:
                context.AddId(0, ReferenceTargetKind.Weapon);
                break;
            case 128:
                context.AddId(0, ReferenceTargetKind.Armor);
                break;
            case 129:
                context.AddId(0, ReferenceTargetKind.Actor);
                break;
            case 201:
                ScanTransferPlayer(parameters, context);
                break;
            case 202:
                ScanSetVehicleLocation(parameters, context);
                break;
            case 203:
                ScanSetEventLocation(parameters, context);
                break;
            case 205:
                ScanSetMovementRoute(parameters, context);
                break;
            case 212:
                context.AddId(1, ReferenceTargetKind.Animation);
                break;
            case 231:
                context.AddAsset(1, GetString(parameters, 1), AssetCategory.Pictures);
                ScanCoordinateVariables(parameters, 3, 4, 5, context);
                break;
            case 232:
                ScanCoordinateVariables(parameters, 3, 4, 5, context);
                break;
            case 241:
                AddAudioObject(parameters, 0, AssetCategory.BackgroundMusic, context);
                break;
            case 245:
                AddAudioObject(parameters, 0, AssetCategory.BackgroundSounds, context);
                break;
            case 249:
                AddAudioObject(parameters, 0, AssetCategory.MusicEffects, context);
                break;
            case 250:
                AddAudioObject(parameters, 0, AssetCategory.SoundEffects, context);
                break;
            case 261:
                context.AddAsset(0, GetString(parameters, 0), AssetCategory.Movies);
                break;
            case 282:
                context.AddId(0, ReferenceTargetKind.Tileset);
                break;
            case 283:
                context.AddAsset(0, GetString(parameters, 0), AssetCategory.Battlebacks1);
                context.AddAsset(1, GetString(parameters, 1), AssetCategory.Battlebacks2);
                break;
            case 284:
                context.AddAsset(0, GetString(parameters, 0), AssetCategory.Parallaxes);
                break;
            case 285:
                ScanGetLocationInfo(parameters, context);
                break;
            case 301:
                ScanBattleProcessing(parameters, context);
                break;
            case 302:
            case 605:
                ScanShopGoods(parameters, context);
                break;
            case 303:
                context.AddId(0, ReferenceTargetKind.Actor);
                break;
            case 311:
            case 312:
            case 313:
            case 314:
            case 315:
            case 316:
            case 317:
            case 318:
            case 319:
            case 320:
            case 321:
            case 322:
            case 324:
            case 325:
                ScanActorCommand(code, parameters, context);
                break;
            case 323:
                context.AddAsset(1, GetString(parameters, 1), AssetCategory.Characters);
                break;
            case 331:
            case 332:
                break;
            case 333:
                context.AddId(2, ReferenceTargetKind.State);
                break;
            case 336:
                context.AddId(1, ReferenceTargetKind.Enemy);
                break;
            case 337:
                context.AddId(1, ReferenceTargetKind.Animation);
                break;
            case 339:
                context.AddId(2, ReferenceTargetKind.Skill);
                break;
            case 355:
            case 655:
                ScanScript(GetString(parameters, 0), context, 0);
                break;
            case 356:
                ScanPluginCommand(GetString(parameters, 0), context);
                break;
        }
    }

    private static void ScanConditionalBranch(JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        int type = GetInt(p, 0) ?? -1;
        switch (type)
        {
            case 0: // switch
                context.AddId(1, ReferenceTargetKind.Switch);
                break;
            case 1: // variable
                context.AddId(1, ReferenceTargetKind.Variable);
                if ((GetInt(p, 2) ?? 0) == 1) context.AddId(3, ReferenceTargetKind.Variable);
                break;
            case 2: // self switch
                break;
            case 3: // timer
                break;
            case 4: // actor
                context.AddId(1, ReferenceTargetKind.Actor);
                int actorCondition = GetInt(p, 2) ?? -1;
                ReferenceTargetKind actorTarget = actorCondition switch
                {
                    2 => ReferenceTargetKind.Class,
                    3 => ReferenceTargetKind.Skill,
                    4 => ReferenceTargetKind.Weapon,
                    5 => ReferenceTargetKind.Armor,
                    6 => ReferenceTargetKind.State,
                    _ => ReferenceTargetKind.Unknown
                };
                if (actorTarget != ReferenceTargetKind.Unknown) context.AddId(3, actorTarget);
                break;
            case 5: // enemy
                if ((GetInt(p, 2) ?? -1) == 1) context.AddId(3, ReferenceTargetKind.State);
                break;
            case 6: // character direction
                break;
            case 7: // gold
                break;
            case 8:
                context.AddId(1, ReferenceTargetKind.Item);
                break;
            case 9:
                context.AddId(1, ReferenceTargetKind.Weapon);
                break;
            case 10:
                context.AddId(1, ReferenceTargetKind.Armor);
                break;
            case 11: // button
                break;
            case 12: // script
                ScanScript(GetString(p, 1), context, 1);
                break;
        }
    }

    private static void ScanControlVariables(JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        AddRange(p, 0, 1, ReferenceTargetKind.Variable, context);
        int operandType = GetInt(p, 3) ?? -1;
        if (operandType == 1)
        {
            context.AddId(4, ReferenceTargetKind.Variable);
        }
        else if (operandType == 4)
        {
            ScanScript(GetString(p, 4), context, 4);
        }
        else if (operandType == 3)
        {
            ScanGameDataOperand(p, context);
        }
    }

    private static void ScanGameDataOperand(JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        int category = GetInt(p, 4) ?? -1;
        int? dataId = GetInt(p, 5);
        ReferenceTargetKind kind = category switch
        {
            0 => ReferenceTargetKind.Item,
            1 => ReferenceTargetKind.Weapon,
            2 => ReferenceTargetKind.Armor,
            3 => ReferenceTargetKind.Actor,
            4 => ReferenceTargetKind.Enemy,
            _ => ReferenceTargetKind.Unknown
        };
        if (kind != ReferenceTargetKind.Unknown) context.AddIdValue(5, dataId, kind, "Control Variables game-data operand");
    }

    private static void ScanTransferPlayer(JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        int designation = GetInt(p, 0) ?? 0;
        if (designation == 0)
        {
            context.AddId(1, ReferenceTargetKind.Map);
        }
        else
        {
            context.AddId(1, ReferenceTargetKind.Variable, "Map ID variable");
            context.AddId(2, ReferenceTargetKind.Variable, "X-coordinate variable");
            context.AddId(3, ReferenceTargetKind.Variable, "Y-coordinate variable");
        }
    }

    private static void ScanSetVehicleLocation(JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        int designation = GetInt(p, 1) ?? 0;
        if (designation == 0)
        {
            context.AddId(2, ReferenceTargetKind.Map);
        }
        else
        {
            context.AddId(2, ReferenceTargetKind.Variable, "Map ID variable");
            context.AddId(3, ReferenceTargetKind.Variable, "X-coordinate variable");
            context.AddId(4, ReferenceTargetKind.Variable, "Y-coordinate variable");
        }
    }

    private static void ScanSetEventLocation(JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        int designation = GetInt(p, 1) ?? 0;
        if (designation == 1)
        {
            context.AddId(2, ReferenceTargetKind.Variable);
            context.AddId(3, ReferenceTargetKind.Variable);
        }
    }

    private static void ScanSetMovementRoute(JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        if (!TryGet(p, 1, out JsonElement route) || route.ValueKind != JsonValueKind.Object) return;
        if (!route.TryGetPropertyValue("list", out JsonElement list)) return;
        foreach ((int routeIndex, JsonElement command) in list.EnumerateIndexedArray())
        {
            int code = command.GetOptionalInt32("code") ?? 0;
            if (!command.TryGetPropertyValue("parameters", out JsonElement routeParameters)) continue;
            if (code == 41)
            {
                string? name = GetString(routeParameters, 0);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    context.Extraction.Add(
                        new ReferenceSource(
                            context.Extraction.RelativePath,
                            $"{context.ParametersPath}/1/list/{routeIndex}/parameters/0",
                            context.MapId,
                            context.RecordId,
                            context.EventId,
                            context.PageIndex,
                            context.CommandIndex),
                        new ReferenceTarget(ReferenceTargetKind.Asset, Name: name, AssetCategory: AssetCategory.Characters),
                        ReferenceUsageKind.MovementRoute,
                        ReferenceConfidence.Exact);
                }
            }
            else if (code == 44 && TryGet(routeParameters, 0, out JsonElement se))
            {
                string? name = se.GetOptionalString("name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    context.Extraction.Add(
                        new ReferenceSource(
                            context.Extraction.RelativePath,
                            $"{context.ParametersPath}/1/list/{routeIndex}/parameters/0/name",
                            context.MapId,
                            context.RecordId,
                            context.EventId,
                            context.PageIndex,
                            context.CommandIndex),
                        new ReferenceTarget(ReferenceTargetKind.Asset, Name: name, AssetCategory: AssetCategory.SoundEffects),
                        ReferenceUsageKind.MovementRoute,
                        ReferenceConfidence.Exact);
                }
            }
            else if (code == 45)
            {
                string? script = GetString(routeParameters, 0);
                HeuristicReferenceScanner.ScanText(
                    script,
                    new ReferenceSource(
                        context.Extraction.RelativePath,
                        $"{context.ParametersPath}/1/list/{routeIndex}/parameters/0",
                        context.MapId,
                        context.RecordId,
                        context.EventId,
                        context.PageIndex,
                        context.CommandIndex),
                    ReferenceUsageKind.Script,
                    context.Extraction);
            }
        }
    }

    private static void ScanCoordinateVariables(
        JsonElement p,
        int designationIndex,
        int xIndex,
        int yIndex,
        MvReferenceExtractor.EventCommandContext context)
    {
        if ((GetInt(p, designationIndex) ?? 0) != 1) return;
        context.AddId(xIndex, ReferenceTargetKind.Variable);
        context.AddId(yIndex, ReferenceTargetKind.Variable);
    }

    private static void ScanGetLocationInfo(JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        context.AddId(0, ReferenceTargetKind.Variable, "Result variable");
        int designation = GetInt(p, 2) ?? 0;
        if (designation == 1)
        {
            context.AddId(3, ReferenceTargetKind.Variable, "X-coordinate variable");
            context.AddId(4, ReferenceTargetKind.Variable, "Y-coordinate variable");
        }
    }

    private static void ScanBattleProcessing(JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        int designation = GetInt(p, 0) ?? 0;
        if (designation == 0) context.AddId(1, ReferenceTargetKind.Troop);
        else if (designation == 1) context.AddId(1, ReferenceTargetKind.Variable, "Troop ID variable");
    }

    private static void ScanShopGoods(JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        int kind = GetInt(p, 0) ?? -1;
        ReferenceTargetKind target = kind switch
        {
            0 => ReferenceTargetKind.Item,
            1 => ReferenceTargetKind.Weapon,
            2 => ReferenceTargetKind.Armor,
            _ => ReferenceTargetKind.Unknown
        };
        if (target != ReferenceTargetKind.Unknown) context.AddId(1, target);
    }

    private static void ScanActorCommand(int code, JsonElement p, MvReferenceExtractor.EventCommandContext context)
    {
        int selector = GetInt(p, 0) ?? 0;
        if (selector == 0) context.AddId(1, ReferenceTargetKind.Actor);
        else context.AddId(1, ReferenceTargetKind.Variable, "Actor ID variable");

        switch (code)
        {
            case 313:
                context.AddId(3, ReferenceTargetKind.State);
                break;
            case 318:
                context.AddId(3, ReferenceTargetKind.Skill);
                break;
            case 319:
                int equipType = GetInt(p, 2) ?? 0;
                ReferenceTargetKind equipmentKind = equipType == 0 ? ReferenceTargetKind.Weapon : ReferenceTargetKind.Armor;
                context.AddId(3, equipmentKind);
                break;
            case 321:
                context.AddId(2, ReferenceTargetKind.Class);
                break;
            case 322:
                context.AddAsset(2, GetString(p, 2), AssetCategory.Characters);
                context.AddAsset(4, GetString(p, 4), AssetCategory.Faces);
                context.AddAsset(6, GetString(p, 6), AssetCategory.SideViewActors);
                break;
        }
    }

    private static void AddAudioObject(
        JsonElement p,
        int parameterIndex,
        AssetCategory category,
        MvReferenceExtractor.EventCommandContext context)
    {
        if (!TryGet(p, parameterIndex, out JsonElement audio)) return;
        context.AddAsset(parameterIndex, audio.GetOptionalString("name"), category, "/name");
    }

    private static void ScanPluginCommand(string? command, MvReferenceExtractor.EventCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        string pluginName = command.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? command;
        context.AddPlugin(pluginName);
        ScanScript(command, context, 0, ReferenceUsageKind.PluginCommand);
    }

    private static void ScanScript(
        string? script,
        MvReferenceExtractor.EventCommandContext context,
        int parameterIndex,
        ReferenceUsageKind usage = ReferenceUsageKind.Script)
    {
        HeuristicReferenceScanner.ScanText(script, context.Source(parameterIndex), usage, context.Extraction);
    }

    private static void AddRange(
        JsonElement p,
        int startIndex,
        int endIndex,
        ReferenceTargetKind kind,
        MvReferenceExtractor.EventCommandContext context)
    {
        int? start = GetInt(p, startIndex);
        int? end = GetInt(p, endIndex);
        if (!start.HasValue || !end.HasValue) return;
        int lower = Math.Max(1, Math.Min(start.Value, end.Value));
        int upper = Math.Max(start.Value, end.Value);
        const int maximumExpandedRange = 10_000;
        if (upper - lower > maximumExpandedRange)
        {
            context.AddIdValue(startIndex, lower, kind, "Range start");
            context.AddIdValue(endIndex, upper, kind, "Range end");
            return;
        }

        for (int id = lower; id <= upper; id++)
        {
            context.AddIdValue(startIndex, id, kind, $"Range {lower}..{upper}");
        }
    }

    private static bool TryGet(JsonElement array, int index, out JsonElement value)
    {
        if (array.ValueKind == JsonValueKind.Array && index >= 0 && index < array.GetArrayLength())
        {
            value = array[index];
            return true;
        }
        value = default;
        return false;
    }

    private static int? GetInt(JsonElement array, int index)
    {
        if (!TryGet(array, index, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static string? GetString(JsonElement array, int index)
    {
        if (!TryGet(array, index, out JsonElement value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
