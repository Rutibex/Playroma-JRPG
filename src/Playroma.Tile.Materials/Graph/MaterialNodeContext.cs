using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Graph;

public sealed class MaterialNodeContext
{
    private readonly IReadOnlyDictionary<string, MaterialValue> _inputs;
    public MaterialNodeInstance Node { get; }
    public MaterialEvaluationContext Evaluation { get; }

    public MaterialNodeContext(MaterialNodeInstance node, MaterialEvaluationContext evaluation, IReadOnlyDictionary<string, MaterialValue> inputs)
    {
        Node = node; Evaluation = evaluation; _inputs = inputs;
    }

    public MaterialValue? Input(string name) => _inputs.TryGetValue(name, out var value) ? value : null;
    public ColorBuffer? ColorInput(string name) => Input(name)?.Color;
    public ScalarField? ScalarInput(string name) => Input(name)?.Scalar;
    public VectorField? VectorInput(string name) => Input(name)?.Vector;

    public string String(string name, string fallback = "") => Parameter(name)?.AsString(fallback) ?? fallback;
    public int Int(string name, int fallback = 0) => Parameter(name)?.AsInt(fallback) ?? fallback;
    public float Float(string name, float fallback = 0f) => Parameter(name)?.AsFloat(fallback) ?? fallback;
    public bool Bool(string name, bool fallback = false) => Parameter(name)?.AsBool(fallback) ?? fallback;
    public T Enum<T>(string name, T fallback) where T : struct, Enum => Parameter(name)?.AsEnum(fallback) ?? fallback;
    public System.Drawing.Color Color(string name, System.Drawing.Color fallback) => Parameter(name)?.AsColor(fallback) ?? fallback;
    public MaterialParameterValue? Parameter(string name) => Node.Parameters.TryGetValue(name, out var value) ? value : null;

    public void ThrowIfCancelled() => Evaluation.CancellationToken.ThrowIfCancellationRequested();
    public void Report(string stage, float fraction) => Evaluation.Progress?.Report(new MaterialProgress(stage, fraction, Node.Id));
}
