using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;

namespace Playroma.JRPG.DatabaseEditing;

public static class MvParameterNames
{
    public static IReadOnlyList<string> All { get; } =
        ["Max HP", "Max MP", "Attack", "Defense", "M.Attack", "M.Defense", "Agility", "Luck"];

    public static string Get(int parameterId) => parameterId >= 0 && parameterId < All.Count
        ? All[parameterId]
        : $"Parameter {parameterId}";
}

public sealed record MvActorRecordSnapshot(
    int Id,
    string Name,
    string Nickname,
    string Profile,
    int ClassId,
    int InitialLevel,
    int MaximumLevel,
    string FaceName,
    int FaceIndex,
    string CharacterName,
    int CharacterIndex,
    string BattlerName,
    IReadOnlyList<int> EquipmentIds,
    string Note);

public sealed record MvClassLearningSnapshot(int Level, int SkillId, string RawJson);

public sealed record MvClassRecordSnapshot(
    int Id,
    string Name,
    IReadOnlyList<int> ExpParameters,
    IReadOnlyList<IReadOnlyList<int>> ParameterCurves,
    IReadOnlyList<MvClassLearningSnapshot> Learnings,
    int TraitCount,
    string Note);

public static class ActorClassRecordReader
{
    public static MvActorRecordSnapshot ReadActor(MutableJsonProjectDocument document, int recordId)
    {
        JsonPath root = JsonPath.Root.Append(Validate(recordId));
        EnsureObject(document, root, "actor");
        return new MvActorRecordSnapshot(
            recordId,
            document.GetString(root.Append("name")) ?? string.Empty,
            document.GetString(root.Append("nickname")) ?? string.Empty,
            document.GetString(root.Append("profile")) ?? string.Empty,
            document.GetInt32(root.Append("classId")) ?? 0,
            document.GetInt32(root.Append("initialLevel")) ?? 1,
            document.GetInt32(root.Append("maxLevel")) ?? 99,
            document.GetString(root.Append("faceName")) ?? string.Empty,
            document.GetInt32(root.Append("faceIndex")) ?? 0,
            document.GetString(root.Append("characterName")) ?? string.Empty,
            document.GetInt32(root.Append("characterIndex")) ?? 0,
            document.GetString(root.Append("battlerName")) ?? string.Empty,
            ReadIntArray(document, root.Append("equips")),
            document.GetString(root.Append("note")) ?? string.Empty);
    }

    public static MvClassRecordSnapshot ReadClass(MutableJsonProjectDocument document, int recordId)
    {
        JsonPath root = JsonPath.Root.Append(Validate(recordId));
        EnsureObject(document, root, "class");
        IReadOnlyList<int> exp = ReadIntArray(document, root.Append("expParams"));
        IReadOnlyList<IReadOnlyList<int>> curves = ReadNestedIntArrays(document, root.Append("params"));
        IReadOnlyList<MvClassLearningSnapshot> learnings = ReadLearnings(document, root.Append("learnings"));
        return new MvClassRecordSnapshot(
            recordId,
            document.GetString(root.Append("name")) ?? string.Empty,
            exp,
            curves,
            learnings,
            ArrayCount(document, root.Append("traits")),
            document.GetString(root.Append("note")) ?? string.Empty);
    }

    public static IReadOnlyList<int> ReadIntArray(MutableJsonProjectDocument document, JsonPath path)
    {
        if (document.Syntax.Resolve(path) is not JsonArraySyntax array) return [];
        int[] result = new int[array.Elements.Count];
        for (int index = 0; index < result.Length; index++) result[index] = document.GetInt32(path.Append(index)) ?? 0;
        return result;
    }

    private static IReadOnlyList<IReadOnlyList<int>> ReadNestedIntArrays(MutableJsonProjectDocument document, JsonPath path)
    {
        if (document.Syntax.Resolve(path) is not JsonArraySyntax outer) return [];
        List<IReadOnlyList<int>> result = new(outer.Elements.Count);
        for (int index = 0; index < outer.Elements.Count; index++) result.Add(ReadIntArray(document, path.Append(index)));
        return result;
    }

    private static IReadOnlyList<MvClassLearningSnapshot> ReadLearnings(MutableJsonProjectDocument document, JsonPath path)
    {
        if (document.Syntax.Resolve(path) is not JsonArraySyntax array) return [];
        List<MvClassLearningSnapshot> result = new(array.Elements.Count);
        for (int index = 0; index < array.Elements.Count; index++)
        {
            JsonPath entry = path.Append(index);
            string raw = document.GetRawJson(entry) ?? "{}";
            result.Add(new MvClassLearningSnapshot(
                document.GetInt32(entry.Append("level")) ?? 1,
                document.GetInt32(entry.Append("skillId")) ?? 0,
                raw));
        }
        return result;
    }


    private static int ArrayCount(MutableJsonProjectDocument document, JsonPath path) =>
        (document.Syntax.Resolve(path) as JsonArraySyntax)?.Elements.Count ?? 0;

    private static void EnsureObject(MutableJsonProjectDocument document, JsonPath path, string kind)
    {
        if (document.Syntax.Resolve(path) is not JsonObjectSyntax)
        {
            throw new InvalidDataException($"The selected {kind} record is empty or invalid.");
        }
    }

    private static int Validate(int id) => id > 0 ? id : throw new ArgumentOutOfRangeException(nameof(id));
}

public sealed class ActorClassMutationService
{
    private readonly MvEditingSession _session;

