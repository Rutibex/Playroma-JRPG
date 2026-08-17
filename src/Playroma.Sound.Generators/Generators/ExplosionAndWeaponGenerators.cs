using Playroma.Sound.Generators.Building;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Generators.Profiles;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Generators;

public sealed class ExplosionGenerator : ISoundGenerator<ExplosionDescription>
{
    public const string Id = "playroma.explosion";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Explosion", SoundGeneratorCategory.Explosion,
        "Layered pressure events with crack, shock, turbulence, debris, secondary bursts, and environmental scale.",
        .12, 14, ["explosion", "pressure", "debris", "energy"]);

    public SoundGraphDefinition Generate(ExplosionDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        var random = context.CreateRandom("explosion.layers");
        float size = Semantic.Clamp01(description.Size);
        float sharp = Semantic.Clamp01(description.Sharpness);
        float containment = Semantic.Clamp01(description.Containment);
        float debris = Semantic.Clamp01(description.Debris);
        double duration = Semantic.Lerp(.22, 3.8, size);
        double baseFrequency = Semantic.Lerp(115, 28, size);
        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"explosion.{description.EnergyType.ToString().ToLowerInvariant()}", $"{description.EnergyType} Explosion");
        var layers = new List<string>();

        string crack = LayerBuilders.AddNoiseBurst(builder, "initial-crack", "White",
            Semantic.Lerp(.3, 1.1, sharp), Semantic.Lerp(.018, .09, 1 - sharp),
            Semantic.Lerp(1_200, 8_500, sharp), .7 + (containment * 1.8));
        layers.Add(crack);

        string pressure = LayerBuilders.AddNoiseBurst(builder, "pressure", "Brown",
            Semantic.Lerp(.28, 1.2, size), duration * .62,
            Semantic.Lerp(85, 520, 1 - size), .42, .0005, .92);
        layers.Add(pressure);

        string shock = LayerBuilders.AddTonalBody(builder, "sub-shock", "Sine", baseFrequency,
            Semantic.Lerp(.18, .92, size), duration * .72, .0005, duration * .65);
        layers.Add(shock);

        string turbulence = LayerBuilders.AddNoiseBurst(builder, "turbulence", "Pink",
            Semantic.Lerp(.12, .62, size), duration,
            Semantic.Lerp(340, 2_200, EnergyBrightness(description.EnergyType)), .45, .008, .88);
        if (context.QualityMode != SoundQualityMode.Draft)
        {
            turbulence = LayerBuilders.AddDelayed(builder, turbulence, "turbulence-offset", .018 + (size * .025));
        }
        layers.Add(turbulence);

        if (debris > .08f && context.QualityMode != SoundQualityMode.Draft)
        {
            string rubble = LayerBuilders.AddNoiseBurst(builder, "debris", debris > .65 ? "Brown" : "White",
                .08 + (debris * .48), duration * (.35 + debris * .55),
                Semantic.Lerp(650, 5_400, 1 - size), .65);
            rubble = LayerBuilders.AddDelayed(builder, rubble, "debris-offset", .04 + GeneratorMath.Range(ref random, 0, .09));
            layers.Add(rubble);
        }

        int secondaryCount = context.QualityMode == SoundQualityMode.Draft
            ? 0
            : Math.Clamp(description.SecondaryEvents, 0, context.Budget.MaximumSecondaryEvents);
        for (int i = 0; i < secondaryCount; i++)
        {
            double secondaryDuration = duration * GeneratorMath.Range(ref random, .08, .22);
            string secondary = LayerBuilders.AddNoiseBurst(builder, $"secondary-{i + 1}", "White",
                GeneratorMath.Range(ref random, .08, .28) * (0.5 + size), secondaryDuration,
                GeneratorMath.Range(ref random, 900, 4_800), GeneratorMath.Range(ref random, .5, 1.8));
            secondary = LayerBuilders.AddDelayed(builder, secondary, $"secondary-{i + 1}-offset",
                duration * GeneratorMath.Range(ref random, .12, .78));
            layers.Add(secondary);
        }

        if (description.EnergyType is ExplosionEnergyType.Plasma or ExplosionEnergyType.Electrical or ExplosionEnergyType.Magical)
        {
            string energy = LayerBuilders.AddTonalBody(builder, "energy-tail",
                description.EnergyType == ExplosionEnergyType.Electrical ? "Pulse" : "Saw",
                Semantic.Lerp(180, 1_400, EnergyBrightness(description.EnergyType)), .22 + (size * .24),
                duration * .8, .003, duration * .72);
            if (context.QualityMode == SoundQualityMode.Draft)
            {
                layers.Add(energy);
            }
            else
            {
                string chorus = builder.AddNode("energy-instability", "dsp.chorus",
                    ("baseDelayMilliseconds", 6), ("depthMilliseconds", 3.2),
                    ("rateHz", Semantic.Lerp(.3, 4.5, sharp)), ("feedback", .16), ("wet", .42), ("dry", .85));
                builder.Connect(energy, chorus);
                chorus = LayerBuilders.AddDelayed(builder, chorus, "energy-offset", .012);
                layers.Add(chorus);
            }
        }

        string mix = builder.AddMixer("explosion-mix", layers, Math.Clamp(.66 - (description.Distance * .2), .38, .75));
        string current = mix;
        string saturation = builder.AddNode("pressure-saturation", "dsp.waveshaper",
            ("mode", "HyperbolicTangent"), ("driveDecibels", Semantic.Lerp(4, 16, size)),
            ("bias", 0), ("mix", Semantic.Lerp(.28, .75, containment)), ("outputDecibels", -2.5));
        builder.Connect(current, saturation);
        current = saturation;
        current = builder.AddEnvironment(current, description.Environment, description.Distance, 1);
        current = builder.AddProtection(current, -.6);

        builder.AddMacro("size", "Size", size,
            GeneratedGraphBuilder.Binding(shock, "frequencyHz", 130, 24, MacroMappingCurve.Inverted),
            GeneratedGraphBuilder.Binding(shock, "amplitude", .12f, 1.05f, MacroMappingCurve.SmoothStep));
        builder.AddMacro("distance", "Distance", description.Distance,
            GeneratedGraphBuilder.Binding(mix, "gain", .85f, .32f, MacroMappingCurve.Inverted));

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["energyType"] = description.EnergyType.ToString(),
            ["environment"] = description.Environment.ToString(),
            ["secondaryEvents"] = secondaryCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    private static float EnergyBrightness(ExplosionEnergyType type) => type switch
    {
        ExplosionEnergyType.Electrical => .95f,
        ExplosionEnergyType.Plasma => .8f,
        ExplosionEnergyType.Magical => .7f,
        ExplosionEnergyType.Steam => .38f,
        ExplosionEnergyType.Implosion => .18f,
        _ => .48f
    };
}

