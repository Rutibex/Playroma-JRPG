using Playroma.Sound.Core.Random;
using Playroma.Sound.Generators.Building;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Generators.Profiles;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Generators;

public sealed class InterfaceSoundGenerator : ISoundGenerator<InterfaceSoundDescription>
{
    public const string Id = "playroma.ui";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Interface Sound", SoundGeneratorCategory.Interface,
        "Clear, compact tonal and noisy feedback for menus, notifications, resources, and achievements.",
        .025, 1.5, ["ui", "notification", "tonal", "short"]);

    public SoundGraphDefinition Generate(InterfaceSoundDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        var random = context.CreateRandom("ui.structure");
        float durationControl = Semantic.Clamp01(description.Duration);
        double duration = Semantic.Lerp(.045, .72, durationControl);
        double baseFrequency = Semantic.Exp(190, 1_750, description.Brightness);
        baseFrequency *= KindPitch(description.Kind);
        int noteCount = Math.Clamp(description.NoteCount, 1, Math.Min(4, context.Budget.MaximumSecondaryEvents + 2));
        double direction = IsDescending(description.Kind) ? -1 : 1;
        double interval = Semantic.Lerp(1.08, 1.5, description.Importance);

        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"ui.{description.Kind.ToString().ToLowerInvariant()}", $"{description.Kind} UI Sound");
        var layers = new List<string>();
        string? primary = null;

        for (int i = 0; i < noteCount; i++)
        {
            double exponent = direction > 0 ? i : noteCount - 1 - i;
            double frequency = baseFrequency * Math.Pow(interval, exponent);
            frequency *= 1 + (GeneratorMath.Signed(ref random) * .025);
            double noteDuration = duration * Semantic.Lerp(.28, .64, 1f - (i / (float)Math.Max(1, noteCount)));
            string note = LayerBuilders.AddTonalBody(builder, $"note-{i + 1}", Waveform(description.Technology),
                frequency, Semantic.Lerp(.18, .54, description.Importance), noteDuration, .0015, noteDuration * .65);
            if (i > 0)
            {
                note = LayerBuilders.AddDelayed(builder, note, $"note-{i + 1}-offset", duration * .22 * i);
            }

            primary ??= note;
            layers.Add(note);
        }

        if (description.TonalBalance < .95f)
        {
            string click = LayerBuilders.AddNoiseBurst(builder, "click", "White",
                Semantic.Lerp(.04, .34, 1 - description.TonalBalance),
                Math.Min(.055, duration * .22),
                Semantic.Lerp(1_100, 8_000, description.Brightness), 1.1, .0001, .9);
            layers.Add(click);
        }

        string mix = builder.AddMixer("ui-mix", layers, Semantic.Lerp(.55, .88, description.Importance));
        string current = mix;
        if (description.Technology > .7f && context.QualityMode != SoundQualityMode.Draft)
        {
            string crusher = builder.AddNode("digital-character", "dsp.bitcrusher",
                ("bitDepth", Semantic.Lerp(11, 20, 1 - description.Technology)),
                ("sampleRateReduction", Semantic.Lerp(1, 3.5, description.Technology)),
                ("mix", Semantic.Lerp(.05, .32, description.Technology)));
            builder.Connect(current, crusher);
            current = crusher;
        }

        current = builder.AddEnvironment(current, AcousticEnvironment.SmallRoom, 0, .35f + (.25f * description.Importance));
        current = builder.AddProtection(current, -.9);

        if (primary is not null)
        {
            builder.AddMacro("brightness", "Brightness", description.Brightness,
                GeneratedGraphBuilder.Binding(primary, "frequencyHz", (float)(baseFrequency * .58), (float)(baseFrequency * 1.85), MacroMappingCurve.Logarithmic));
            builder.AddMacro("importance", "Importance", description.Importance,
                GeneratedGraphBuilder.Binding(primary, "amplitude", .12f, .78f, MacroMappingCurve.SmoothStep));
        }

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["kind"] = description.Kind.ToString(),
            ["duration"] = description.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    private static double KindPitch(InterfaceSoundKind kind) => kind switch
    {
        InterfaceSoundKind.Error => .62,
        InterfaceSoundKind.Warning => .78,
        InterfaceSoundKind.Cancel or InterfaceSoundKind.ResourceLost => .72,
        InterfaceSoundKind.Achievement => 1.22,
        InterfaceSoundKind.Success or InterfaceSoundKind.ResourceGained => 1.12,
        InterfaceSoundKind.Hover => 1.38,
        _ => 1
    };

    private static bool IsDescending(InterfaceSoundKind kind) => kind is InterfaceSoundKind.Cancel or InterfaceSoundKind.Close or InterfaceSoundKind.Error or InterfaceSoundKind.ResourceLost;
    private static string Waveform(float technology) => technology switch { < .3f => "Sine", < .7f => "Triangle", _ => "Pulse" };
}

