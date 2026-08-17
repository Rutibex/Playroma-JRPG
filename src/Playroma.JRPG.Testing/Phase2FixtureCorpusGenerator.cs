using System.Text;

namespace Playroma.JRPG.Testing;

public static class Phase2FixtureCorpusGenerator
{
    public static void Generate(string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        string root = Path.Combine(outputRoot, "phase2");
        Directory.CreateDirectory(root);

        WriteBytes(
            Path.Combine(root, "lossless-lexical", "Actors.json"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
                .Concat(Encoding.UTF8.GetBytes("[\r\n  null,\r\n  {\"id\":1,\"name\":\"Hero\",\"number\":1.0,\"exponent\":1e3,\"escaped\":\"\\u0041\",\"duplicate\":1,\"duplicate\":2}\r\n]\r\n"))
                .ToArray());

        WriteText(
            Path.Combine(root, "plugin-extension", "Actors.json"),
            "[null,{\"id\":1,\"name\":\"Hero\",\"note\":\"<Synthetic>\",\"customPluginData\":{\"formula\":\"a.atk * 4\",\"nested\":[1,2,3],\"enabled\":true}}]\n");

        WriteText(
            Path.Combine(root, "plugin-manifest", "plugins.js"),
            "// Synthetic wrapper retained by Phase 2.\nvar $plugins =\n[\n  {\"name\":\"SyntheticOne\",\"status\":true,\"description\":\"\",\"parameters\":{\"Nested\":\"[\\\"A\\\",\\\"B\\\"]\"}},\n  {\"name\":\"SyntheticOne\",\"status\":false,\"description\":\"duplicate\",\"parameters\":{}}\n];\n// Synthetic suffix.\n");

        WriteText(
            Path.Combine(root, "pretty-json", "System.json"),
            "{\n    \"gameTitle\": \"Pretty Synthetic Project\",\n    \"currencyUnit\": \"G\",\n    \"customField\": {\n        \"preserveMe\": true\n    }\n}\n");

        WriteText(
            Path.Combine(root, "map-data", "Map001.json"),
            "{\"displayName\":\"Synthetic Map\",\"width\":2,\"height\":1,\"tilesetId\":1,\"note\":\"\",\"data\":[1,2,3,4,5,6,7,8,9,10,11,12],\"events\":[null]}\n");
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
}