public sealed class WeaponGenerator : ISoundGenerator<WeaponDescription>
{
    public const string Id = "playroma.weapon";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Weapon Effect", SoundGeneratorCategory.Weapon,
        "Fictional projectile, energy, launcher, bow, melee, ricochet, dry-fire, and reload effects.",
        .035, 9, ["weapon", "fictional", "mechanical", "energy"]);

    public SoundGraphDefinition Generate(WeaponDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        var random = context.CreateRandom("weapon.layers");
        float power = Semantic.Clamp01(description.Power);
        float size = Semantic.Clamp01(description.Size);
        float mechanism = Semantic.Clamp01(description.MechanicalComplexity);
        double duration = Semantic.Lerp(.08, 1.25, Math.Max(power, size));
        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"weapon.{description.Mechanism.ToString().ToLowerInvariant()}", $"{description.Mechanism} Effect");
        var layers = new List<string>();

        switch (description.Mechanism)
        {
            case WeaponMechanism.ProjectileFirearm:
                layers.Add(LayerBuilders.AddNoiseBurst(builder, "muzzle-crack", "White", .35 + power * .8,
                    .018 + ((1 - power) * .035), Semantic.Lerp(1_300, 8_200, power), .7));
                layers.Add(LayerBuilders.AddTonalBody(builder, "pressure-body", "Sine", Semantic.Lerp(150, 52, size),
                    .18 + power * .62, duration * .42, .0005, duration * .35));
                break;

            case WeaponMechanism.EnergyWeapon:
                string beam = LayerBuilders.AddTonalBody(builder, "discharge", "Saw", Semantic.Lerp(330, 2_200, description.Technology),
                    .28 + power * .48, duration * .55, .001, duration * .44);
                string beamShape = builder.AddNode("energy-shape", "dsp.waveshaper", ("mode", "WaveFold"),
                    ("driveDecibels", 4 + power * 14), ("bias", 0), ("mix", .48), ("outputDecibels", -3));
                builder.Connect(beam, beamShape);
                layers.Add(beamShape);
                layers.Add(LayerBuilders.AddNoiseBurst(builder, "arc", "Blue", .12 + power * .34, duration * .36,
                    Semantic.Lerp(2_000, 10_000, description.Technology), 1.6));
                break;

            case WeaponMechanism.Bow:
            case WeaponMechanism.Crossbow:
                layers.Add(LayerBuilders.AddTonalBody(builder, "string-release", "Triangle",
                    description.Mechanism == WeaponMechanism.Crossbow ? 165 : 230, .24 + power * .35, .18, .0005, .15));
                layers.Add(LayerBuilders.AddNoiseBurst(builder, "air-snap", "White", .12 + power * .22, .08, 2_800, 1.2));
                break;

            case WeaponMechanism.MeleeSwing:
                layers.Add(AddWhoosh(builder, power, size, duration));
                break;

            case WeaponMechanism.MeleeStrike:
                layers.Add(LayerBuilders.AddNoiseBurst(builder, "strike", "White", .25 + power * .66, .055,
                    Semantic.Lerp(800, 4_600, 1 - size), .8));
                layers.Add(LayerBuilders.AddTonalBody(builder, "blade-ring", "Triangle", Semantic.Lerp(720, 190, size),
                    .15 + power * .28, duration * .6, .0005, duration * .52));
                break;

            case WeaponMechanism.Ricochet:
                layers.Add(LayerBuilders.AddTonalBody(builder, "ricochet", "Sine", Semantic.Lerp(900, 4_200, 1 - size),
                    .24 + power * .35, .38, .0004, .34));
                layers.Add(LayerBuilders.AddNoiseBurst(builder, "ricochet-spark", "White", .16, .06, 6_400, 2.2));
                break;

            case WeaponMechanism.DryFire:
            case WeaponMechanism.Reload:
            case WeaponMechanism.MechanicalLauncher:
                layers.Add(LayerBuilders.AddNoiseBurst(builder, "mechanism", "White", .14 + mechanism * .42,
                    description.Mechanism == WeaponMechanism.Reload ? .28 : .11,
                    Semantic.Lerp(950, 5_600, mechanism), 1.2));
                if (description.Mechanism != WeaponMechanism.DryFire)
                {
                    string clack = LayerBuilders.AddTonalBody(builder, "mechanical-clack", "Triangle", Semantic.Lerp(280, 920, mechanism),
                        .13 + mechanism * .28, .18, .0005, .15);
                    clack = LayerBuilders.AddDelayed(builder, clack, "clack-offset", .07 + GeneratorMath.Range(ref random, 0, .06));
                    layers.Add(clack);
                }
                break;
        }

        if (mechanism > .12f && description.Mechanism is not WeaponMechanism.DryFire and not WeaponMechanism.Reload)
        {
            string action = LayerBuilders.AddNoiseBurst(builder, "action", "White", .06 + mechanism * .24, .12,
                Semantic.Lerp(650, 3_600, mechanism), 1.1);
            action = LayerBuilders.AddDelayed(builder, action, "action-offset", duration * .28);
            layers.Add(action);
        }

        string mix = builder.AddMixer("weapon-mix", layers, Math.Clamp(.72 - description.Distance * .2, .4, .8));
        string current = builder.AddEnvironment(mix, description.Environment, description.Distance, description.Tail);
        current = builder.AddProtection(current, -.7);
        builder.AddMacro("power", "Power", power,
            GeneratedGraphBuilder.Binding(mix, "gain", .35f, .9f, MacroMappingCurve.SmoothStep));
        builder.AddMacro("distance", "Distance", description.Distance,
            GeneratedGraphBuilder.Binding(mix, "gain", .85f, .3f, MacroMappingCurve.Inverted));

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["mechanism"] = description.Mechanism.ToString(),
            ["environment"] = description.Environment.ToString()
        });
    }

    private static string AddWhoosh(GeneratedGraphBuilder builder, float power, float size, double duration) =>
        LayerBuilders.AddNoiseBurst(builder, "whoosh", "Pink", .18 + power * .45,
            Semantic.Lerp(.12, .65, size), Semantic.Lerp(1_700, 430, size), .5, .012, .68);
}
