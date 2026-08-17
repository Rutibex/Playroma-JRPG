namespace Playroma.Tile.Materials.Models;

public enum MaterialDataType { Color, Scalar, Vector2, Mask, Integer, Palette, Curve }
public enum MaterialChannel { BaseColor, Alpha, Height, Roughness, Emission, Normal, MaterialId, DecorationProbability, Wear, Moisture, FlowX, FlowY }
public enum MaterialQuality { Draft, Interactive, Final }
public enum TileabilitySupport { None, Preserved, NativePeriodic, RequiresRepair }
public enum MaterialNodeCategory { Sources, Noise, Patterns, Transform, Filter, Simulation, Synthesis, PixelArt, Output }
public enum MaterialParameterKind { String, Integer, Float, Boolean, Color, Enum, FilePath }
public enum BlendMode { Normal, Multiply, Screen, Overlay, SoftLight, HardLight, Add, Subtract, Difference, Darken, Lighten, Minimum, Maximum, HeightBlend }
public enum NoiseKind { Value, Perlin, Cellular, Fbm, Turbulence, Billow, Ridged, DomainWarped }
public enum PatternKind { Checker, Stripes, Bricks, RunningBond, Herringbone, Planks, FloorTiles, Shingles, Cobblestone, Flagstone, Hexagons, Grate, RivetedPanels, Circuit, Cloth, Dots, Waves, Dunes, Sediment, Cracks, Veins }
public enum FilterKind { BoxBlur, GaussianBlur, DirectionalBlur, Median, Sharpen, Emboss, Sobel, HighPass, LowPass, LocalContrast }
public enum MorphologyKind { Dilate, Erode, Open, Close, Outline, Bevel, Slope, Curvature, AmbientOcclusion }
public enum DitherMode { None, Bayer2, Bayer4, Bayer8, FloydSteinberg, Atkinson, SierraLite, BlueNoise }
public enum MaterialCategory { NaturalGround, Stone, Wood, Manufactured, Fluid, Organic, Fantasy, ScienceFiction, Custom }
public enum MaterialStyle { ClassicRpgMaker, SoftPaintedFantasy, SharpPixelArt, SnesLike, GameBoyColor, DarkDungeon, CleanScienceFiction, HandInked, PastelStorybook, HighContrastHorror }
public enum MaterialCondition { Clean, Worn, Ancient, Cracked, Wet, Dry, Mossy, Rusted, SnowCovered, Burned, Corrupted, Overgrown }
public enum GrowthKind { CellularAutomata, DiffusionLimitedAggregation, Branching, Crystal, Fungal, Roots }
public enum MaterialAdapterMode { UniformFloor, TransparentDecoration, HardEdgeTerrain, SoftEdgeTerrain, Shoreline, Cliff, Roof, Wall, AnimatedWater, Waterfall }
public enum ResampleMode { Nearest, Box, Bilinear, Bicubic, Lanczos3, MajorityColor }
public enum PaletteMode { Supplied, MedianCut, KMeans, Octree }
