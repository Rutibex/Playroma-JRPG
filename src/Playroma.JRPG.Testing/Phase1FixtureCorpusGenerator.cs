using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Playroma.JRPG.Testing;

public static class Phase1FixtureCorpusGenerator
{
    public static void Generate(string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        string empty = Path.Combine(outputRoot, "empty-source-project");
        string mapLayers = Path.Combine(outputRoot, "map-layer-catalog-project");

        string source = Path.Combine(outputRoot, "phase1", "source-layout-project");
        CopyDirectory(empty, source);
        AddOpenSyntheticRuntime(source);

        string www = Path.Combine(outputRoot, "phase1", "www-layout-project", "www");
        CopyDirectory(source, www);
        DeleteIfExists(Path.Combine(www, "Game.rpgproject"));

        string browser = Path.Combine(outputRoot, "phase1", "browser-only-project");
        CopyDirectory(source, browser);
        DeleteIfExists(Path.Combine(browser, "Game.rpgproject"));
        DeleteIfExists(Path.Combine(browser, "package.json"));

        string markerless = Path.Combine(outputRoot, "phase1", "markerless-readable-project");
        CopyDirectory(empty, markerless);
        DeleteIfExists(Path.Combine(markerless, "Game.rpgproject"));

        string incomplete = Path.Combine(outputRoot, "phase1", "incomplete-runtime-project");
        CopyDirectory(empty, incomplete);
        WriteText(Path.Combine(incomplete, "index.html"), "<!doctype html><title>Incomplete synthetic runtime</title>\n");

        string encrypted = Path.Combine(outputRoot, "phase1", "encrypted-asset-layout");
        CopyDirectory(source, encrypted);
        WriteBytes(Path.Combine(encrypted, "img", "pictures", "SyntheticEncrypted.rpgmvp"), [0x52, 0x50, 0x47, 0x4d, 0x56, 0x00]);

        string unicode = Path.Combine(outputRoot, "phase1", "unicode-path-project-猫");
        CopyDirectory(source, unicode);

        string multiple = Path.Combine(outputRoot, "phase1", "multiple-project-candidates");
        CopyDirectory(source, Path.Combine(multiple, "project-a"));
        CopyDirectory(source, Path.Combine(multiple, "project-b"));

        string pluginHeavy = Path.Combine(outputRoot, "phase1", "plugin-heavy-project");
        CopyDirectory(source, pluginHeavy);
        AddPluginHeavyManifest(pluginHeavy);

        string largeMap = Path.Combine(outputRoot, "phase1", "large-map-inventory-project");
        CopyDirectory(mapLayers, largeMap);
        AddOpenSyntheticRuntime(largeMap);

        string mz = Path.Combine(outputRoot, "phase1", "unsupported-mz-project");
        Directory.CreateDirectory(Path.Combine(mz, "data"));
        WriteText(Path.Combine(mz, "Game.rmmzproject"), "RPGMZ 1.8.0\n");
        JsonUtilities.Write(Path.Combine(mz, "data", "System.json"), new JsonObject
        {
            ["gameTitle"] = "Synthetic MZ Fixture"
        });

        string unsafePlugin = Path.Combine(outputRoot, "phase1", "unsafe-plugin-path");
        CopyDirectory(source, unsafePlugin);
        WriteText(
            Path.Combine(unsafePlugin, "js", "plugins.js"),
            "var $plugins =\n[\n  {\"name\":\"../Outside\",\"status\":true,\"description\":\"Synthetic unsafe path\",\"parameters\":{}}\n];\n");
    }

    private static void AddOpenSyntheticRuntime(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "js", "libs"));
        Directory.CreateDirectory(Path.Combine(root, "js", "plugins"));
        WriteText(Path.Combine(root, "index.html"), "<!doctype html><html><head><meta charset=\"utf-8\"></head><body><script src=\"js/rpg_core.js\"></script></body></html>\n");
        WriteText(Path.Combine(root, "package.json"), "{\n  \"name\": \"playroma-phase1-synthetic-fixture\",\n  \"main\": \"index.html\"\n}\n");
        WriteText(Path.Combine(root, "js", "rpg_core.js"), "var Utils = Utils || {};\nUtils.RPGMAKER_VERSION = \"1.6.2\";\n");
        foreach (string file in new[] { "rpg_managers.js", "rpg_objects.js", "rpg_scenes.js", "rpg_sprites.js", "rpg_windows.js", "main.js" })
        {
            WriteText(Path.Combine(root, "js", file), "// Independently authored synthetic Phase 1 fixture.\n");
        }
        WriteText(Path.Combine(root, "js", "plugins.js"), "var $plugins =\n[\n];\n");
    }

    private static void AddPluginHeavyManifest(string root)
    {
        JsonArray plugins = [];
        for (int index = 1; index <= 25; index++)
        {
            string name = string.Create(CultureInfo.InvariantCulture, $"SyntheticPlugin{index:D2}");
            plugins.Add(new JsonObject
            {
                ["name"] = name,
                ["status"] = index % 3 != 0,
                ["description"] = string.Create(CultureInfo.InvariantCulture, $"Synthetic plugin {index}"),
                ["parameters"] = new JsonObject
                {
                    ["Enabled"] = index % 2 == 0 ? "true" : "false",
                    ["Nested"] = "[\"A\",\"B\"]"
                }
            });
            string source = index == 25
                ? "// Synthetic Node heuristic fixture.\nvar fs = require(\"fs\");\n"
                : "// Independently authored synthetic plugin fixture.\n";
            WriteText(Path.Combine(root, "js", "plugins", name + ".js"), source);
        }

        string json = plugins.ToJsonString(JsonUtilities.IndentedOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
        WriteText(Path.Combine(root, "js", "plugins.js"), "var $plugins =\n" + json + ";\n");
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