public sealed class ImpactGenerator : ISoundGenerator<ImpactDescription>
{
    public const string Id = "playroma.impact";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Material Impact", SoundGeneratorCategory.Impact,
        "Layered two-material contacts from taps to heavy collisions, with resonances, debris, breakage, and environment.",
        .035, 8, ["impact", "material", "collision", "variation"]);

    public SoundGraphDefinition Generate(ImpactDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        SoundMaterialProfile a = MaterialProfiles.Get(description.MaterialA);
        SoundMaterialProfile b = MaterialProfiles.Get(description.MaterialB);
        var random = context.CreateRandom("impact.layers");
        float mass = Semantic.Clamp01(description.Mass);
        float velocity = Semantic.Clamp01(description.Velocity);
        float breakage = Semantic.Clamp01(description.Breakage);
        double energy = .12 + (mass * .42) + (velocity * .52);
        double duration = Semantic.Lerp(.055, 1.25, MathF.Sqrt(mass * velocity));
        double baseFrequency = Math.Sqrt(a.FundamentalHz * b.FundamentalHz) * Semantic.Lerp(1.45, .42, mass);

        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"impact.{description.MaterialA.ToString().ToLowerInvariant()}-{description.MaterialB.ToString().ToLowerInvariant()}",
            $"{description.MaterialA} on {description.MaterialB} Impact");
        var layers = new List<string>();

        double sharpness = (a.TransientSharpness + b.TransientSharpness) * .5;
        string transient = LayerBuilders.AddNoiseBurst(builder, "contact", "White",
            Math.Clamp(energy * (.28 + sharpness * .48), .04, 1.3),
            Semantic.Lerp(.018, .11, 1 - velocity),
            Semantic.Lerp(850, 9_500, (float)sharpness),
            Semantic.Lerp(.5, 2.4, description.ContactArea));
        layers.Add(transient);

        string body = LayerBuilders.AddTonalBody(builder, "mass-body", "Sine", baseFrequency,
            Math.Clamp(energy * .62, .05, 1.2), duration, .001, duration * .78);
        layers.Add(body);

        int resonances = Math.Clamp(context.Budget.MaximumResonances, 1, context.QualityMode switch
        {
            SoundQualityMode.Draft => 1,
            SoundQualityMode.RealtimeLow => 2,
            SoundQualityMode.RealtimeHigh => 4,
            _ => 6
        });
        double spread = (a.ResonanceSpread + b.ResonanceSpread) * .5;
        double damping = (a.Damping + b.Damping) * .5;
        for (int i = 0; i < resonances; i++)
        {
            double ratio = 1.7 + (i * (1.1 + spread)) + GeneratorMath.Range(ref random, -.14, .14);
            double ringDuration = duration * Semantic.Lerp(.35, 1.25, (float)(1 - damping));
            string ring = LayerBuilders.AddTonalBody(builder, $"resonance-{i + 1}", i % 2 == 0 ? "Sine" : "Triangle",
                baseFrequency * ratio, energy * (.22 / (i + 1)), ringDuration, .0008, ringDuration * .86);
            layers.Add(ring);
        }

        double roughness = (a.Roughness + b.Roughness) * .5;
        if (roughness > .2 || breakage > .15f)
        {
            string debris = LayerBuilders.AddNoiseBurst(builder, "debris", roughness > .7 ? "Brown" : "Pink",
                energy * (.12 + breakage * .42), duration * Semantic.Lerp(.25, .9, breakage),
                Semantic.Lerp(280, 4_200, (float)((a.DebrisCharacter + b.DebrisCharacter) * .5)), .55);
            debris = LayerBuilders.AddDelayed(builder, debris, "debris-offset", .008 + GeneratorMath.Range(ref random, 0, .035));
            layers.Add(debris);
        }

        string mix = builder.AddMixer("impact-mix", layers, Math.Clamp(.74 - (description.Distance * .22), .42, .82));
        string current = mix;
        if (velocity > .48f)
        {
            string shape = builder.AddNode("contact-nonlinearity", "dsp.waveshaper",
                ("mode", "HyperbolicTangent"),
                ("driveDecibels", Semantic.Lerp(1, 12, velocity)),
                ("bias", 0),
                ("mix", Semantic.Lerp(.12, .58, velocity)),
                ("outputDecibels", -1.5));
            builder.Connect(current, shape);
            current = shape;
        }

        current = builder.AddEnvironment(current, description.Environment, description.Distance, .85f);
        current = builder.AddProtection(current, -.7);
        builder.AddMacro("force", "Force", velocity,
            GeneratedGraphBuilder.Binding(body, "amplitude", .12f, 1.3f, MacroMappingCurve.SmoothStep));
        builder.AddMacro("mass", "Mass", mass,
            GeneratedGraphBuilder.Binding(body, "frequencyHz", (float)(baseFrequency * 1.6), (float)(baseFrequency * .48), MacroMappingCurve.Inverted));

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["materialA"] = description.MaterialA.ToString(),
            ["materialB"] = description.MaterialB.ToString(),
            ["environment"] = description.Environment.ToString()
        });
    }
}

