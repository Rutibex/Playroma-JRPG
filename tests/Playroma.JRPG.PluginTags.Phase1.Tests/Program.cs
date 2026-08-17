using System.Text;
using Playroma.JRPG.PluginTags;

PluginFeatureKey PassiveKey = new("TEST", "passive-states");
PluginFeatureKey AiKey = new("TEST", "ai-priority");
PluginFeatureKey LevelKey = new("TEST", "enemy-level");
PluginFeatureKey SkillOnlyKey = new("TEST", "skill-only");
PluginTagContext ActorContext = new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Actor, "Project", "Actors.json", 1);
PluginTagContext EnemyContext = new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Enemy, "Project", "Enemies.json", 1);

List<(string Name, Action Test)> tests =
[
    ("Catalog and provider registration", CatalogAndProviderRegistration),
    ("Catalog serialization round trip", CatalogSerializationRoundTrip),
    ("Deterministic match priority", DeterministicMatchPriority),
    ("Lossless mixed-text round trip", LosslessMixedTextRoundTrip),
    ("Document coverage invariants", DocumentCoverageInvariants),
    ("Recognized block parsing", RecognizedBlockParsing),
    ("Malformed block recovery", MalformedBlockRecovery),
    ("Unexpected and nested block recovery", UnexpectedAndNestedBlockRecovery),
    ("Canonical rendering", CanonicalRendering),
    ("Editing session synchronization", EditingSessionSynchronization),
    ("Editing commit and revert", EditingCommitAndRevert),
    ("Editing dirty-state notifications", EditingDirtyStateNotifications),
    ("Applicability guards", ApplicabilityGuards),
    ("Built-in validation", BuiltInValidation),
    ("Reference extraction", ReferenceExtraction),
    ("Specialized editor registration", SpecializedEditorRegistration),
    ("Deterministic fuzz round trips", DeterministicFuzzRoundTrips)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + exception);
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} Plugin Tags Phase 1 tests passed.");
return failures == 0 ? 0 : 1;

void CatalogAndProviderRegistration()
{
    PluginTagProviderRegistry registry = new();
    registry.Register(CreateProvider());
    Equal(1, registry.Providers.Count);
    PluginTagCatalog catalog = registry.CreateCatalog();
    Equal(7, catalog.Features.Count);
    True(catalog.TryGetFeature(PassiveKey, out PluginFeatureDefinition passive));
    Equal("Passive States", passive.DisplayName);
    Equal(5, catalog.Query(ActorContext).Count);
    Equal(1, catalog.Query(ActorContext, "passive").Count);

    Throws<PluginTagCatalogException>(() => registry.Register(CreateProvider()));
    registry.Register(CreateProvider(), replace: true);
    True(registry.Unregister("TEST"));
    Equal(0, registry.Providers.Count);
}

void CatalogSerializationRoundTrip()
{
    PluginTagCatalog source = CreateCatalog();
    PluginTagCatalogSerializer serializer = new();
    string json = serializer.Serialize(source);
    PluginTagCatalog restored = serializer.Deserialize(json);
    Equal(source.Providers.Count, restored.Providers.Count);
    Equal(source.Features.Count, restored.Features.Count);
    PluginFeatureDefinition passive = restored.GetRequiredFeature(PassiveKey);
    Equal("Passive States", passive.DisplayName);
    Equal(PluginReferenceKind.State, passive.Arguments.Single().ReferenceKind);

    string note = "Text\r\n<Passive State: 4, 7>";
    PluginTextDocument parsed = new PluginTextParser(restored).Parse(note, ActorContext);
    Equal(note, new PluginTextRenderer().RenderDocument(parsed));
}

void DeterministicMatchPriority()
{
    PluginProviderManifest manifest = new("PRIORITY", "Priority Provider", "RPG Maker MV", "1");
    PluginFeatureDefinition low = new(
        "PRIORITY", "PriorityPlugin", "low", "Low", "Low priority alias.",
        PluginAuthoringSurface.DatabaseNote,
        new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor },
        new PluginSyntaxDefinition(PluginSyntaxKind.Flag, "<Shared>", [new PluginSyntaxPattern("^<Shared>$")]),
        [], MatchPriority: 1);
    PluginFeatureDefinition high = low with { FeatureId = "high", DisplayName = "High", MatchPriority = 100 };
    PluginTagCatalog catalog = new PluginTagCatalogBuilder()
        .AddProvider(manifest)
        .AddFeature(low)
        .AddFeature(high)
        .Build();
    RecognizedTagNode parsed = new PluginTextParser(catalog).Parse("<Shared>", ActorContext).RecognizedTags.Single();
    Equal("high", parsed.Definition.FeatureId);
}

