using PlayromaMusic.Core.Extensions;

namespace PlayromaMusic.Generators.Neural;

public sealed class NeuralGeneratorAdapter : IMusicGeneratorPlugin
{
    private readonly INeuralSequenceModel _model;
    private readonly double _temperature;
    private readonly int _maximumTokens;

    public NeuralGeneratorAdapter(INeuralSequenceModel model, double temperature = 0.85, int maximumTokens = 512)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _temperature = Math.Clamp(temperature, 0.05, 3.0);
        _maximumTokens = Math.Clamp(maximumTokens, 8, 32768);
    }

    public PluginMetadata Metadata => _model.Metadata with
    {
        Id = "neural:" + _model.Metadata.Id,
        Name = _model.Metadata.Name + " (neural adapter)"
    };

    public bool CanGenerate(GeneratorRequest request) => _model.Supports(request);

    public GeneratorResult Generate(GeneratorContext context)
    {
        double temperature = context.NeuralTemperature > 0 ? Math.Clamp(context.NeuralTemperature, 0.05, 3.0) : _temperature;
        int maximumTokens = context.NeuralMaximumTokens > 0 ? Math.Clamp(context.NeuralMaximumTokens, 8, 32768) : _maximumTokens;
        return _model.Generate(context, temperature, maximumTokens);
    }
}
