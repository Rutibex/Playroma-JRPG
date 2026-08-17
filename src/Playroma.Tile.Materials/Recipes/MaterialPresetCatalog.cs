using System.Drawing;
using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Recipes;

public sealed record MaterialPresetDescriptor(string Name, MaterialCategory Category, string Description, bool Animated = false);

public sealed class MaterialPresetCatalog
{
    private readonly MaterialNodeRegistry _registry;
    private static readonly MaterialPresetDescriptor[] Presets =
    {
        new("Grass",MaterialCategory.NaturalGround,"Patchy grass with fine organic variation."),
        new("Dry Grass",MaterialCategory.NaturalGround,"Sun-bleached grass and sparse soil."),
        new("Dirt",MaterialCategory.NaturalGround,"Layered soil with mottled compacted areas."),
        new("Mud",MaterialCategory.NaturalGround,"Wet, dark earth with pooled variation."),
        new("Sand",MaterialCategory.NaturalGround,"Soft dunes and granular variation."),
        new("Snow",MaterialCategory.NaturalGround,"Cool snow with wind-shaped ridges."),
        new("Ash",MaterialCategory.NaturalGround,"Fine gray ash and darker deposits."),
        new("Moss",MaterialCategory.NaturalGround,"Dense organic moss with raised patches."),
        new("Cobblestone",MaterialCategory.Stone,"Irregular rounded stones and deep mortar."),
        new("Flagstone",MaterialCategory.Stone,"Large irregular paving stones."),
        new("Brick",MaterialCategory.Stone,"Running-bond brickwork with mortar."),
        new("Marble",MaterialCategory.Stone,"Polished stone with branching veins."),
        new("Slate",MaterialCategory.Stone,"Dark layered slate with subtle highlights."),
        new("Crystal",MaterialCategory.Stone,"Angular fantasy crystal surface."),
        new("Wood Planks",MaterialCategory.Wood,"Long boards with grain and seams."),
        new("Rotten Boards",MaterialCategory.Wood,"Damaged damp planks with dark wear."),
        new("Roof Shingles",MaterialCategory.Wood,"Overlapping roof shingles."),
        new("Ceramic Tiles",MaterialCategory.Manufactured,"Clean square tiles and grout."),
        new("Carpet",MaterialCategory.Manufactured,"Soft woven carpet texture."),
        new("Riveted Metal",MaterialCategory.Manufactured,"Industrial plates, seams and rivets."),
        new("Concrete",MaterialCategory.Manufactured,"Poured concrete with aggregate."),
        new("Circuit Floor",MaterialCategory.ScienceFiction,"Glowing traces and panel geometry."),
        new("Laboratory Floor",MaterialCategory.ScienceFiction,"Clean modular science-fiction floor."),
        new("Water",MaterialCategory.Fluid,"Animated clear blue water.",true),
        new("Swamp Water",MaterialCategory.Fluid,"Animated murky organic water.",true),
        new("Lava",MaterialCategory.Fluid,"Animated molten rock with hot cracks.",true),
        new("Acid",MaterialCategory.Fluid,"Animated luminous corrosive liquid.",true),
        new("Magical Energy",MaterialCategory.Fantasy,"Animated arcane energy field.",true),
        new("Reaction Organic",MaterialCategory.Organic,"Reaction-diffusion spots and stripes."),
        new("Corruption",MaterialCategory.Fantasy,"Veined supernatural corruption.")
    };

    public MaterialPresetCatalog(MaterialNodeRegistry? registry=null)=>_registry=registry??new MaterialNodeRegistry();
    public IReadOnlyList<MaterialPresetDescriptor> GetPresets()=>Presets;