void LosslessMixedTextRoundTrip()
{
    PluginTagCatalog catalog = CreateCatalog();
    string source = "Lore line\r\n<Passive State: 4, 7>\n<Unknown: abc>\r\n\r\nTail without newline";
    PluginTextDocument document = new PluginTextParser(catalog).Parse(source, ActorContext);
    Equal(source, new PluginTextRenderer().RenderDocument(document));
    Equal(source, string.Concat(document.Nodes.Select(node => node.RawText)));
    Equal(1, document.Nodes.OfType<RecognizedTagNode>().Count());
    Equal(1, document.Nodes.OfType<UnknownTagNode>().Count());
    Equal(1, document.Nodes.OfType<BlankLineNode>().Count());
    RecognizedTagNode tag = document.Nodes.OfType<RecognizedTagNode>().Single();
    Equal("4, 7", tag.Values["stateIds"].Text);
    Equal(source.Length, document.Nodes.Sum(node => node.Span.Length));
}

void DocumentCoverageInvariants()
{
    RawTextNode validNode = new(new PluginTextSpan(0, 3), "abc");
    _ = new PluginTextDocument("abc", ActorContext, [validNode]);
    Throws<ArgumentException>(() => new PluginTextDocument("abc", ActorContext, [new RawTextNode(new PluginTextSpan(1, 2), "bc")]));
    Throws<ArgumentException>(() => new PluginTextDocument("abc", ActorContext, [new RawTextNode(new PluginTextSpan(0, 2), "ab")]));
    Throws<ArgumentException>(() => new PluginTextDocument("abc", ActorContext, [new RawTextNode(new PluginTextSpan(0, 3), "xyz")]));
}

void RecognizedBlockParsing()
{
    PluginTagCatalog catalog = CreateCatalog();
    string source = "<AI Priority>\r\nSkill 5, Lowest HP%\r\nSkill 7, Random\r\n</AI Priority>\r\n" +
                    "<Custom Requirement>\nvalue = user.level >= 10;\n</Custom Requirement>";
    PluginTextDocument document = new PluginTextParser(catalog).Parse(source, EnemyContext);
    Equal(source, new PluginTextRenderer().RenderDocument(document));
    ActionSequenceBlockNode ai = document.Nodes.OfType<ActionSequenceBlockNode>().Single();
    True(ai.BodyText.Contains("Skill 5", StringComparison.Ordinal));
    ScriptBlockNode script = document.Nodes.OfType<ScriptBlockNode>().Single();
    True(script.BodyText.Contains("user.level", StringComparison.Ordinal));
}

void MalformedBlockRecovery()
{
    PluginTagCatalog catalog = CreateCatalog();
    string source = "Before\n<AI Priority>\nSkill 5, Random\nAfter";
    PluginTextDocument document = new PluginTextParser(catalog).Parse(source, EnemyContext);
    MalformedTagNode malformed = document.Nodes.OfType<MalformedTagNode>().Single();
    Equal("missing-closing-tag", malformed.IssueCode);
    Equal("<AI Priority>\nSkill 5, Random\nAfter", malformed.RawText);
    Equal(source, new PluginTextRenderer().RenderDocument(document));
    True(new PluginTagValidator().Validate(document, EmptyValidation(EnemyContext)).Any(diagnostic => diagnostic.Code == "PT1001"));
}

void UnexpectedAndNestedBlockRecovery()
{
    PluginTextCatalogTestUnexpectedClosing();
    PluginTextCatalogTestNestedBlock();

    void PluginTextCatalogTestUnexpectedClosing()
    {
        string source = "Before\n</AI Priority>\nAfter";
        PluginTextDocument document = new PluginTextParser(CreateCatalog()).Parse(source, EnemyContext);
        MalformedTagNode malformed = document.Nodes.OfType<MalformedTagNode>().Single();
        Equal("unexpected-closing-tag", malformed.IssueCode);
        Equal(source, new PluginTextRenderer().RenderDocument(document));
    }

    void PluginTextCatalogTestNestedBlock()
    {
        string source = "<AI Priority>\n<AI Priority>\nSkill 5, Random\n</AI Priority>\n</AI Priority>";
        PluginTextDocument document = new PluginTextParser(CreateCatalog()).Parse(source, EnemyContext);
        MalformedTagNode malformed = document.Nodes.OfType<MalformedTagNode>().Single();
        Equal("nested-block", malformed.IssueCode);
        Equal(source, new PluginTextRenderer().RenderDocument(document));
    }
}

