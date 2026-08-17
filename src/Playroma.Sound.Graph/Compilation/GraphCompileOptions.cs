namespace Playroma.Sound.Graph.Compilation;

public sealed class GraphCompileOptions
{
    public int VariationIndex { get; set; }
    public int EventQueueCapacity { get; set; } = 1024;
    public bool EnableBufferReuse { get; set; } = true;
    public bool ApplyMacroDefaults { get; set; } = true;
}
