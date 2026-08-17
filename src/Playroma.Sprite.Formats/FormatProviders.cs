namespace Playroma.Sprite.Formats;

public interface ISpriteFormatProvider
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyCollection<string> Extensions { get; }
    bool CanRead { get; }
    bool CanWrite { get; }
}

public sealed class PngSpriteFormatProvider : ISpriteFormatProvider
{
    public string Id => "png";
    public string DisplayName => "Portable Network Graphics";
    public IReadOnlyCollection<string> Extensions { get; } = [".png"];
    public bool CanRead => true;
    public bool CanWrite => true;
}

public sealed class GifSpriteFormatProvider : ISpriteFormatProvider
{
    public string Id => "gif";
    public string DisplayName => "Animated Graphics Interchange Format";
    public IReadOnlyCollection<string> Extensions { get; } = [".gif"];
    public bool CanRead => true;
    public bool CanWrite => true;
}

public sealed class PlayromaPackageFormatProvider : ISpriteFormatProvider
{
    public string Id => "playroma-sprite";
    public string DisplayName => "Playroma Editable Sprite";
    public IReadOnlyCollection<string> Extensions { get; } = [".playromasprite"];
    public bool CanRead => true;
    public bool CanWrite => true;
}

public sealed class SpriteFormatProviderRegistry
{
    private readonly Dictionary<string, ISpriteFormatProvider> providers = new(StringComparer.OrdinalIgnoreCase);

    public SpriteFormatProviderRegistry(bool includeBuiltIn = true)
    {
        if (includeBuiltIn)
        {
            Register(new PlayromaPackageFormatProvider());
            Register(new PngSpriteFormatProvider());
            Register(new GifSpriteFormatProvider());
        }
    }

    public IReadOnlyCollection<ISpriteFormatProvider> Providers => providers.Values;

    public void Register(ISpriteFormatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!providers.TryAdd(provider.Id, provider)) throw new InvalidOperationException($"Format provider '{provider.Id}' is already registered.");
    }

    public ISpriteFormatProvider FindByExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.StartsWith('.') ? extension : "." + extension;
        return providers.Values.FirstOrDefault(provider => provider.Extensions.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"No sprite format provider handles '{normalized}'.");
    }
}