void CanonicalRendering()
{
    PluginTagCatalog catalog = CreateCatalog();
    PluginTextRenderer renderer = new();
    PluginFeatureDefinition passive = catalog.GetRequiredFeature(PassiveKey);
    string line = renderer.RenderFeature(passive, Values(("stateIds", "3, 8")));
    Equal("<Passive State: 3, 8>", line);

    PluginFeatureDefinition ai = catalog.GetRequiredFeature(AiKey);
    string block = renderer.RenderFeature(ai, new PluginFeatureValues(body: "Skill 4, Lowest HP%\r\nSkill 7, Random"), "\r\n");
    Equal("<AI Priority>\r\nSkill 4, Lowest HP%\r\nSkill 7, Random\r\n</AI Priority>", block);
    Throws<PluginTagRenderException>(() => renderer.RenderFeature(passive, PluginFeatureValues.Empty));
}

void EditingSessionSynchronization()
{
    PluginTagCatalog catalog = CreateCatalog();
    string source = "Intro\r\n<Passive State: 4>\r\n<Other Author Tag>\r\n";
    PluginTextEditingSession session = new(catalog, ActorContext, source);
    int textEvents = 0;
    int parseEvents = 0;
    session.WorkingTextChanged += (_, _) => textEvents++;
    session.ParsedDocumentChanged += (_, _) => parseEvents++;

    session.UpsertFeature(PassiveKey, Values(("stateIds", "9, 12")));
    Equal("Intro\r\n<Passive State: 9, 12>\r\n<Other Author Tag>\r\n", session.WorkingText);
    True(session.IsDirty);
    Equal(1, textEvents);
    Equal(1, parseEvents);
    Equal("9, 12", session.ParsedDocument.Nodes.OfType<RecognizedTagNode>().Single().Values["stateIds"].Text);

    session.UpsertFeature(LevelKey, Values(("level", "25")));
    True(session.WorkingText.EndsWith("<Enemy Level: 25>", StringComparison.Ordinal));
    Equal(1, session.RemoveFeature(PassiveKey));
    True(!session.WorkingText.Contains("Passive State", StringComparison.Ordinal));
    True(session.WorkingText.Contains("<Other Author Tag>", StringComparison.Ordinal));
}

void EditingCommitAndRevert()
{
    PluginTextEditingSession session = new(CreateCatalog(), ActorContext, "Text");
    session.UpsertFeature(PassiveKey, Values(("stateIds", "4")));
    string changed = session.WorkingText;
    session.Revert();
    Equal("Text", session.WorkingText);
    True(!session.IsDirty);

    session.UpsertFeature(PassiveKey, Values(("stateIds", "7")));
    PluginTextCommit commit = session.Commit();
    True(commit.HasChanges);
    Equal("Text", commit.Before);
    Equal(session.WorkingText, commit.After);
    True(!session.IsDirty);
    Equal(changed.Replace("4", "7", StringComparison.Ordinal), session.WorkingText);
}

void EditingDirtyStateNotifications()
{
    PluginTextEditingSession session = new(CreateCatalog(), ActorContext, "Original");
    int dirtyEvents = 0;
    session.DirtyStateChanged += (_, _) => dirtyEvents++;
    session.ReplaceText("Changed");
    Equal(1, dirtyEvents);
    session.ReplaceText("Changed again");
    Equal(1, dirtyEvents);
    session.Revert();
    Equal(2, dirtyEvents);
    session.ReplaceText("Final");
    Equal(3, dirtyEvents);
    session.Commit();
    Equal(4, dirtyEvents);
}

void ApplicabilityGuards()
{
    PluginTextEditingSession actor = new(CreateCatalog(), ActorContext);
    Throws<PluginTextMutationException>(() => actor.UpsertFeature(SkillOnlyKey, Values(("level", "5"))));
    Throws<PluginTextMutationException>(() => actor.UpsertFeature(AiKey, new PluginFeatureValues(body: "Skill 1, Random")));
    actor.UpsertFeature(PassiveKey, Values(("stateIds", "4")));
    Throws<PluginTextMutationException>(() => actor.UpsertFeature(PassiveKey, Values(("stateIds", "5")), occurrenceIndex: 1));
}

