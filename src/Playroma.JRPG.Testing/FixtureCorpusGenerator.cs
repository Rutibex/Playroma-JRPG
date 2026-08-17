using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Specifications;

namespace Playroma.JRPG.Testing;

public sealed class FixtureCorpusGenerator
{
    public void Generate(string outputRoot, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(outputRoot);
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        if (Directory.Exists(outputRoot))
        {
            Directory.Delete(outputRoot, recursive: true);
        }

        Directory.CreateDirectory(outputRoot);
        MvSpecificationCatalog catalog = MvSpecificationCatalog.Load(repositoryRoot);

        GenerateEmptyProject(Path.Combine(outputRoot, "empty-source-project"));
        GenerateDatabaseCatalog(Path.Combine(outputRoot, "database-catalog-project"));
        GenerateEventCommandCatalog(Path.Combine(outputRoot, "event-command-catalog-project"), catalog);
        GenerateMapLayerCatalog(Path.Combine(outputRoot, "map-layer-catalog-project"));
        GenerateMovementRouteCatalog(Path.Combine(outputRoot, "movement-route-catalog-project"), catalog);
        GeneratePluginParameterCatalog(Path.Combine(outputRoot, "plugin-parameter-catalog-project"), repositoryRoot);
        GenerateMalformedFixtures(Path.Combine(outputRoot, "malformed"));
        Phase1FixtureCorpusGenerator.Generate(outputRoot);
        Phase2FixtureCorpusGenerator.Generate(outputRoot);
        WriteManifest(outputRoot);
    }

