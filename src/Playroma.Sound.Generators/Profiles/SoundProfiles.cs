namespace Playroma.Sound.Generators.Profiles;

public enum SoundMaterial
{
    Wood, Stone, Concrete, Glass, Ceramic, Steel, Iron, Aluminium, Brass, Plastic,
    Rubber, Leather, Cloth, Flesh, Bone, Ice, Dirt, Sand, Water, Energy
}

public enum AcousticEnvironment
{
    OpenField, SmallRoom, DomesticRoom, Corridor, StoneChamber, Cave, Warehouse,
    IndustrialHall, Forest, UrbanStreet, Underwater, SyntheticVacuum
}

public sealed record SoundMaterialProfile(
    SoundMaterial Material,
    double FundamentalHz,
    double ResonanceSpread,
    double Damping,
    double TransientSharpness,
    double HighFrequencyContent,
    double Roughness,
    double DeformationNoise,
    double BreakageTendency,
    double DebrisCharacter,
    double MassResponse);

public sealed record AcousticEnvironmentProfile(
    AcousticEnvironment Environment,
    double PreDelayMilliseconds,
    double RoomSize,
    double Damping,
    double Width,
    double Wet,
    double DistanceLowPassHz,
    double TailSeconds,
    double SlapDelayMilliseconds = 0);

public static class MaterialProfiles
{
    private static readonly Dictionary<SoundMaterial, SoundMaterialProfile> Profiles =
        new Dictionary<SoundMaterial, SoundMaterialProfile>
        {
            [SoundMaterial.Wood] = P(SoundMaterial.Wood, 230, .48, .56, .58, .52, .55, .34, .35, .45, .55),
            [SoundMaterial.Stone] = P(SoundMaterial.Stone, 145, .32, .72, .75, .58, .62, .28, .5, .6, .84),
            [SoundMaterial.Concrete] = P(SoundMaterial.Concrete, 105, .26, .82, .7, .45, .75, .4, .55, .72, .9),
            [SoundMaterial.Glass] = P(SoundMaterial.Glass, 1220, .82, .2, .96, .96, .28, .18, .95, .92, .25),
            [SoundMaterial.Ceramic] = P(SoundMaterial.Ceramic, 760, .62, .36, .9, .82, .34, .25, .88, .82, .38),
            [SoundMaterial.Steel] = P(SoundMaterial.Steel, 480, .74, .2, .9, .88, .42, .2, .44, .65, .92),
            [SoundMaterial.Iron] = P(SoundMaterial.Iron, 330, .58, .32, .84, .72, .5, .28, .4, .62, .96),
            [SoundMaterial.Aluminium] = P(SoundMaterial.Aluminium, 690, .78, .24, .88, .9, .3, .16, .48, .58, .45),
            [SoundMaterial.Brass] = P(SoundMaterial.Brass, 560, .68, .22, .82, .78, .32, .18, .42, .55, .66),
            [SoundMaterial.Plastic] = P(SoundMaterial.Plastic, 360, .4, .68, .62, .48, .4, .48, .42, .35, .35),
            [SoundMaterial.Rubber] = P(SoundMaterial.Rubber, 95, .18, .92, .3, .18, .5, .82, .15, .18, .48),
            [SoundMaterial.Leather] = P(SoundMaterial.Leather, 145, .22, .84, .4, .28, .62, .72, .2, .25, .45),
            [SoundMaterial.Cloth] = P(SoundMaterial.Cloth, 180, .12, .96, .2, .34, .78, .8, .05, .12, .18),
            [SoundMaterial.Flesh] = P(SoundMaterial.Flesh, 82, .15, .9, .38, .2, .58, .9, .12, .22, .52),
            [SoundMaterial.Bone] = P(SoundMaterial.Bone, 520, .56, .38, .84, .72, .4, .25, .72, .68, .68),
            [SoundMaterial.Ice] = P(SoundMaterial.Ice, 880, .72, .32, .9, .86, .38, .25, .9, .86, .42),
            [SoundMaterial.Dirt] = P(SoundMaterial.Dirt, 72, .12, .96, .25, .14, .88, .9, .1, .72, .65),
            [SoundMaterial.Sand] = P(SoundMaterial.Sand, 110, .08, .98, .22, .3, .96, .92, .08, .88, .5),
            [SoundMaterial.Water] = P(SoundMaterial.Water, 210, .34, .66, .35, .5, .44, .9, .05, .72, .34),
            [SoundMaterial.Energy] = P(SoundMaterial.Energy, 980, .92, .1, .98, .98, .15, .35, .25, .4, .2)
        };

    public static SoundMaterialProfile Get(SoundMaterial material) => Profiles[material];
    public static IReadOnlyCollection<SoundMaterialProfile> All => Profiles.Values;

    private static SoundMaterialProfile P(SoundMaterial m, double f, double s, double d, double t, double h, double r, double n, double b, double debris, double mass) =>
        new(m, f, s, d, t, h, r, n, b, debris, mass);
}

public static class EnvironmentProfiles
{
    private static readonly Dictionary<AcousticEnvironment, AcousticEnvironmentProfile> Profiles =
        new Dictionary<AcousticEnvironment, AcousticEnvironmentProfile>
        {
            [AcousticEnvironment.OpenField] = P(AcousticEnvironment.OpenField, 2, .16, .78, .75, .06, 18_000, .8),
            [AcousticEnvironment.SmallRoom] = P(AcousticEnvironment.SmallRoom, 5, .32, .62, .7, .16, 15_000, 1.4, 65),
            [AcousticEnvironment.DomesticRoom] = P(AcousticEnvironment.DomesticRoom, 8, .42, .58, .78, .18, 14_500, 1.8, 90),
            [AcousticEnvironment.Corridor] = P(AcousticEnvironment.Corridor, 12, .58, .5, .58, .26, 13_000, 2.8, 145),
            [AcousticEnvironment.StoneChamber] = P(AcousticEnvironment.StoneChamber, 18, .78, .34, .9, .36, 12_500, 4.8, 180),
            [AcousticEnvironment.Cave] = P(AcousticEnvironment.Cave, 26, .9, .48, .92, .42, 10_000, 6.5, 260),
            [AcousticEnvironment.Warehouse] = P(AcousticEnvironment.Warehouse, 22, .72, .42, .88, .33, 13_000, 4.2, 210),
            [AcousticEnvironment.IndustrialHall] = P(AcousticEnvironment.IndustrialHall, 28, .84, .34, .86, .38, 12_000, 5.4, 240),
            [AcousticEnvironment.Forest] = P(AcousticEnvironment.Forest, 9, .34, .82, .94, .12, 11_000, 1.6, 115),
            [AcousticEnvironment.UrbanStreet] = P(AcousticEnvironment.UrbanStreet, 16, .56, .55, .82, .22, 12_500, 2.7, 175),
            [AcousticEnvironment.Underwater] = P(AcousticEnvironment.Underwater, 4, .5, .9, .35, .28, 2_200, 3.8, 80),
            [AcousticEnvironment.SyntheticVacuum] = P(AcousticEnvironment.SyntheticVacuum, 0, .12, .95, 1, .03, 20_000, .3)
        };

    public static AcousticEnvironmentProfile Get(AcousticEnvironment environment) => Profiles[environment];
    public static IReadOnlyCollection<AcousticEnvironmentProfile> All => Profiles.Values;

    private static AcousticEnvironmentProfile P(AcousticEnvironment e, double pre, double room, double damping, double width, double wet, double cutoff, double tail, double slap = 0) =>
        new(e, pre, room, damping, width, wet, cutoff, tail, slap);
}
