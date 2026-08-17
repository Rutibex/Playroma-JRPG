using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public static class YanflyProviderIdentity
{
    public const string ProviderId = "YEP-MV";

    public static PluginProviderManifest CreateManifest(string catalogVersion = "0.1.0") => new(
        ProviderId,
        "Yanfly Engine Plugins (MV)",
        "RPG Maker MV",
        catalogVersion,
        "https://www.yanfly.moe/wiki/Category:Yanfly_Engine_Plugins",
        "Metadata provider for user-owned Yanfly Engine Plugins. Plugin source is never redistributed.");
}