void BuiltInValidation()
{
    PluginTagCatalog catalog = CreateCatalog();
    string source = "<Enemy Level: 200>\n<Enemy Level: 20>\n<Mode A>\n<Mode B>\n<Skill Only: 5>";
    PluginTextDocument document = new PluginTextParser(catalog).Parse(source, ActorContext);
    Dictionary<string, InstalledPluginStatus> installed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TestCore"] = new("TestCore", true, false, "1.0")
    };
    IReadOnlyList<PluginTagDiagnostic> diagnostics = new PluginTagValidator().Validate(
        document,
        new PluginTagValidationContext(ActorContext, installed));
    True(diagnostics.Any(diagnostic => diagnostic.Code == "PT1304"));
    True(diagnostics.Any(diagnostic => diagnostic.Code == "PT1201"));
    True(diagnostics.Any(diagnostic => diagnostic.Code == "PT1202"));
    True(diagnostics.Any(diagnostic => diagnostic.Code == "PT1102"));
    True(diagnostics.Any(diagnostic => diagnostic.Code == "PT1402"));
}

void ReferenceExtraction()
{
    string source = "<Passive State: 4, 7, 12>";
    PluginTextDocument document = new PluginTextParser(CreateCatalog()).Parse(source, ActorContext);
    IReadOnlyList<PluginTagReference> references = new PluginTagReferenceExtractor().Extract(document);
    Equal(3, references.Count);
    SequenceEqual(new[] { 4, 7, 12 }, references.Select(reference => reference.Id));
    True(references.All(reference => reference.Kind == PluginReferenceKind.State));
}

void SpecializedEditorRegistration()
{
    PluginSpecializedEditorRegistry registry = new();
    PluginSpecializedEditorDescriptor descriptor = new(
        "test.ai-priority",
        "AI Priority Editor",
        new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.DatabaseNote },
        new HashSet<PluginFeatureKey> { AiKey });
    registry.Register(descriptor);
    True(registry.TryGet("TEST.AI-PRIORITY", out PluginSpecializedEditorDescriptor found));
    Equal(descriptor, found);
    PluginFeatureDefinition feature = CreateCatalog().GetRequiredFeature(AiKey);
    Equal(descriptor, registry.FindFor(feature));
    Throws<InvalidOperationException>(() => registry.Register(descriptor));
    True(registry.Unregister(descriptor.EditorId));
}

void DeterministicFuzzRoundTrips()
{
    PluginTextParser parser = new(CreateCatalog());
    PluginTextRenderer renderer = new();
    Random random = new(0x504C4159);
    string[] fragments =
    [
        "plain", " ", "\t", "<Unknown Tag>", "</Odd>", "<Passive State: 4>",
        "<AI Priority>", "</AI Priority>", "{ value: 2 }", "Ω", "日本語", "🙂",
        "\n", "\r\n", "\r", ":", "<", ">"
    ];

    for (int iteration = 0; iteration < 1000; iteration++)
    {
        StringBuilder source = new();
        int pieces = random.Next(0, 80);
        for (int index = 0; index < pieces; index++) source.Append(fragments[random.Next(fragments.Length)]);
        string text = source.ToString();
        PluginTextDocument document = parser.Parse(text, ActorContext);
        Equal(text, renderer.RenderDocument(document), "Fuzz round trip failed at iteration " + iteration + ".");
        Equal(text.Length, document.Nodes.Sum(node => node.Span.Length));
    }
}

PluginTagValidationContext EmptyValidation(PluginTagContext context) =>
    new(context, new Dictionary<string, InstalledPluginStatus>(StringComparer.OrdinalIgnoreCase));

PluginTagCatalog CreateCatalog()
{
    PluginTagProviderRegistry registry = new();
    registry.Register(CreateProvider());
    return registry.CreateCatalog();
}