    public FixtureCheckResult Check(string committedRoot, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(committedRoot);
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        string temporary = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Fixtures-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        try
        {
            Generate(temporary, repositoryRoot);
            Dictionary<string, string> expected = ReadManifest(Path.Combine(committedRoot, "fixture-manifest.json"));
            Dictionary<string, string> actual = ReadManifest(Path.Combine(temporary, "fixture-manifest.json"));

            List<string> differences = [];
            foreach (string missing in expected.Keys.Except(actual.Keys, StringComparer.Ordinal))
            {
                differences.Add("Missing generated file: " + missing);
            }

            foreach (string added in actual.Keys.Except(expected.Keys, StringComparer.Ordinal))
            {
                differences.Add("Unexpected generated file: " + added);
            }

            foreach (string common in expected.Keys.Intersect(actual.Keys, StringComparer.Ordinal))
            {
                if (!string.Equals(expected[common], actual[common], StringComparison.Ordinal))
                {
                    differences.Add("Fixture differs: " + common);
                }
            }

            return new FixtureCheckResult(differences.Count == 0, differences);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static void GenerateEmptyProject(string root)
    {
        CreateProjectFolders(root);
        foreach (string file in StandardArrayFiles)
        {
            JsonUtilities.Write(Path.Combine(root, "data", file), new JsonArray((JsonNode?)null));
        }

        JsonUtilities.Write(Path.Combine(root, "data", "MapInfos.json"), new JsonArray((JsonNode?)null));
        JsonUtilities.Write(Path.Combine(root, "data", "System.json"), CreateSystem());
        WriteProjectMarker(root);
    }

    private static void GenerateDatabaseCatalog(string root)
    {
        CreateProjectFolders(root);
        WriteStandardDatabase(root, includeMap: true);
        WriteProjectMarker(root);
    }

    private static void GenerateEventCommandCatalog(string root, MvSpecificationCatalog catalog)
    {
        CreateProjectFolders(root);
        WriteStandardDatabase(root, includeMap: true);

        JsonArray commonEvents = [(JsonNode?)null];
        int id = 1;
        foreach (EventCommandSpecification command in catalog.EventCommands)
        {
            JsonArray list = BuildCommandFixture(command);
            commonEvents.Add(new JsonObject
            {
                ["id"] = id,
                ["list"] = list,
                ["name"] = string.Create(CultureInfo.InvariantCulture, $"Command {command.Code:D3} - {command.Name}"),
                ["switchId"] = 1,
                ["trigger"] = 0
            });
            id++;
        }

        JsonUtilities.Write(Path.Combine(root, "data", "CommonEvents.json"), commonEvents);
        WriteProjectMarker(root);
    }

    private static JsonArray BuildCommandFixture(EventCommandSpecification command)
    {
        JsonNode? parameters = JsonNode.Parse(command.SampleParameters.GetRawText());
        JsonObject row = Command(command.Code, 0, parameters as JsonArray ?? []);

        return command.Code switch
        {
            401 => [Command(101, 0, ["", 0, 0, 2]), row, End()],
            402 => [Command(102, 0, [new JsonArray("Choice A"), 0, 0, 2, 0]), row, End(1), Command(404, 0, []), End()],
            403 => [Command(102, 0, [new JsonArray("Choice A"), 0, 0, 2, 0]), row, End(1), Command(404, 0, []), End()],
            404 => [Command(102, 0, [new JsonArray("Choice A"), 0, 0, 2, 0]), Command(402, 0, [0, "Choice A"]), End(1), row, End()],
            405 => [Command(105, 0, [2, false]), row, End()],
            408 => [Command(108, 0, ["Fixture comment"]), row, End()],
            411 => [Command(111, 0, [0, 1, 0]), End(1), row, End(1), Command(412, 0, []), End()],
            412 => [Command(111, 0, [0, 1, 0]), End(1), row, End()],
            413 => [Command(112, 0, []), row, End()],
            505 => [Command(205, 0, [0, CreateMovementRoute([Route(1), Route(0)])]), row, End()],
            601 => BattleBranchFixture(row),
            602 => BattleBranchFixture(row),
            603 => BattleBranchFixture(row),
            604 => BattleBranchFixture(row),
            605 => [Command(302, 0, [0, 1, 0, 0, false]), row, End()],
            655 => [Command(355, 0, ["var fixture = true;"]), row, End()],
            0 => [row],
            _ => [row, End()]
        };
    }

    private static JsonArray BattleBranchFixture(JsonObject highlighted)
    {
        return
        [
            Command(301, 0, [0, 1, true, true]),
            highlighted,
            End(1),
            Command(604, 0, []),
            End()
        ];
    }

    private static void GenerateMapLayerCatalog(string root)
    {
        CreateProjectFolders(root);
        WriteStandardDatabase(root, includeMap: false);

        const int width = 4;
        const int height = 4;
        JsonArray data = [];
        int[][] layers =
        [
            Enumerable.Range(0, 16).Select(i => 2048 + i).ToArray(),
            Enumerable.Range(0, 16).Select(i => i % 2 == 0 ? 2816 + i : 0).ToArray(),
            Enumerable.Range(0, 16).Select(i => i % 3 == 0 ? 4352 + i : 0).ToArray(),
            Enumerable.Range(0, 16).Select(i => i % 5 == 0 ? 4400 + i : 0).ToArray(),
            Enumerable.Range(0, 16).Select(i => i % 16).ToArray(),
            Enumerable.Range(1, 16).ToArray()
        ];
        foreach (int[] layer in layers)
        {
            foreach (int value in layer)
            {
                data.Add(value);
            }
        }

        JsonArray pages = [];
        string[] validKeys = ["switch1Valid", "switch2Valid", "variableValid", "selfSwitchValid", "itemValid", "actorValid"];
        foreach (string key in validKeys)
        {
            JsonObject page = CreateEventPage();
            ((JsonObject)page["conditions"]!)[key] = true;
            pages.Add(page);
        }
        pages.Add(CreateEventPage());

        JsonObject map = CreateMap(width, height, data);
        map["events"] = new JsonArray(
            (JsonNode?)null,
            new JsonObject
            {
                ["id"] = 1,
                ["name"] = "All Page Conditions",
                ["note"] = "Synthetic fixture",
                ["pages"] = pages,
                ["x"] = 1,
                ["y"] = 1
            });

        JsonUtilities.Write(Path.Combine(root, "data", "Map001.json"), map);
        JsonUtilities.Write(Path.Combine(root, "data", "MapInfos.json"), new JsonArray(
            (JsonNode?)null,
            CreateMapInfo(1, "All Layers", 0, 1)));
        JsonUtilities.Write(Path.Combine(root, "data", "Tilesets.json"), new JsonArray(
            (JsonNode?)null,
            CreateTileset(1, "World Mode", 0),
            CreateTileset(2, "Area Mode", 1)));
        WriteProjectMarker(root);
    }

    private static void GenerateMovementRouteCatalog(string root, MvSpecificationCatalog catalog)
    {
        CreateProjectFolders(root);
        WriteStandardDatabase(root, includeMap: false);

        JsonArray routeRows = [];
        foreach (MovementRouteCommandSpecification spec in catalog.MovementRouteCommands)
        {
            JsonArray parameters = JsonNode.Parse(spec.SampleParameters.GetRawText()) as JsonArray ?? [];
            routeRows.Add(Route(spec.Code, parameters));
        }

        JsonObject page = CreateEventPage();
        page["moveType"] = 3;
        page["moveRoute"] = CreateMovementRoute(routeRows);
        page["list"] = new JsonArray(
            Command(205, 0, [0, CreateMovementRoute(routeRows.DeepClone().AsArray())]),
            End());

        JsonObject map = CreateMap(4, 4, new JsonArray(Enumerable.Repeat<JsonNode?>(0, 96).ToArray()));
        map["events"] = new JsonArray(
            (JsonNode?)null,
            new JsonObject
            {
                ["id"] = 1,
                ["name"] = "All Movement Route Commands",
                ["note"] = "Synthetic fixture",
                ["pages"] = new JsonArray(page),
                ["x"] = 1,
                ["y"] = 1
            });
        JsonUtilities.Write(Path.Combine(root, "data", "Map001.json"), map);
        JsonUtilities.Write(Path.Combine(root, "data", "MapInfos.json"), new JsonArray((JsonNode?)null, CreateMapInfo(1, "Movement Routes", 0, 1)));
        WriteProjectMarker(root);
    }

    private static void GeneratePluginParameterCatalog(string root, string repositoryRoot)
    {
        CreateProjectFolders(root);
        WriteStandardDatabase(root, includeMap: false);
        string specificationPath = Path.Combine(repositoryRoot, "specifications", "mv-plugin-parameter-fixtures.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(specificationPath));
        JsonObject parameters = new();
        foreach (JsonElement parameter in document.RootElement.GetProperty("parameters").EnumerateArray())
        {
            parameters[parameter.GetProperty("name").GetString()!] = parameter.GetProperty("raw").GetString();
        }

        string plugins = "var $plugins =\n" + new JsonArray(
            new JsonObject
            {
                ["name"] = "PlayromaFixtureParameters",
                ["status"] = true,
                ["description"] = "Synthetic parameter encoding fixture",
                ["parameters"] = parameters
            },
            new JsonObject
            {
                ["name"] = "PlayromaFixtureParameters",
                ["status"] = false,
                ["description"] = "Deliberate duplicate entry for preservation tests",
                ["parameters"] = new JsonObject { ["BooleanLike"] = "false" }
            }).ToJsonString(JsonUtilities.IndentedOptions) + ";\n";
        File.WriteAllText(Path.Combine(root, "js", "plugins.js"), plugins, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "js", "plugins", "PlayromaFixtureParameters.js"), PluginFixtureSource + "\n", new UTF8Encoding(false));
        WriteProjectMarker(root);
    }

    private static void GenerateMalformedFixtures(string root)
    {
        string invalidJson = Path.Combine(root, "invalid-json");
        Directory.CreateDirectory(Path.Combine(invalidJson, "data"));
        File.WriteAllText(Path.Combine(invalidJson, "data", "Actors.json"), "[null,{\"id\":1", new UTF8Encoding(false));
        JsonUtilities.Write(Path.Combine(invalidJson, "expected-diagnostics.json"), Diagnostic("PJRPG-JSON-001", "error"));

        string missingIndex = Path.Combine(root, "missing-index-zero");
        CreateProjectFolders(missingIndex);
        JsonUtilities.Write(Path.Combine(missingIndex, "data", "Actors.json"), new JsonArray(CreateActor()));
        JsonUtilities.Write(Path.Combine(missingIndex, "expected-diagnostics.json"), Diagnostic("PJRPG-MV-001", "warning"));

        string badMap = Path.Combine(root, "map-data-length-mismatch");
        CreateProjectFolders(badMap);
        JsonUtilities.Write(Path.Combine(badMap, "data", "Map001.json"), CreateMap(4, 4, new JsonArray(0, 0, 0)));
        JsonUtilities.Write(Path.Combine(badMap, "expected-diagnostics.json"), Diagnostic("PJRPG-MAP-001", "error"));

        string unknown = Path.Combine(root, "unknown-event-command");
        CreateProjectFolders(unknown);
        JsonUtilities.Write(Path.Combine(unknown, "data", "CommonEvents.json"), new JsonArray(
            (JsonNode?)null,
            new JsonObject
            {
                ["id"] = 1,
                ["list"] = new JsonArray(Command(9999, 0, ["preserve", 42]), End()),
                ["name"] = "Unknown Command",
                ["switchId"] = 1,
                ["trigger"] = 0
            }));
        JsonUtilities.Write(Path.Combine(unknown, "expected-diagnostics.json"), Diagnostic("PJRPG-EVT-UNKNOWN", "info"));

        string duplicate = Path.Combine(root, "duplicate-plugin-entry");
        Directory.CreateDirectory(Path.Combine(duplicate, "js"));
        File.WriteAllText(Path.Combine(duplicate, "js", "plugins.js"),
            "var $plugins = [\n  {\"name\":\"Fixture\",\"status\":true,\"description\":\"\",\"parameters\":{}},\n  {\"name\":\"Fixture\",\"status\":true,\"description\":\"\",\"parameters\":{}}\n];\n",
            new UTF8Encoding(false));
        JsonUtilities.Write(Path.Combine(duplicate, "expected-diagnostics.json"), Diagnostic("PJRPG-PLUGIN-DUPLICATE", "warning"));

        string missingFile = Path.Combine(root, "missing-standard-file");
        Directory.CreateDirectory(Path.Combine(missingFile, "data"));
        JsonUtilities.Write(Path.Combine(missingFile, "data", "Actors.json"), new JsonArray((JsonNode?)null));
        JsonUtilities.Write(Path.Combine(missingFile, "expected-diagnostics.json"), Diagnostic("PJRPG-PROJECT-MISSING", "warning"));
    }

    private static JsonArray Diagnostic(string code, string severity)
    {
        return [new JsonObject { ["code"] = code, ["severity"] = severity }];
    }

    private static void WriteStandardDatabase(string root, bool includeMap)
    {
        JsonUtilities.Write(Path.Combine(root, "data", "Actors.json"), new JsonArray((JsonNode?)null, CreateActor()));
        JsonUtilities.Write(Path.Combine(root, "data", "Animations.json"), new JsonArray((JsonNode?)null, CreateAnimation()));
        JsonUtilities.Write(Path.Combine(root, "data", "Armors.json"), new JsonArray((JsonNode?)null, CreateArmor()));
        JsonUtilities.Write(Path.Combine(root, "data", "Classes.json"), new JsonArray((JsonNode?)null, CreateClass()));
        JsonUtilities.Write(Path.Combine(root, "data", "CommonEvents.json"), new JsonArray((JsonNode?)null, CreateCommonEvent()));
        JsonUtilities.Write(Path.Combine(root, "data", "Enemies.json"), new JsonArray((JsonNode?)null, CreateEnemy()));
        JsonUtilities.Write(Path.Combine(root, "data", "Items.json"), new JsonArray((JsonNode?)null, CreateItem()));
        JsonUtilities.Write(Path.Combine(root, "data", "Skills.json"), new JsonArray((JsonNode?)null, CreateSkill()));
        JsonUtilities.Write(Path.Combine(root, "data", "States.json"), new JsonArray((JsonNode?)null, CreateState()));
        JsonUtilities.Write(Path.Combine(root, "data", "System.json"), CreateSystem());
        JsonUtilities.Write(Path.Combine(root, "data", "Tilesets.json"), new JsonArray((JsonNode?)null, CreateTileset(1, "Fixture Tileset", 1)));
        JsonUtilities.Write(Path.Combine(root, "data", "Troops.json"), new JsonArray((JsonNode?)null, CreateTroop()));
        JsonUtilities.Write(Path.Combine(root, "data", "Weapons.json"), new JsonArray((JsonNode?)null, CreateWeapon()));

        if (includeMap)
        {
            JsonUtilities.Write(Path.Combine(root, "data", "MapInfos.json"), new JsonArray((JsonNode?)null, CreateMapInfo(1, "Fixture Map", 0, 1)));
            JsonUtilities.Write(Path.Combine(root, "data", "Map001.json"), CreateMap(4, 4, new JsonArray(Enumerable.Repeat<JsonNode?>(0, 96).ToArray())));
        }
        else
        {
            JsonUtilities.Write(Path.Combine(root, "data", "MapInfos.json"), new JsonArray((JsonNode?)null));
        }
    }

    private static JsonObject CreateActor() => new()
    {
        ["id"] = 1, ["battlerName"] = "", ["characterIndex"] = 0, ["characterName"] = "",
        ["classId"] = 1, ["equips"] = new JsonArray(0, 0, 0, 0, 0), ["faceIndex"] = 0,
        ["faceName"] = "", ["traits"] = new JsonArray(), ["initialLevel"] = 1, ["maxLevel"] = 99,
        ["name"] = "Fixture Actor", ["nickname"] = "Fixture", ["note"] = "<PlayromaFixture: actor>",
        ["profile"] = "Synthetic record"
    };

    private static JsonObject CreateAnimation() => new()
    {
        ["id"] = 1, ["animation1Hue"] = 0, ["animation1Name"] = "", ["animation2Hue"] = 0,
        ["animation2Name"] = "", ["frames"] = new JsonArray(new JsonArray()), ["name"] = "Fixture Animation",
        ["position"] = 1, ["timings"] = new JsonArray()
    };

    private static JsonObject CreateArmor() => new()
    {
        ["id"] = 1, ["atypeId"] = 1, ["description"] = "Synthetic armor", ["etypeId"] = 2,
        ["traits"] = new JsonArray(), ["iconIndex"] = 0, ["name"] = "Fixture Armor",
        ["note"] = "<PlayromaFixture: armor>", ["params"] = new JsonArray(0, 0, 0, 1, 0, 0, 0, 0), ["price"] = 10
    };

    private static JsonObject CreateClass()
    {
        JsonArray parameters = [];
        for (int parameter = 0; parameter < 8; parameter++)
        {
            parameters.Add(new JsonArray(Enumerable.Range(0, 100).Select(level => (JsonNode?)(parameter + level)).ToArray()));
        }
        return new JsonObject
        {
            ["id"] = 1, ["expParams"] = new JsonArray(30, 20, 30, 30), ["traits"] = new JsonArray(),
            ["learnings"] = new JsonArray(new JsonObject { ["level"] = 1, ["note"] = "", ["skillId"] = 1 }),
            ["name"] = "Fixture Class", ["note"] = "<PlayromaFixture: class>", ["params"] = parameters
        };
    }

    private static JsonObject CreateCommonEvent() => new()
    {
        ["id"] = 1, ["list"] = new JsonArray(Command(108, 0, ["Synthetic fixture"]), End()),
        ["name"] = "Fixture Common Event", ["switchId"] = 1, ["trigger"] = 0
    };

    private static JsonObject CreateEnemy() => new()
    {
        ["id"] = 1,
        ["actions"] = new JsonArray(new JsonObject { ["conditionParam1"] = 0, ["conditionParam2"] = 0, ["conditionType"] = 0, ["rating"] = 5, ["skillId"] = 1 }),
        ["battlerHue"] = 0, ["battlerName"] = "", ["dropItems"] = new JsonArray(
            new JsonObject { ["kind"] = 0, ["dataId"] = 1, ["denominator"] = 1 },
            new JsonObject { ["kind"] = 0, ["dataId"] = 1, ["denominator"] = 1 },
            new JsonObject { ["kind"] = 0, ["dataId"] = 1, ["denominator"] = 1 }),
        ["exp"] = 1, ["traits"] = new JsonArray(), ["gold"] = 1, ["name"] = "Fixture Enemy",
        ["note"] = "<PlayromaFixture: enemy>", ["params"] = new JsonArray(10, 0, 5, 5, 5, 5, 5, 5)
    };

    private static JsonObject Damage() => new() { ["critical"] = false, ["elementId"] = 0, ["formula"] = "0", ["type"] = 0, ["variance"] = 20 };

    private static JsonObject CreateItem() => new()
    {
        ["id"] = 1, ["animationId"] = 0, ["consumable"] = true, ["damage"] = Damage(),
        ["description"] = "Synthetic item", ["effects"] = new JsonArray(), ["hitType"] = 0,
        ["iconIndex"] = 0, ["itypeId"] = 1, ["name"] = "Fixture Item", ["note"] = "<PlayromaFixture: item>",
        ["occasion"] = 0, ["price"] = 1, ["repeats"] = 1, ["scope"] = 0, ["speed"] = 0,
        ["successRate"] = 100, ["tpGain"] = 0
    };

    private static JsonObject CreateSkill() => new()
    {
        ["id"] = 1, ["animationId"] = 0, ["damage"] = Damage(), ["description"] = "Synthetic skill",
        ["effects"] = new JsonArray(), ["hitType"] = 0, ["iconIndex"] = 0, ["message1"] = " uses Fixture Skill!",
        ["message2"] = "", ["mpCost"] = 0, ["name"] = "Fixture Skill", ["note"] = "<PlayromaFixture: skill>",
        ["occasion"] = 1, ["repeats"] = 1, ["requiredWtypeId1"] = 0, ["requiredWtypeId2"] = 0,
        ["scope"] = 1, ["speed"] = 0, ["stypeId"] = 1, ["successRate"] = 100, ["tpCost"] = 0, ["tpGain"] = 0
    };

    private static JsonObject CreateState() => new()
    {
        ["id"] = 1, ["autoRemovalTiming"] = 0, ["chanceByDamage"] = 100, ["iconIndex"] = 0,
        ["maxTurns"] = 1, ["message1"] = "", ["message2"] = "", ["message3"] = "", ["message4"] = "",
        ["minTurns"] = 1, ["motion"] = 0, ["name"] = "Fixture State", ["note"] = "<PlayromaFixture: state>",
        ["overlay"] = 0, ["priority"] = 50, ["removeAtBattleEnd"] = false,
        ["removeByDamage"] = false, ["removeByRestriction"] = false, ["removeByWalking"] = false,
        ["restriction"] = 0, ["stepsToRemove"] = 100, ["traits"] = new JsonArray()
    };

    private static JsonObject CreateTileset(int id, string name, int mode) => new()
    {
        ["id"] = id, ["flags"] = new JsonArray(Enumerable.Repeat<JsonNode?>(0, 8192).ToArray()),
        ["mode"] = mode, ["name"] = name, ["note"] = "<PlayromaFixture: tileset>",
        ["tilesetNames"] = new JsonArray("Fixture_A1", "Fixture_A2", "Fixture_A3", "Fixture_A4", "Fixture_A5", "Fixture_B", "Fixture_C", "Fixture_D", "Fixture_E")
    };

    private static JsonObject CreateTroop() => new()
    {
        ["id"] = 1,
        ["members"] = new JsonArray(new JsonObject { ["enemyId"] = 1, ["x"] = 400, ["y"] = 280, ["hidden"] = false }),
        ["name"] = "Fixture Troop",
        ["pages"] = new JsonArray(new JsonObject
        {
            ["conditions"] = new JsonObject
            {
                ["actorHp"] = 50, ["actorId"] = 1, ["actorValid"] = false,
                ["enemyHp"] = 50, ["enemyIndex"] = 0, ["enemyValid"] = false,
                ["switchId"] = 1, ["switchValid"] = false,
                ["turnA"] = 0, ["turnB"] = 0, ["turnEnding"] = false, ["turnValid"] = false
            },
            ["list"] = new JsonArray(End()), ["span"] = 0
        })
    };

    private static JsonObject CreateWeapon() => new()
    {
        ["id"] = 1, ["animationId"] = 0, ["description"] = "Synthetic weapon", ["etypeId"] = 1,
        ["traits"] = new JsonArray(), ["iconIndex"] = 0, ["name"] = "Fixture Weapon",
        ["note"] = "<PlayromaFixture: weapon>", ["params"] = new JsonArray(0, 0, 1, 0, 0, 0, 0, 0),
        ["price"] = 10, ["wtypeId"] = 1
    };

    private static JsonObject CreateSystem()
    {
        JsonObject audio = new() { ["name"] = "", ["pan"] = 0, ["pitch"] = 100, ["volume"] = 90 };
        JsonObject vehicle = new() { ["bgm"] = audio.DeepClone(), ["characterIndex"] = 0, ["characterName"] = "", ["startMapId"] = 0, ["startX"] = 0, ["startY"] = 0 };
        JsonArray sounds = [];
        for (int index = 0; index < 24; index++) sounds.Add(audio.DeepClone());
        JsonArray attackMotions = [];
        for (int index = 0; index < 13; index++) attackMotions.Add(new JsonObject { ["type"] = 0, ["weaponImageId"] = index });

        return new JsonObject
        {
            ["airship"] = vehicle.DeepClone(), ["armorTypes"] = new JsonArray("", "Fixture Armor"),
            ["attackMotions"] = attackMotions, ["battleBgm"] = audio.DeepClone(), ["battleback1Name"] = "",
            ["battleback2Name"] = "", ["battlerHue"] = 0, ["battlerName"] = "", ["boat"] = vehicle.DeepClone(),
            ["currencyUnit"] = "Gold", ["defeatMe"] = audio.DeepClone(), ["editMapId"] = 1,
            ["elements"] = new JsonArray("", "Normal"), ["equipTypes"] = new JsonArray("", "Weapon", "Shield", "Head", "Body", "Accessory"),
            ["gameTitle"] = "Playroma Synthetic Fixture", ["gameoverMe"] = audio.DeepClone(), ["locale"] = "en_US",
            ["magicSkills"] = new JsonArray(1), ["menuCommands"] = new JsonArray(true, true, true, true, true, true),
            ["optDisplayTp"] = true, ["optDrawTitle"] = true, ["optExtraExp"] = true, ["optFloorDeath"] = false,
            ["optFollowers"] = true, ["optSideView"] = false, ["optSlipDeath"] = false, ["optTransparent"] = false,
            ["partyMembers"] = new JsonArray(1), ["ship"] = vehicle.DeepClone(), ["skillTypes"] = new JsonArray("", "Skills"),
            ["sounds"] = sounds, ["startMapId"] = 1, ["startX"] = 1, ["startY"] = 1,
            ["switches"] = new JsonArray("", "Fixture Switch 1", "Fixture Switch 2"),
            ["terms"] = new JsonObject
            {
                ["basic"] = new JsonArray("Level", "Lv", "HP", "HP", "MP", "MP", "TP", "TP", "EXP", "EXP"),
                ["commands"] = new JsonArray("Fight", "Escape", "Attack", "Guard", "Item", "Skill", "Equip", "Status", "Formation", "Save", "Game End", "Options"),
                ["params"] = new JsonArray("Max HP", "Max MP", "Attack", "Defense", "M.Attack", "M.Defense", "Agility", "Luck", "Hit", "Evasion"),
                ["messages"] = new JsonObject()
            },
            ["testBattlers"] = new JsonArray(), ["testTroopId"] = 1, ["title1Name"] = "", ["title2Name"] = "",
            ["titleBgm"] = audio.DeepClone(), ["variables"] = new JsonArray("", "Fixture Variable"), ["versionId"] = 1,
            ["victoryMe"] = audio.DeepClone(), ["weaponTypes"] = new JsonArray("", "Fixture Weapon"),
            ["windowTone"] = new JsonArray(0, 0, 0, 0)
        };
    }

    private static JsonObject CreateMapInfo(int id, string name, int parentId, int order) => new()
    {
        ["id"] = id, ["expanded"] = false, ["name"] = name, ["order"] = order,
        ["parentId"] = parentId, ["scrollX"] = 0, ["scrollY"] = 0
    };

    private static JsonObject CreateMap(int width, int height, JsonArray data) => new()
    {
        ["autoplayBgm"] = false, ["autoplayBgs"] = false, ["battleback1Name"] = "", ["battleback2Name"] = "",
        ["bgm"] = new JsonObject { ["name"] = "", ["pan"] = 0, ["pitch"] = 100, ["volume"] = 90 },
        ["bgs"] = new JsonObject { ["name"] = "", ["pan"] = 0, ["pitch"] = 100, ["volume"] = 90 },
        ["disableDashing"] = false, ["displayName"] = "Synthetic Fixture", ["encounterList"] = new JsonArray(),
        ["encounterStep"] = 30, ["height"] = height, ["note"] = "<PlayromaFixture: map>",
        ["parallaxLoopX"] = false, ["parallaxLoopY"] = false, ["parallaxName"] = "", ["parallaxShow"] = true,
        ["parallaxSx"] = 0, ["parallaxSy"] = 0, ["scrollType"] = 0, ["specifyBattleback"] = false,
        ["tilesetId"] = 1, ["width"] = width, ["data"] = data, ["events"] = new JsonArray((JsonNode?)null)
    };

    private static JsonObject CreateEventPage() => new()
    {
        ["conditions"] = new JsonObject
        {
            ["actorId"] = 1, ["actorValid"] = false, ["itemId"] = 1, ["itemValid"] = false,
            ["selfSwitchCh"] = "A", ["selfSwitchValid"] = false, ["switch1Id"] = 1, ["switch1Valid"] = false,
            ["switch2Id"] = 2, ["switch2Valid"] = false, ["variableId"] = 1, ["variableValid"] = false,
            ["variableValue"] = 1
        },
        ["directionFix"] = false,
        ["image"] = new JsonObject { ["characterIndex"] = 0, ["characterName"] = "", ["direction"] = 2, ["pattern"] = 1, ["tileId"] = 0 },
        ["list"] = new JsonArray(End()), ["moveFrequency"] = 3,
        ["moveRoute"] = CreateMovementRoute([Route(0)]), ["moveSpeed"] = 3, ["moveType"] = 0,
        ["priorityType"] = 1, ["stepAnime"] = false, ["through"] = false, ["trigger"] = 0, ["walkAnime"] = true
    };

    private static JsonObject Command(int code, int indent, JsonArray parameters) => new()
    {
        ["code"] = code, ["indent"] = indent, ["parameters"] = parameters
    };

    private static JsonObject End(int indent = 0) => Command(0, indent, []);

    private static JsonObject Route(int code, JsonArray? parameters = null) => new()
    {
        ["code"] = code, ["parameters"] = parameters ?? new JsonArray()
    };

    private static JsonObject CreateMovementRoute(JsonArray list) => new()
    {
        ["list"] = list, ["repeat"] = false, ["skippable"] = true, ["wait"] = true
    };

    private static void CreateProjectFolders(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "js", "plugins"));
    }

