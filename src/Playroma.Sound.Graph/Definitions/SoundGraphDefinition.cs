namespace Playroma.Sound.Graph.Definitions;

public sealed class SoundGraphDefinition
{
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Id { get; set; } = "untitled";
    public string DisplayName { get; set; } = "Untitled Sound";
    public ulong RootSeed { get; set; } = 1;
    public List<NodeDefinition> Nodes { get; set; } = [];
    public List<ConnectionDefinition> Connections { get; set; } = [];
    public List<MacroDefinition> Macros { get; set; } = [];
    public GraphOutputDefinition Output { get; set; } = new();
    public GraphEditorDocumentMetadata Editor { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

public sealed class GraphEditorDocumentMetadata
{
    public float ViewX { get; set; }
    public float ViewY { get; set; }
    public float Zoom { get; set; } = 1f;
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public List<GraphGroupDefinition> Groups { get; set; } = [];
    public List<GraphCommentDefinition> Comments { get; set; } = [];
}

public sealed class GraphGroupDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Group";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 480;
    public float Height { get; set; } = 280;
    public bool Collapsed { get; set; }
}

public sealed class GraphCommentDefinition
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "Comment";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 260;
    public float Height { get; set; } = 120;
}

public sealed class NodeDefinition
{
    public string Id { get; set; } = "";
    public string TypeId { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public ulong? LocalSeed { get; set; }
    public Dictionary<string, GraphValue> Parameters { get; set; } = new(StringComparer.Ordinal);
    public NodeEditorMetadata? Editor { get; set; }
}

public sealed class NodeEditorMetadata
{
    public float X { get; set; }
    public float Y { get; set; }
    public bool Collapsed { get; set; }
    public bool Bypassed { get; set; }
    public string Comment { get; set; } = "";
}

public sealed class ConnectionDefinition
{
    public string FromNodeId { get; set; } = "";
    public string FromPortId { get; set; } = "out";
    public string ToNodeId { get; set; } = "";
    public string ToPortId { get; set; } = "in";
}

public sealed class GraphOutputDefinition
{
    public string NodeId { get; set; } = "";
    public string PortId { get; set; } = "out";
    public bool EnableLatencyCompensation { get; set; } = true;
    public double MaximumTailSeconds { get; set; } = 12;
}

public enum MacroMappingCurve
{
    Linear,
    SmoothStep,
    Exponential,
    Logarithmic,
    Stepped,
    Inverted
}

public sealed class MacroDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public float Minimum { get; set; } = 0;
    public float Maximum { get; set; } = 1;
    public float DefaultValue { get; set; }
    public List<MacroBinding> Bindings { get; set; } = [];
}

public sealed class MacroBinding
{
    public string TargetPath { get; set; } = "";
    public float InputMinimum { get; set; } = 0;
    public float InputMaximum { get; set; } = 1;
    public float OutputMinimum { get; set; }
    public float OutputMaximum { get; set; } = 1;
    public MacroMappingCurve Curve { get; set; } = MacroMappingCurve.Linear;
    public int Steps { get; set; } = 8;
}
