using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

List<(string Name, Action Test)> tests =
[
    ("AI conditions parse and render", AiConditionsRoundTrip),
    ("AI multiple conditions round trip", AiMultipleConditionsRoundTrip),
    ("AI rule parsing supports skill IDs", AiRuleSkillId),
    ("AI rule parsing supports skill names", AiRuleSkillName),
    ("AI Eval rules preserve ternary colons", AiRuleEvalTernary),
    ("AI target catalog is complete", AiTargetCatalog),
    ("AI priority model parses settings", AiPriorityParsesSettings),
    ("AI raw lines retain their position", AiRawLineOrder),
    ("AI note replacement preserves unrelated text", AiReplacementPreservesText),
    ("AI validation catches invalid skills", AiValidationCatchesInvalidSkill),
    ("Action phases parse", ActionPhasesParse),
    ("Action entries preserve raw commands", ActionEntriesPreserveRaw),
    ("Action rendering emits five phases", ActionRenderingEmitsPhases),
    ("Action replacement preserves unrelated notes", ActionReplacementPreservesText),
    ("Action Copy parses and renders", ActionCopyRoundTrip),
    ("Action Copy warns about custom phases", ActionCopyWarns),
    ("Action IF validation catches missing END", ActionIfValidation),
    ("Action ELSE validation catches missing IF", ActionElseValidation),
    ("Built-in sequence templates are valid", SequenceTemplatesValid),
    ("YEP command catalog includes all packs", CommandCatalogIncludesPacks),
    ("Command catalog is deduplicated", CommandCatalogIsDeduplicated),
    ("Phase definitions are available", PhaseDefinitionsAvailable),
    ("Phase blocks are parsed by shared parser", PhaseBlocksUseSharedParser),
    ("Specialized editor descriptors register", SpecializedDescriptorsRegister),
    ("Battle AI feature remains available", BattleAiFeatureAvailable),
    ("No YEP JavaScript is embedded", NoJavaScriptEmbedded)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
}
Console.WriteLine($"Phase 6 tests: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;

static PluginTagCatalog Embedded() => new PluginTagCatalogBuilder().AddProvider(YanflyGeneratedCatalogProvider.LoadEmbedded()).Build();

static void AiConditionsRoundTrip()
{
    string[] samples =
    [
        "Always", "Random 50%", "Turn >= 3", "Switch 5 On", "Variable 3 > 10",
        "State === State 5", "State !== Blind", "Element Fire Weakness",
        "Party Alive Members > 2", "Troop Dead Members >= 1", "HP% Param <= 50%",
        "User ATK Param >= 150", "Highest Party Level > 10", "Eval user.hpRate() < 0.5"
    ];
    foreach (string sample in samples)
    {
        string rendered = BattleAiConditionCodec.Render(BattleAiConditionCodec.Parse(sample));
        Assert(!string.IsNullOrWhiteSpace(rendered), "Condition rendered empty: " + sample);
    }
}

static void AiMultipleConditionsRoundTrip()
{
    BattleAiCondition parsed = BattleAiConditionCodec.Parse("State === Blind +++ User HP% Param <= 50%");
    Assert(parsed.Kind == BattleAiConditionKind.Multiple && parsed.EffectiveChildren.Count == 2, "Multiple condition did not parse.");
    Assert(BattleAiConditionCodec.Render(parsed).Contains("+++", StringComparison.Ordinal), "Multiple condition did not render.");
}

static void AiRuleSkillId()
{
    Assert(BattleAiPriorityCodec.TryParseRule("Always: Skill 10, Lowest HP%", out BattleAiRule? rule), "Rule did not parse.");
    Assert(rule!.UsesSkillId && rule.Target == "Lowest HP%", "Skill ID or target was wrong.");
}

static void AiRuleSkillName()
{
    Assert(BattleAiPriorityCodec.TryParseRule("Element Fire Weakness: Fireball, Highest MAT", out BattleAiRule? rule), "Named skill rule did not parse.");
    Assert(rule!.SkillToken == "Fireball" && !rule.UsesSkillId, "Named skill was altered.");
}

static void AiRuleEvalTernary()
{
    Assert(BattleAiPriorityCodec.TryParseRule("Eval user.hpRate() < 0.5 ? true : false: Skill 7, Lowest HP%", out BattleAiRule? rule), "Eval ternary rule did not parse.");
    Assert(rule!.Condition.Kind == BattleAiConditionKind.Eval && rule.Condition.Value.Contains("? true : false", StringComparison.Ordinal), "Eval condition was split at the ternary colon.");
    Assert(rule.SkillToken == "Skill 7" && rule.Target == "Lowest HP%", "Eval action did not parse.");
}

static void AiTargetCatalog()
{
    Assert(BattleAiTargets.All.Count == 35, $"Expected 35 target selectors, found {BattleAiTargets.All.Count}.");
    Assert(BattleAiTargets.All.Contains("Highest HP%") && BattleAiTargets.All.Contains("Lowest Level"), "Target selector range is incomplete.");
}

static void AiPriorityParsesSettings()
{
    string notes = "<AI Level: 80>\n<AI Consider Taunt>\n<AI Priority>\nAlways: Skill 4, Lowest HP%\n</AI Priority>";
    BattleAiPriorityModel model = BattleAiPriorityCodec.Parse(notes);
    Assert(model.AiLevel == 80 && model.ConsiderTaunt && model.Rules.Count == 1, "AI settings did not parse.");
}

static void AiRawLineOrder()
{
    string notes = "<AI Priority>\n// before\nAlways: Skill 1\n// between\nTurn >= 3: Skill 2\n// after\n</AI Priority>";
    BattleAiPriorityModel model = BattleAiPriorityCodec.Parse(notes);
    string rendered = BattleAiPriorityCodec.RenderBlock(model);
    Assert(rendered.IndexOf("// before", StringComparison.Ordinal) < rendered.IndexOf("Always: Skill 1", StringComparison.Ordinal), "Leading raw line moved.");
    Assert(rendered.IndexOf("// between", StringComparison.Ordinal) > rendered.IndexOf("Always: Skill 1", StringComparison.Ordinal), "Middle raw line moved before the first rule.");
    Assert(rendered.IndexOf("// after", StringComparison.Ordinal) > rendered.IndexOf("Turn >= 3: Skill 2", StringComparison.Ordinal), "Trailing raw line moved.");
}

static void AiReplacementPreservesText()
{
    string notes = "Manual prose\r\n<Unknown: Keep>\r\n<AI Level: 20>\r\n";
    BattleAiPriorityModel model = new(90, false, [new(new(BattleAiConditionKind.Always), "Skill 3", "Random")]);
    string result = BattleAiPriorityCodec.ReplaceInNotes(notes, model, "\r\n");
    Assert(result.StartsWith("Manual prose\r\n<Unknown: Keep>", StringComparison.Ordinal), "Unrelated notes changed.");
    Assert(result.Contains("<AI Level: 90>", StringComparison.Ordinal), "AI level was not updated.");
}

static void AiValidationCatchesInvalidSkill()
{
    BattleAiPriorityModel model = new(100, false, [new(new(BattleAiConditionKind.Always), "", "Random")]);
    Assert(BattleAiPriorityCodec.Validate(model).Any(issue => issue.Severity == "Error"), "Missing skill was accepted.");
}

static void ActionPhasesParse()
{
    string notes = "<setup action>\nDISPLAY ACTION\n</setup action>\n<target action>\nACTION EFFECT\n</target action>";
    ActionSequenceModel model = ActionSequenceCodec.Parse(notes);
    Assert(model.Get(ActionSequencePhase.Setup).Entries.Count == 1, "Setup phase did not parse.");
    Assert(model.Get(ActionSequencePhase.Target).Entries.Count == 1, "Target phase did not parse.");
}

static void ActionEntriesPreserveRaw()
{
    ActionSequenceEntry entry = ActionSequenceCodec.ParseEntry("  CUSTOM EXTENSION THING: user, 7  ");
    Assert(entry.Text == "  CUSTOM EXTENSION THING: user, 7  ", "Raw command changed.");
    Assert(ActionSequenceCodec.RenderEntry(entry) == entry.Text, "Raw command did not round trip exactly.");
}

static void ActionRenderingEmitsPhases()
{
    Dictionary<ActionSequencePhase, ActionSequencePhaseModel> phases = new()
    {
        [ActionSequencePhase.Setup] = new(ActionSequencePhase.Setup, [ActionSequenceCodec.ParseEntry("DISPLAY ACTION")]),
        [ActionSequencePhase.Target] = new(ActionSequencePhase.Target, [ActionSequenceCodec.ParseEntry("ACTION EFFECT")])
    };
    string text = ActionSequenceCodec.RenderAll(new(phases));
    Assert(text.Contains("<setup action>", StringComparison.Ordinal) && text.Contains("</target action>", StringComparison.Ordinal), "Phase tags are missing.");
}

static void ActionReplacementPreservesText()
{
    string notes = "Manual\n<Unknown: Keep>\n<setup action>\nOLD\n</setup action>\n";
    Dictionary<ActionSequencePhase, ActionSequencePhaseModel> phases = new()
    {
        [ActionSequencePhase.Setup] = new(ActionSequencePhase.Setup, [ActionSequenceCodec.ParseEntry("DISPLAY ACTION")])
    };
    string result = ActionSequenceCodec.ReplaceSequencesInNotes(notes, new(phases), "\n");
    Assert(result.StartsWith("Manual\n<Unknown: Keep>", StringComparison.Ordinal), "Unrelated notes changed.");
    Assert(!result.Contains("OLD", StringComparison.Ordinal), "Old sequence survived replacement.");
}

static void ActionCopyRoundTrip()
{
    ActionSequenceModel parsed = ActionSequenceCodec.Parse("<action copy: skill:45>");
    Assert(parsed.CopyType == "skill" && parsed.CopyId == 45, "Action Copy did not parse.");
    Assert(ActionSequenceCodec.RenderAll(parsed) == "<action copy: skill:45>", "Action Copy did not render canonically.");
}

static void ActionCopyWarns()
{
    Dictionary<ActionSequencePhase, ActionSequencePhaseModel> phases = new()
    {
        [ActionSequencePhase.Setup] = new(ActionSequencePhase.Setup, [ActionSequenceCodec.ParseEntry("DISPLAY ACTION")])
    };
    Assert(ActionSequenceCodec.Validate(new(phases, "skill", 1)).Any(issue => issue.Severity == "Warning"), "Action Copy priority warning missing.");
}

static void ActionIfValidation()
{
    Dictionary<ActionSequencePhase, ActionSequencePhaseModel> phases = new()
    {
        [ActionSequencePhase.Target] = new(ActionSequencePhase.Target, [ActionSequenceCodec.ParseEntry("IF user.hpRate() < 0.5"), ActionSequenceCodec.ParseEntry("ACTION EFFECT")])
    };
    Assert(ActionSequenceCodec.Validate(new(phases)).Any(issue => issue.Message.Contains("missing END", StringComparison.OrdinalIgnoreCase)), "Missing END was not detected.");
}

static void ActionElseValidation()
{
    Dictionary<ActionSequencePhase, ActionSequencePhaseModel> phases = new()
    {
        [ActionSequencePhase.Target] = new(ActionSequencePhase.Target, [ActionSequenceCodec.ParseEntry("ELSE")])
    };
    Assert(ActionSequenceCodec.Validate(new(phases)).Any(issue => issue.Message.Contains("matching IF", StringComparison.OrdinalIgnoreCase)), "Orphan ELSE was not detected.");
}

static void SequenceTemplatesValid()
{
    Assert(ActionSequenceTemplates.BuiltIn.Count >= 5, "Expected at least five templates.");
    foreach (ActionSequenceTemplate template in ActionSequenceTemplates.BuiltIn)
    {
        Dictionary<ActionSequencePhase, ActionSequencePhaseModel> phases = template.Commands.ToDictionary(pair => pair.Key, pair => new ActionSequencePhaseModel(pair.Key, pair.Value.Select(ActionSequenceCodec.ParseEntry).ToArray()));
        Assert(!ActionSequenceCodec.Validate(new(phases)).Any(issue => issue.Severity == "Error"), "Template is invalid: " + template.DisplayName);
    }
}

static void CommandCatalogIncludesPacks()
{
    IReadOnlyList<ActionSequenceCommandDescriptor> commands = YanflyActionSequenceCommandCatalog.Create(Embedded());
    string[] required = ["YEP_X_ActSeqPack1", "YEP_X_ActSeqPack2", "YEP_X_ActSeqPack3"];
    foreach (string plugin in required) Assert(commands.Any(command => command.PluginId == plugin), "Missing command provider: " + plugin);
    Assert(commands.Count >= 100, $"Expected a broad command catalog, found {commands.Count}.");
}

static void CommandCatalogIsDeduplicated()
{
    IReadOnlyList<ActionSequenceCommandDescriptor> commands = YanflyActionSequenceCommandCatalog.Create(Embedded());
    Assert(commands.Select(command => command.CanonicalLine.Trim().ToUpperInvariant()).Distinct().Count() == commands.Count, "Duplicate canonical commands remain.");
}

static void PhaseDefinitionsAvailable()
{
    PluginTagCatalog catalog = Embedded();
    foreach (ActionSequencePhase phase in Enum.GetValues<ActionSequencePhase>())
        Assert(catalog.Features.Any(feature => feature.FeatureId == "action-sequence.phase." + phase.ToString().ToLowerInvariant()), "Missing phase definition: " + phase);
}

static void PhaseBlocksUseSharedParser()
{
    PluginTagCatalog catalog = Embedded();
    PluginTextDocument document = new PluginTextParser(catalog).Parse("<setup action>\nDISPLAY ACTION\n</setup action>", new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Skill));
    Assert(document.Nodes.OfType<ActionSequenceBlockNode>().Count() == 1, "Shared parser did not create an action-sequence node.");
}

static void SpecializedDescriptorsRegister()
{
    PluginTagCatalog catalog = Embedded();
    PluginSpecializedEditorRegistry registry = new();
    YanflyBattleFeatureDefinitions.RegisterSpecializedEditors(registry, catalog);
    Assert(registry.TryGet(YanflyBattleFeatureDefinitions.BattleAiEditorId, out _), "Battle AI editor descriptor missing.");
    Assert(registry.TryGet(YanflyBattleFeatureDefinitions.ActionSequenceEditorId, out _), "Action Sequence editor descriptor missing.");
}

static void BattleAiFeatureAvailable()
{
    PluginTagCatalog catalog = Embedded();
    Assert(catalog.Features.Any(feature => feature.FeatureId == "battle-ai.priority-block" && feature.SpecializedEditorId == YanflyBattleFeatureDefinitions.BattleAiEditorId), "Battle AI block is not routed to its editor.");
}

static void NoJavaScriptEmbedded()
{
    string[] resources = typeof(YanflyGeneratedCatalogProvider).Assembly.GetManifestResourceNames();
    Assert(!resources.Any(name => name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)), "YEP JavaScript was embedded.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