    public MaterialRecipe Create(string name,MaterialStyle style=MaterialStyle.ClassicRpgMaker,MaterialCondition condition=MaterialCondition.Clean,int seed=0)
    {
        var preset=Presets.FirstOrDefault(x=>string.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase))??Presets[0];
        var recipe=CreateRecipe(preset,seed);
        ApplyStyle(recipe,style);
        ApplyCondition(recipe,condition);
        return recipe;
    }

    private MaterialRecipe CreateRecipe(MaterialPresetDescriptor preset,int seed)
    {
        var recipe=new MaterialRecipe{Name=preset.Name,Description=preset.Description,Category=preset.Category,Seed=seed,Tags=new(){preset.Category.ToString(),preset.Animated?"Animated":"Static"}};
        var graph=recipe.Graph;
        MaterialNodeInstance Add(string type,float x,float y,string? name=null){var n=_registry.Create(type,x,y);if(name is not null)n.Name=name;graph.Nodes.Add(n);return n;}
        void Link(MaterialNodeInstance from,MaterialNodeInstance to,string input,string port="Value")=>to.Inputs[input]=new MaterialConnection(from.Id,port);
        MaterialNodeInstance source;
        MaterialNodeInstance height;
        if(preset.Name=="Reaction Organic")
        {
            source=Add("Simulation.ReactionDiffusion",40,100,"Organic Pattern");height=source;
            source.Parameters["Feed"]=MaterialParameterValue.From(.0545f);source.Parameters["Kill"]=MaterialParameterValue.From(.062f);
        }
        else if(IsPattern(preset.Name))
        {
            source=Add("Pattern.Procedural",40,100,"Structure");height=source;ConfigurePattern(source,preset.Name);
        }
        else
        {
            source=Add("Noise.Procedural",40,100,"Structure");height=source;ConfigureNoise(source,preset.Name);
        }
        var gradient=Add("Color.GradientMap",280,80,"Palette");Link(source,gradient,"Value",preset.Name=="Cobblestone"?"CellEdge":"Value");gradient.Parameters["Palette"]=MaterialParameterValue.From(PaletteFor(preset.Name));
        MaterialNodeInstance color=gradient;
        if(preset.Name is "Cobblestone" or "Flagstone" or "Brick" or "Wood Planks" or "Rotten Boards" or "Roof Shingles" or "Ceramic Tiles" or "Riveted Metal" or "Circuit Floor" or "Laboratory Floor")
        {
            var shade=Add("Color.HeightShade",500,80,"Surface Lighting");Link(gradient,shade,"Color");Link(height,shade,"Height",preset.Name=="Cobblestone"?"CellEdge":"Value");shade.Parameters["Strength"]=MaterialParameterValue.From(.55f);color=shade;
        }
        var adjust=Add("Color.Adjust",700,80,"Style Finish");Link(color,adjust,"Value");
        var output=Add("Material.Output",920,80,"Output");Link(adjust,output,"BaseColor");Link(height,output,"Height",preset.Name=="Cobblestone"?"CellEdge":"Value");
        if(preset.Category==MaterialCategory.Fluid){var noise2=Add("Noise.Procedural",280,250,"Flow");ConfigureNoise(noise2,preset.Name);noise2.Parameters["Frequency"]=MaterialParameterValue.From(2.5f);Link(noise2,output,"FlowX");Link(source,output,"FlowY");}
        graph.OutputNodeId=output.Id;
        recipe.ExposedParameters.Add(new(){DisplayName="Scale",NodeId=source.Id,ParameterName=source.TypeId=="Pattern.Procedural"?"Scale":"Frequency",Minimum=2,Maximum=32,Step=.25f});
        recipe.ExposedParameters.Add(new(){DisplayName="Irregularity",NodeId=source.Id,ParameterName=source.TypeId=="Pattern.Procedural"?"Irregularity":"Persistence",Minimum=0,Maximum=1,Step=.02f});
        recipe.ExposedParameters.Add(new(){DisplayName="Contrast",NodeId=adjust.Id,ParameterName="Contrast",Minimum=.5f,Maximum=2f,Step=.05f});
        recipe.ExposedParameters.Add(new(){DisplayName="Saturation",NodeId=adjust.Id,ParameterName="Saturation",Minimum=0,Maximum=2f,Step=.05f});
        return recipe;
    }

    private static bool IsPattern(string name)=>name is "Cobblestone" or "Flagstone" or "Brick" or "Wood Planks" or "Rotten Boards" or "Roof Shingles" or "Ceramic Tiles" or "Riveted Metal" or "Circuit Floor" or "Laboratory Floor";
    private static void ConfigurePattern(MaterialNodeInstance node,string name)
    {
        var (kind,scale,gap,irregularity)=name switch
        {
            "Cobblestone"=>(PatternKind.Cobblestone,14f,1.5f,.25f),"Flagstone"=>(PatternKind.Flagstone,22f,1.8f,.18f),"Brick"=>(PatternKind.RunningBond,16f,1.2f,.08f),
            "Wood Planks"=>(PatternKind.Planks,18f,1f,.12f),"Rotten Boards"=>(PatternKind.Planks,18f,1.4f,.35f),"Roof Shingles"=>(PatternKind.Shingles,15f,1f,.1f),
            "Ceramic Tiles"=>(PatternKind.FloorTiles,16f,1.1f,.02f),"Riveted Metal"=>(PatternKind.RivetedPanels,24f,1.2f,.04f),"Circuit Floor"=>(PatternKind.Circuit,14f,.8f,.08f),
            _=>(PatternKind.FloorTiles,24f,1f,.03f)
        };
        node.Parameters["Kind"]=MaterialParameterValue.FromEnum(kind);node.Parameters["Scale"]=MaterialParameterValue.From(scale);node.Parameters["Gap"]=MaterialParameterValue.From(gap);node.Parameters["Irregularity"]=MaterialParameterValue.From(irregularity);
    }
    private static void ConfigureNoise(MaterialNodeInstance node,string name)
    {
        var (kind,freq,oct,persist)=name switch
        {
            "Grass"=>(NoiseKind.Fbm,12f,5,.55f),"Dry Grass"=>(NoiseKind.Ridged,11f,5,.48f),"Dirt"=>(NoiseKind.Fbm,7f,5,.55f),"Mud"=>(NoiseKind.Billow,5f,4,.6f),
            "Sand"=>(NoiseKind.DomainWarped,8f,4,.45f),"Snow"=>(NoiseKind.Ridged,4f,4,.45f),"Ash"=>(NoiseKind.Fbm,9f,6,.6f),"Moss"=>(NoiseKind.Cellular,9f,4,.55f),
            "Marble"=>(NoiseKind.DomainWarped,3.5f,6,.55f),"Slate"=>(NoiseKind.Ridged,5f,4,.45f),"Crystal"=>(NoiseKind.Cellular,7f,4,.45f),"Carpet"=>(NoiseKind.Fbm,20f,3,.35f),
            "Concrete"=>(NoiseKind.Fbm,15f,5,.55f),"Water"=>(NoiseKind.DomainWarped,4f,5,.55f),"Swamp Water"=>(NoiseKind.Billow,5f,5,.6f),"Lava"=>(NoiseKind.Ridged,5f,6,.6f),
            "Acid"=>(NoiseKind.Cellular,6f,4,.55f),"Magical Energy"=>(NoiseKind.DomainWarped,6f,6,.62f),"Corruption"=>(NoiseKind.Ridged,7f,6,.6f),_=>(NoiseKind.Fbm,8f,5,.5f)
        };
        node.Parameters["Kind"]=MaterialParameterValue.FromEnum(kind);node.Parameters["Frequency"]=MaterialParameterValue.From(freq);node.Parameters["Octaves"]=MaterialParameterValue.From(oct);node.Parameters["Persistence"]=MaterialParameterValue.From(persist);
    }
    private static string PaletteFor(string name)=>name switch
    {
        "Grass"=>"#19351c,#315e2b,#538a3c,#83b85a,#b4d77a","Dry Grass"=>"#3a3420,#75633b,#a78c4c,#d0b96e","Dirt"=>"#2b1c14,#5a3826,#80553b,#aa7a52",
        "Mud"=>"#171611,#30281f,#514334,#71604b","Sand"=>"#6e5633,#a4834e,#d3b26f,#efd59a","Snow"=>"#7e9aaa,#b9ced8,#e5eff2,#ffffff","Ash"=>"#18191a,#343638,#5d6062,#8e9192",
        "Moss"=>"#132313,#294728,#4d7240,#7c9c55","Cobblestone"=>"#272727,#494944,#6e6d64,#9a988d","Flagstone"=>"#30343a,#565c64,#7e8790,#a8b0b5","Brick"=>"#2e1715,#63302b,#984b3f,#c27358",
        "Marble"=>"#23262b,#676d76,#c4c8cd,#f1f0eb","Slate"=>"#151c23,#293745,#45586a,#718293","Crystal"=>"#161b35,#3b4f8d,#6f8fd5,#b6d7ff","Wood Planks"=>"#2a1810,#5c321f,#8d5430,#c07b45",
        "Rotten Boards"=>"#14150f,#303121,#54513a,#71684a","Roof Shingles"=>"#29171b,#5b2d35,#8b4650,#b46266","Ceramic Tiles"=>"#38444c,#7e939e,#c5d6dc,#edf4f3","Carpet"=>"#26152a,#55305c,#854b8a,#b276b5",
        "Riveted Metal"=>"#171b1d,#343b3f,#667176,#9ba7a9","Concrete"=>"#303030,#5b5b58,#85847e,#aaa79d","Circuit Floor"=>"#061619,#0f3439,#1c7374,#65e0c1","Laboratory Floor"=>"#1c2830,#465864,#8295a0,#c5d0d4",
        "Water"=>"#08283d,#0e5f88,#2aa6c7,#8be1e8","Swamp Water"=>"#101d17,#294332,#52694a,#8c9564","Lava"=>"#18080a,#62120d,#d4380e,#ffb62a,#fff18a","Acid"=>"#10250d,#2b6b1d,#75cc35,#c8ff68",
        "Magical Energy"=>"#110d2d,#3f2580,#8a4ad1,#e2a9ff","Reaction Organic"=>"#17101d,#4c274d,#8d5167,#c98b8c","Corruption"=>"#130b19,#3c1646,#771f6e,#c34c9a",_=>"#202020,#707070,#d0d0d0"
    };

    private static void ApplyStyle(MaterialRecipe recipe,MaterialStyle style)
    {
        recipe.StyleProfile.Style=style;recipe.StyleProfile.Name=style.ToString();var adjust=recipe.Graph.Nodes.FirstOrDefault(x=>x.TypeId=="Color.Adjust");if(adjust is null)return;
        var (contrast,saturation,posterize)=style switch
        {
            MaterialStyle.SharpPixelArt=>(1.35f,1.15f,24),MaterialStyle.SnesLike=>(1.2f,1.1f,48),MaterialStyle.GameBoyColor=>(1.35f,.65f,12),MaterialStyle.DarkDungeon=>(1.45f,.75f,32),
            MaterialStyle.CleanScienceFiction=>(1.2f,.9f,48),MaterialStyle.HandInked=>(1.65f,.25f,10),MaterialStyle.PastelStorybook=>(.8f,.7f,48),MaterialStyle.HighContrastHorror=>(1.8f,.55f,16),MaterialStyle.SoftPaintedFantasy=>(.9f,1.05f,96),_=>(1.1f,1f,64)
        };
        adjust.Parameters["Contrast"]=MaterialParameterValue.From(contrast);adjust.Parameters["Saturation"]=MaterialParameterValue.From(saturation);adjust.Parameters["Posterize"]=MaterialParameterValue.From(posterize);recipe.StyleProfile.ColorBudget=posterize;
    }
    private static void ApplyCondition(MaterialRecipe recipe,MaterialCondition condition)
    {
        var adjust=recipe.Graph.Nodes.FirstOrDefault(x=>x.TypeId=="Color.Adjust");var source=recipe.Graph.Nodes.First();if(adjust is null)return;switch(condition)
        {
            case MaterialCondition.Worn:adjust.Parameters["Saturation"]=MaterialParameterValue.From(.75f);source.Parameters["Persistence"]=MaterialParameterValue.From(.68f);break;
            case MaterialCondition.Ancient:adjust.Parameters["Brightness"]=MaterialParameterValue.From(-.12f);adjust.Parameters["Saturation"]=MaterialParameterValue.From(.55f);break;
            case MaterialCondition.Wet:adjust.Parameters["Brightness"]=MaterialParameterValue.From(-.1f);adjust.Parameters["Contrast"]=MaterialParameterValue.From(1.35f);break;
            case MaterialCondition.Dry:adjust.Parameters["Brightness"]=MaterialParameterValue.From(.08f);adjust.Parameters["Saturation"]=MaterialParameterValue.From(.65f);break;
            case MaterialCondition.Burned:adjust.Parameters["Brightness"]=MaterialParameterValue.From(-.25f);adjust.Parameters["Saturation"]=MaterialParameterValue.From(.2f);break;
            case MaterialCondition.Corrupted:adjust.Parameters["Hue"]=MaterialParameterValue.From(45f);adjust.Parameters["Saturation"]=MaterialParameterValue.From(1.4f);break;
            case MaterialCondition.SnowCovered:adjust.Parameters["Brightness"]=MaterialParameterValue.From(.25f);adjust.Parameters["Saturation"]=MaterialParameterValue.From(.5f);break;
            case MaterialCondition.Overgrown:source.Parameters["Persistence"]=MaterialParameterValue.From(.72f);adjust.Parameters["Saturation"]=MaterialParameterValue.From(1.25f);break;
        }
        recipe.Tags.Add(condition.ToString());
    }
}