IPluginTagProvider CreateProvider()
{
    PluginProviderManifest manifest = new("TEST", "Test Provider", "RPG Maker MV", "1");
    return new StaticPluginTagProvider(manifest,
    [
        new PluginFeatureDefinition(
            "TEST", "TestCore", "passive-states", "Passive States", "Grant passive states.",
            PluginAuthoringSurface.DatabaseNote,
            new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor, PluginDocumentKind.Class, PluginDocumentKind.Enemy },
            new PluginSyntaxDefinition(
                PluginSyntaxKind.KeyValue,
                "<Passive State: {stateIds}>",
                [new PluginSyntaxPattern("^<Passive\\s+State:\\s*(?<stateIds>[0-9, \\-]+)>$")]),
            [new PluginArgumentDefinition("stateIds", "States", PluginArgumentKind.DatabaseIdList, true, ReferenceKind: PluginReferenceKind.State, AllowMultiple: true)],
            RequiredPlugins: ["TestCore"]),

        new PluginFeatureDefinition(
            "TEST", "TestBattleAi", "ai-priority", "AI Priority", "Ordered enemy AI rules.",
            PluginAuthoringSurface.DatabaseNote,
            new HashSet<PluginDocumentKind> { PluginDocumentKind.Enemy },
            new PluginSyntaxDefinition(
                PluginSyntaxKind.OrderedBlock,
                "<AI Priority>",
                [new PluginSyntaxPattern("^<AI\\s+Priority>$")],
                "</AI Priority>",
                [new PluginSyntaxPattern("^</AI\\s+Priority>$")]),
            [],
            SpecializedEditorId: "test.ai-priority",
            Complexity: PluginFeatureComplexity.Specialized),

        new PluginFeatureDefinition(
            "TEST", "TestCore", "custom-requirement", "Custom Requirement", "JavaScript requirement.",
            PluginAuthoringSurface.DatabaseNote,
            new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor, PluginDocumentKind.Enemy },
            new PluginSyntaxDefinition(
                PluginSyntaxKind.ScriptBlock,
                "<Custom Requirement>",
                [new PluginSyntaxPattern("^<Custom\\s+Requirement>$")],
                "</Custom Requirement>",
                [new PluginSyntaxPattern("^</Custom\\s+Requirement>$")]),
            [],
            Complexity: PluginFeatureComplexity.AdvancedCode),

        new PluginFeatureDefinition(
            "TEST", "TestCore", "enemy-level", "Enemy Level", "Set a level.",
            PluginAuthoringSurface.DatabaseNote,
            new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor, PluginDocumentKind.Enemy },
            new PluginSyntaxDefinition(
                PluginSyntaxKind.KeyValue,
                "<Enemy Level: {level}>",
                [new PluginSyntaxPattern("^<Enemy\\s+Level:\\s*(?<level>-?[0-9]+)>$")]),
            [new PluginArgumentDefinition("level", "Level", PluginArgumentKind.Integer, true, 1, 99)],
            PluginFeatureMultiplicity.Singleton,
            RequiredPlugins: ["TestCore"]),

        new PluginFeatureDefinition(
            "TEST", "TestCore", "mode-a", "Mode A", "First exclusive mode.",
            PluginAuthoringSurface.DatabaseNote,
            new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor },
            new PluginSyntaxDefinition(PluginSyntaxKind.Flag, "<Mode A>", [new PluginSyntaxPattern("^<Mode\\s+A>$")]),
            [],
            ConflictGroups: ["test-mode"]),

        new PluginFeatureDefinition(
            "TEST", "TestCore", "mode-b", "Mode B", "Second exclusive mode.",
            PluginAuthoringSurface.DatabaseNote,
            new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor },
            new PluginSyntaxDefinition(PluginSyntaxKind.Flag, "<Mode B>", [new PluginSyntaxPattern("^<Mode\\s+B>$")]),
            [],
            ConflictGroups: ["test-mode"]),

        new PluginFeatureDefinition(
            "TEST", "TestCore", "skill-only", "Skill Only", "Only valid on skills.",
            PluginAuthoringSurface.DatabaseNote,
            new HashSet<PluginDocumentKind> { PluginDocumentKind.Skill },
            new PluginSyntaxDefinition(
                PluginSyntaxKind.KeyValue,
                "<Skill Only: {level}>",
                [new PluginSyntaxPattern("^<Skill\\s+Only:\\s*(?<level>[0-9]+)>$")]),
            [new PluginArgumentDefinition("level", "Level", PluginArgumentKind.Integer, true, 1, 99)])
    ]);
}

PluginFeatureValues Values(params (string Name, string Value)[] pairs) =>
    PluginFeatureValues.FromStrings(pairs.Select(pair => new KeyValuePair<string, string>(pair.Name, pair.Value)));

static void True(bool condition, string? message = null)
{
    if (!condition) throw new InvalidOperationException(message ?? "Expected condition to be true.");
}

static void Equal<T>(T expected, T actual, string? message = null)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(message ?? $"Expected '{expected}', got '{actual}'.");
}

static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException("Sequences were not equal.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException("Expected exception " + typeof(TException).Name + ".");
}