internal static class DescriptorFactory
{
    public static SoundGeneratorDescriptor Create(string id, string name, SoundGeneratorCategory category, string description, double minDuration, double maxDuration, IReadOnlyList<string> tags) =>
        new(id, name, category, description, Parameters(category), tags, minDuration, maxDuration);

    private static IReadOnlyList<GeneratorParameterDescriptor> Parameters(SoundGeneratorCategory category) => category switch
    {
        SoundGeneratorCategory.Interface =>
        [
            N("importance", "Importance", .5), N("friendliness", "Friendliness", .7), N("urgency", "Urgency", .2),
            N("technology", "Technology", .5), N("tonalBalance", "Tonal Balance", .8), N("duration", "Duration", .3),
            N("brightness", "Brightness", .65), new("noteCount", "Note Count", GeneratorParameterKind.Number, 2, 1, 4, "notes")
        ],
        SoundGeneratorCategory.Impact =>
        [
            N("mass", "Mass", .6), N("velocity", "Velocity", .6), N("contactArea", "Contact Area", .45),
            N("glancing", "Glancing Angle", .1), N("breakage", "Breakage", .1), N("distance", "Distance", .2)
        ],
        SoundGeneratorCategory.Explosion =>
        [
            N("size", "Size", .65), N("containment", "Containment", .2), N("distance", "Distance", .25),
            N("debris", "Debris", .45), N("sharpness", "Sharpness", .55),
            new("secondaryEvents", "Secondary Events", GeneratorParameterKind.Number, 2, 0, 8, "events")
        ],
        SoundGeneratorCategory.Weapon =>
        [
            N("power", "Power", .6), N("size", "Size", .5), N("mechanicalComplexity", "Mechanical Complexity", .4),
            N("technology", "Technology", .5), N("distance", "Distance", .15), N("tail", "Tail", .35)
        ],
        SoundGeneratorCategory.Footstep =>
        [
            N("weight", "Weight", .5), N("speed", "Speed", .45), N("wetness", "Wetness", 0),
            N("looseDebris", "Loose Debris", .1), new("leftFoot", "Left Foot", GeneratorParameterKind.Boolean)
        ],
        SoundGeneratorCategory.Magic =>
        [
            N("power", "Power", .6), N("stability", "Stability", .55), N("benevolence", "Benevolence", .5),
            N("organicTechnology", "Organic / Technology", .35), N("castDuration", "Cast Duration", .45),
            N("impactSize", "Impact Size", .45), N("spatialExtent", "Spatial Extent", .5)
        ],
        SoundGeneratorCategory.Retro =>
        [
            N("pitch", "Pitch", .55), N("duration", "Duration", .35), N("brightness", "Brightness", .7),
            N("dirt", "Dirt", .25), new("notes", "Notes", GeneratorParameterKind.Number, 3, 1, 4, "notes")
        ],
        SoundGeneratorCategory.Creature =>
        [
            N("size", "Size", .55), N("throatLength", "Throat Length", .55), N("aggression", "Aggression", .45),
            N("fear", "Fear", 0), N("pain", "Pain", 0), N("organicSynthetic", "Organic / Synthetic", .15),
            N("duration", "Duration", .5)
        ],
        SoundGeneratorCategory.CreatureCry =>
        [
            N("size", "Size", .45), N("cuteness", "Cuteness", .55), N("menace", "Menace", .25),
            N("energy", "Energy", .7), N("intelligence", "Intelligence", .5),
            N("organicSynthetic", "Organic / Synthetic", .25), N("musicalAnimal", "Musical / Animal", .55),
            N("smoothRaspy", "Smooth / Raspy", .35), N("pitchMovement", "Pitch Movement", .75),
            new("syllables", "Syllables", GeneratorParameterKind.Number, 3, 1, 16, "syllables"),
            N("repetition", "Repetition", .2), N("duration", "Duration", .35), N("retroModern", "Retro / Modern", .65)
        ],
        SoundGeneratorCategory.ClassicHandheldCry =>
        [
            N("pitch", "Pitch", .58), N("length", "Length", .35), N("bodySize", "Body Size", .4),
            new("syllables", "Syllables", GeneratorParameterKind.Number, 6, 2, 16, "syllables"),
            N("complexity", "Ornament Complexity", .55), N("sweep", "Pitch Range", .65),
            N("pulseBlend", "Second Pulse", .6), N("noise", "LFSR Noise", .32),
            N("roughness", "Roughness", .35), N("repetition", "Repetition", .15), N("punch", "Punch", .72)
        ],
        SoundGeneratorCategory.Melodic =>
        [
            N("duration", "Duration", .4), N("complexity", "Complexity", .45), N("speed", "Speed", .6),
            N("pitch", "Pitch", .55), N("pitchRange", "Pitch Range", .5), N("harmony", "Harmony", .25),
            N("tonalDissonant", "Tonal / Dissonant", .15), N("acousticSynthetic", "Acoustic / Synthetic", .55),
            N("brightness", "Brightness", .65), N("repetition", "Repetition", .1), N("tail", "Tail", .35),
            new("notes", "Notes", GeneratorParameterKind.Number, 6, 1, 24, "notes")
        ],
        SoundGeneratorCategory.ComplexMagic =>
        [
            N("power", "Power", .65), N("stability", "Stability", .55), N("benevolence", "Benevolence", .5),
            N("organicTechnology", "Organic / Technology", .35), N("duration", "Duration", .45),
            N("musicality", "Musicality", .55), N("anticipation", "Anticipation", .35), N("charge", "Charge", .75),
            N("motif", "Motif", .65), N("release", "Release", .75), N("impact", "Impact", .65),
            N("afterglow", "Afterglow", .55), new("motifNotes", "Motif Notes", GeneratorParameterKind.Number, 7, 1, 24, "notes"),
            N("spatialExtent", "Spatial Extent", .65)
        ],
        SoundGeneratorCategory.CreatureAction => Universal(),
        SoundGeneratorCategory.CombatSequence => [.. Universal(), N("mechanicalDetail", "Mechanical Detail", .45)],
        SoundGeneratorCategory.Destruction => [.. Universal(), N("debris", "Debris", .55), new("secondaryEvents", "Secondary Events", GeneratorParameterKind.Number, 3, 0, 24, "events")],
        SoundGeneratorCategory.MaterialFoley => [.. Universal(), N("contactArea", "Contact Area", .45)],
        SoundGeneratorCategory.Movement => [.. Universal(), new("steps", "Steps / Events", GeneratorParameterKind.Number, 4, 1, 24, "events"), N("wetness", "Wetness", 0), N("equipment", "Equipment Noise", .2)],
        SoundGeneratorCategory.Liquid => [.. Universal(), N("viscosity", "Viscosity", .2), N("containerResonance", "Container Resonance", .25)],
        SoundGeneratorCategory.ElementalEnergy => [.. Universal(), N("instability", "Instability", .35), N("tail", "Tail", .3)],
        SoundGeneratorCategory.Environment => [.. Universal(), N("density", "Density", .5), N("evolution", "Evolution", .4)],
        SoundGeneratorCategory.Machine => [.. Universal(), N("speed", "Speed", .5), N("load", "Load", .5), N("malfunction", "Malfunction", 0)],
        SoundGeneratorCategory.Vehicle => [.. Universal(), N("speed", "Speed", .5), N("engineLoad", "Engine Load", .55)],
        SoundGeneratorCategory.ScienceFiction => [.. Universal(), N("technology", "Technology", .8), N("alienness", "Alienness", .35)],
        SoundGeneratorCategory.Horror => [.. Universal(), N("dread", "Dread", .65), N("supernatural", "Supernatural", .55)],
        SoundGeneratorCategory.Cartoon => [.. Universal(), N("exaggeration", "Exaggeration", .8), N("elasticity", "Elasticity", .6)],
        SoundGeneratorCategory.Household => [.. Universal(), N("age", "Age / Wear", .3), N("force", "Force", .5)],
        SoundGeneratorCategory.Signal => [.. Universal(), N("urgency", "Urgency", .5), N("repetition", "Repetition", .45)],
        SoundGeneratorCategory.CrowdSports => [.. Universal(), N("population", "Population", .6), N("excitement", "Excitement", .45)],
        SoundGeneratorCategory.Transformation => [.. Universal(), N("magnitude", "Magnitude", .65), N("mysticalTechnological", "Mystical / Technological", .5)],
        SoundGeneratorCategory.AbstractSynth => [.. Universal(), N("pitch", "Pitch", .55), N("modulation", "Modulation", .45), N("spread", "Spread", .25)],
        SoundGeneratorCategory.SoundSequence => [.. Universal(), new("events", "Events", GeneratorParameterKind.Number, 5, 2, 32, "events"), N("rhythm", "Rhythm", .5), N("repetition", "Repetition", .2)],
        _ => [N("intensity", "Intensity", .5)]
    };

    private static IReadOnlyList<GeneratorParameterDescriptor> Universal() =>
    [
        N("intensity", "Intensity", .6), N("duration", "Duration", .35), N("size", "Size", .5),
        N("brightness", "Brightness", .55), N("roughness", "Roughness", .25), N("tonalNoise", "Tonal / Noise", .55),
        N("organicSynthetic", "Organic / Synthetic", .4), N("complexity", "Complexity", .5),
        N("movement", "Movement", .35), N("distance", "Distance", .1), N("damage", "Damage / Corruption", 0)
    ];

    private static GeneratorParameterDescriptor N(string id, string name, double value) =>
        new(id, name, GeneratorParameterKind.Number, value, 0, 1);
}