    public ActorClassMutationService(MvEditingSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    public ValueTask SetActorGraphicsAsync(
        string relativePath,
        int actorId,
        string nameProperty,
        string assetName,
        string? indexProperty,
        int index,
        CancellationToken cancellationToken = default)
    {
        JsonPath root = JsonPath.Root.Append(ValidateId(actorId));
        List<IEditorCommand> commands =
        [
            new SetJsonValueCommand(relativePath, root.Append(nameProperty), EncodeString(assetName), $"Set {nameProperty}")
        ];
        if (indexProperty is not null)
        {
            commands.Add(new SetJsonValueCommand(
                relativePath,
                root.Append(indexProperty),
                index.ToString(CultureInfo.InvariantCulture),
                $"Set {indexProperty}"));
        }
        return ExecuteAsync($"Change actor {actorId} graphic", commands, cancellationToken);
    }

    public ValueTask SetActorEquipmentAsync(
        string relativePath,
        int actorId,
        IReadOnlyList<int> equipmentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(equipmentIds);
        string raw = JsonSerializer.Serialize(equipmentIds.Select(value => Math.Max(0, value)).ToArray());
        return ExecuteAsync(
            $"Change actor {actorId} initial equipment",
            [new SetJsonValueCommand(relativePath, JsonPath.Root.Append(ValidateId(actorId)).Append("equips"), raw)],
            cancellationToken);
    }

    public ValueTask SetClassExpParametersAsync(
        string relativePath,
        int classId,
        IReadOnlyList<int> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Count != 4) throw new ArgumentException("MV class EXP parameters require exactly four values.", nameof(parameters));
        int[] normalized =
        [
            Math.Clamp(parameters[0], 1, 9999),
            Math.Clamp(parameters[1], 0, 9999),
            Math.Clamp(parameters[2], 0, 100),
            Math.Clamp(parameters[3], 1, 100)
        ];
        return ExecuteAsync(
            $"Change class {classId} EXP curve",
            [new SetJsonValueCommand(relativePath, JsonPath.Root.Append(ValidateId(classId)).Append("expParams"), JsonSerializer.Serialize(normalized))],
            cancellationToken);
    }

    public ValueTask SetClassParameterCurveAsync(
        string relativePath,
        int classId,
        int parameterId,
        IReadOnlyList<int> values,
        CancellationToken cancellationToken = default)
    {
        if (parameterId < 0 || parameterId >= 8) throw new ArgumentOutOfRangeException(nameof(parameterId));
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 100) throw new ArgumentException("MV parameter curves require indexes 0 through 99.", nameof(values));
        int[] normalized = values.Take(100).Select(value => Math.Clamp(value, 0, 9_999_999)).ToArray();
        return ExecuteAsync(
            $"Change class {classId} {MvParameterNames.Get(parameterId)} curve",
            [new SetJsonValueCommand(relativePath, JsonPath.Root.Append(ValidateId(classId)).Append("params").Append(parameterId), JsonSerializer.Serialize(normalized))],
            cancellationToken);
    }

    public ValueTask SetClassLearningsAsync(
        string relativePath,
        int classId,
        IReadOnlyList<MvClassLearningSnapshot> learnings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(learnings);
        return ExecuteAsync(
            $"Change class {classId} skill learnings",
            [new SetJsonValueCommand(
                relativePath,
                JsonPath.Root.Append(ValidateId(classId)).Append("learnings"),
                BuildClassLearningsJson(learnings))],
            cancellationToken);
    }

    public static string BuildClassLearningsJson(IReadOnlyList<MvClassLearningSnapshot> learnings)
    {
        ArgumentNullException.ThrowIfNull(learnings);
        JsonArray output = [];
        foreach (MvClassLearningSnapshot learning in learnings)
        {
            JsonObject entry = JsonNode.Parse(learning.RawJson) as JsonObject
                ?? throw new InvalidDataException("A class learning entry must be a JSON object.");
            entry["level"] = Math.Clamp(learning.Level, 1, 99);
            entry["skillId"] = Math.Max(0, learning.SkillId);
            output.Add(entry);
        }
        return output.ToJsonString();
    }

    public static long ExperienceForLevel(int level, IReadOnlyList<int> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Count < 4) return 0;
        int normalizedLevel = Math.Clamp(level, 1, 99);
        double basis = parameters[0];
        double extra = parameters[1];
        double accelerationA = parameters[2];
        double accelerationB = Math.Max(1, parameters[3]);
        double result = basis
            * Math.Pow(normalizedLevel - 1, 0.9 + accelerationA / 250d)
            * normalizedLevel
            * (normalizedLevel + 1)
            / (6d + Math.Pow(normalizedLevel, 2) / 50d / accelerationB)
            + (normalizedLevel - 1) * extra;
        if (double.IsNaN(result) || result <= 0) return 0;
        return checked((long)Math.Round(Math.Min(result, long.MaxValue), MidpointRounding.AwayFromZero));
    }

    public static int[] GenerateSmoothCurve(int levelOne, int levelNinetyNine, double exponent)
    {
        int start = Math.Max(0, levelOne);
        int end = Math.Max(0, levelNinetyNine);
        double power = Math.Clamp(exponent, 0.1d, 8d);
        int[] result = new int[100];
        result[0] = start;
        for (int level = 1; level <= 99; level++)
        {
            double t = (level - 1) / 98d;
            double value = start + (end - start) * Math.Pow(t, power);
            result[level] = checked((int)Math.Round(Math.Clamp(value, 0d, 9_999_999d), MidpointRounding.AwayFromZero));
        }
        return result;
    }

    private async ValueTask ExecuteAsync(string description, IReadOnlyList<IEditorCommand> commands, CancellationToken cancellationToken)
    {
        await using ProjectTransaction transaction = _session.BeginTransaction(description);
        foreach (IEditorCommand command in commands) transaction.Add(command);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string EncodeString(string value) => JsonSerializer.Serialize(value ?? string.Empty);
    private static int ValidateId(int id) => id > 0 ? id : throw new ArgumentOutOfRangeException(nameof(id));
}