    private static void WriteProjectMarker(string root)
    {
        File.WriteAllText(Path.Combine(root, "Game.rpgproject"), "RPGMV 1.6.2\n", new UTF8Encoding(false));
    }

    private static void WriteManifest(string outputRoot)
    {
        JsonArray files = [];
        foreach (string file in Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
                     .Where(path => !path.EndsWith("fixture-manifest.json", StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(Path.GetFileName(path), "README.md", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(outputRoot, file).Replace('\\', '/');
            string extension = Path.GetExtension(file);
            string hash;
            if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    hash = JsonUtilities.SemanticSha256(file);
                }
                catch (JsonException)
                {
                    hash = JsonUtilities.RawSha256(file);
                }
            }
            else
            {
                hash = JsonUtilities.RawSha256(file);
            }
            files.Add(new JsonObject { ["path"] = relative, ["sha256"] = hash });
        }

        JsonUtilities.Write(Path.Combine(outputRoot, "fixture-manifest.json"), new JsonObject
        {
            ["schemaVersion"] = 1,
            ["hashMode"] = "Semantic JSON hash; raw SHA-256 for non-JSON files",
            ["files"] = files
        });
    }

    private static Dictionary<string, string> ReadManifest(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty("files").EnumerateArray().ToDictionary(
            item => item.GetProperty("path").GetString()!,
            item => item.GetProperty("sha256").GetString()!,
            StringComparer.Ordinal);
    }

    private static readonly string[] StandardArrayFiles =
    [
        "Actors.json", "Animations.json", "Armors.json", "Classes.json", "CommonEvents.json",
        "Enemies.json", "Items.json", "Skills.json", "States.json", "Tilesets.json", "Troops.json", "Weapons.json"
    ];

    private const string PluginFixtureSource = """
/*:
 * @plugindesc Synthetic Playroma parameter fixture. Contains no game logic.
 * @author Playroma
 *
 * @param BooleanLike
 * @type boolean
 * @default true
 *
 * @param IntegerLike
 * @type number
 * @default 42
 *
 * @param ArrayString
 * @type string[]
 * @default ["A","B"]
 *
 * @help
 * This file exists only to test metadata and parameter preservation.
 */
(function() {
    'use strict';
}());
""";
}

public sealed record FixtureCheckResult(bool Success, IReadOnlyList<string> Differences);
