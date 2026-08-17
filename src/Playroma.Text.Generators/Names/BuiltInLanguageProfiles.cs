using Playroma.Text.Core.Names;

namespace Playroma.Text.Generators.Names;

public static class BuiltInLanguageProfiles
{
    private static WeightedNameToken[] T(params string[] values) => values.Select(x => new WeightedNameToken(x)).ToArray();
    private static WeightedNameToken[] TW(params (string Text, double Weight)[] values) => values.Select(x => new WeightedNameToken(x.Text, x.Weight)).ToArray();
    private static NameDomainProfile D(int minSyl, int maxSyl, int minLen = 4, int maxLen = 16, double markov = .35,
        string[]? patterns = null, WeightedNameToken[]? prefixes = null, WeightedNameToken[]? suffixes = null,
        double prefixChance = 0, double suffixChance = 0, double compoundChance = 0, string[]? joiners = null)
        => new(minSyl, maxSyl, minLen, maxLen, patterns, prefixes, suffixes, prefixChance, suffixChance, compoundChance, joiners, true, markov);

    public static IReadOnlyList<LanguageProfile> All { get; } = new[]
    {
        Common(), Valestrian(), Northreach(), Sylvan(), Kharic(), Umbral()
    };

    public static LanguageProfile Common() => new(
        "common", "Common Fantasy", null,
        T("b","br","c","d","dr","f","g","gr","h","j","k","l","m","n","p","r","s","st","t","v","w"),
        TW(("a",3),("e",3),("i",2),("o",2),("u",1),("ai",1),("ea",1)),
        TW(("",4),("n",2),("r",2),("s",2),("l",1),("m",1),("th",1),("nd",1),("rk",1)),
        new[] { "ON", "ONC", "NC" },
        new[] { "aaa", "eee", "iii", "ooo", "uuu", "jj", "ww", "kkk" },
        new[] { new OrthographicRewriteRule("aa", "a", .65), new OrthographicRewriteRule("ee", "e", .65), new OrthographicRewriteRule("ii", "i", .65) },
        new Dictionary<NameDomain, NameDomainProfile>
        {
            [NameDomain.PersonGiven] = D(2,3,4,12,.35),
            [NameDomain.PersonFamily] = D(2,3,4,14,.25, suffixes:T("en","er","son","ward","mere"), suffixChance:.22),
            [NameDomain.Settlement] = D(2,3,4,16,.22, suffixes:T("ford","haven","wick","mere","stead","watch"), suffixChance:.58),
            [NameDomain.City] = D(2,4,5,18,.28, suffixes:T("hold","gate","port","ton"), suffixChance:.28),
            [NameDomain.Village] = D(2,3,4,16,.18, suffixes:T("ford","wick","stead","brook"), suffixChance:.5),
            [NameDomain.Country] = D(2,4,5,18,.42, suffixes:T("ia","ara","or","en","eth"), suffixChance:.48),
            [NameDomain.Region] = D(2,4,5,18,.30, suffixes:T("reach","vale","march","lands"), suffixChance:.4),
            [NameDomain.River] = D(2,3,4,16,.25, suffixes:T("water","run","flow"), suffixChance:.16),
            [NameDomain.Mountain] = D(2,3,4,18,.20, suffixes:T("peak","horn","spire"), suffixChance:.3),
            [NameDomain.Forest] = D(2,3,4,18,.18, suffixes:T("wood","weald","grove"), suffixChance:.38),
            [NameDomain.Dungeon] = D(2,3,4,18,.22, suffixes:T("crypt","vault","halls","keep"), suffixChance:.32),
            [NameDomain.Deity] = D(2,4,5,16,.45),
            [NameDomain.Faction] = D(2,4,5,18,.24, prefixes:T("Order of ","House ","Clan "), prefixChance:.18),
            [NameDomain.NobleHouse] = D(2,3,4,16,.22, prefixes:T("House "), prefixChance:.7),
            [NameDomain.Monster] = D(1,3,3,15,.42, suffixes:T("ling","maw","spawn","kin"), suffixChance:.2),
            [NameDomain.Spell] = D(2,4,4,18,.40),
            [NameDomain.Weapon] = D(1,3,3,16,.25, compoundChance:.28, joiners:new[]{""," "}),
            [NameDomain.Armor] = D(1,3,3,16,.22),
            [NameDomain.Artifact] = D(2,4,5,20,.38),
            [NameDomain.Language] = D(2,3,4,14,.38, suffixes:T("ic","ish","an"), suffixChance:.45),
            [NameDomain.Title] = D(1,2,3,12,.1),
            [NameDomain.Custom] = D(2,3)
        },
        new Dictionary<NameDomain, IReadOnlyList<string>>
        {
            [NameDomain.PersonGiven] = new[] { "Alden","Brena","Cael","Darin","Elira","Faren","Galen","Helia","Iren","Joran","Kalen","Lessa","Maren","Neris","Oran","Pella","Rovan","Sera","Taren","Vela" },
            [NameDomain.PersonFamily] = new[] { "Ashmere","Blacken","Crower","Dawnward","Elderan","Farrow","Greymere","Hollan","Ironer","Kingsward","Lightmere","Northen","Oakward","Ravenor","Stoneward","Thorner","Valen","Westmere" },
            [NameDomain.Country] = new[] { "Aldoria","Brenara","Caledor","Dameria","Elaren","Faron","Galenor","Ithara","Noravia","Valedor","Rethia","Saleron" },
            [NameDomain.Settlement] = new[] { "Ashford","Bellhaven","Crowmere","Dunwick","Elderford","Fairwatch","Greybrook","Highstead","Ironwick","Moonford","Oakmere","Ravenwatch","Stonehaven","Westford" },
            [NameDomain.Monster] = new[] { "Barrowkin","Cavemaw","Dreadling","Fenstalker","Grimspawn","Hollowkin","Mireling","Ravener","Stonewhelp","Thornmaw" },
            [NameDomain.Spell] = new[] { "Asteron","Cindera","Frostel","Luminor","Mireth","Novara","Rimecall","Solaren","Umbrial","Vespera" }
        },
        "Balanced neutral fantasy phonology used as the inheritance root.", new HashSet<string>(new[]{"fantasy","balanced"}, StringComparer.OrdinalIgnoreCase));

    public static LanguageProfile Valestrian() => new(
        "valestrian", "Valestrian", "common",
        T("b","c","d","f","l","m","n","r","s","v","ch","fl","gl","ll","pr","tr"),
        TW(("a",3),("e",3),("i",2),("o",3),("u",1),("ia",1),("io",1),("ea",1)),
        TW(("",5),("n",2),("r",2),("s",2),("l",2),("m",1),("nt",1),("re",1)),
        new[] { "ON", "ONC", "ON", "NC" },
        new[] { "kk", "ggg", "zzz", "jj" },
        new[] { new OrthographicRewriteRule("ii", "i", .8), new OrthographicRewriteRule("ae", "e", .35), new OrthographicRewriteRule("lr", "l", .7) },
        new Dictionary<NameDomain, NameDomainProfile>
        {
            [NameDomain.PersonGiven] = D(2,4,4,13,.45, suffixes:T("a","io","en","elle"), suffixChance:.2),
            [NameDomain.PersonFamily] = D(2,4,5,15,.36, suffixes:T("eri","ano","elle","ari"), suffixChance:.32),
            [NameDomain.Country] = D(3,4,6,18,.5, suffixes:T("ia","eria","ora","enza"), suffixChance:.7),
            [NameDomain.City] = D(2,4,5,18,.38, suffixes:T("a","ia","o"), suffixChance:.34),
            [NameDomain.Settlement] = D(2,4,5,17,.32),
            [NameDomain.Deity] = D(3,4,6,17,.52),
            [NameDomain.Language] = D(2,3,5,14,.42, suffixes:T("an","ese","ic"), suffixChance:.7)
        },
        new Dictionary<NameDomain, IReadOnlyList<string>>
        {
            [NameDomain.PersonGiven] = new[] { "Avelia","Beloren","Cirella","Damaris","Eliano","Fiora","Galeno","Lusara","Marcelo","Neria","Orlena","Rovian","Serella","Valerio","Zorena" },
            [NameDomain.PersonFamily] = new[] { "Calderi","Dovano","Ferelli","Lorano","Meravi","Norelli","Ravari","Soleri","Valenti","Virello" },
            [NameDomain.Country] = new[] { "Averia","Bellorena","Calenzia","Damerona","Elaria","Lusoria","Maravia","Norezia","Valestria","Virelia" },
            [NameDomain.City] = new[] { "Avelora","Cerenza","Doralia","Fiorena","Lumerio","Marosia","Nerava","Solena","Valora","Vireza" }
        },
        "Flowing southern courtly names with open vowels and soft liquids.", new HashSet<string>(new[]{"courtly","flowing","southern"}, StringComparer.OrdinalIgnoreCase));

    public static LanguageProfile Northreach() => new(
        "northreach", "Northreach", "common",
        T("b","bj","d","dr","f","fr","g","gr","h","hj","k","kr","r","sk","st","th","v","vr"),
        TW(("a",3),("e",2),("i",2),("o",2),("u",2),("ae",1),("ei",1)),
        TW(("",2),("n",2),("r",2),("s",2),("k",2),("g",1),("ld",1),("nd",2),("rk",2),("rn",2),("st",1)),
        new[] { "ONC", "ON", "ONC" },
        new[] { "aaa", "eee", "thth" },
        new[] { new OrthographicRewriteRule("kk", "k", .65), new OrthographicRewriteRule("rrr", "rr", 1), new OrthographicRewriteRule("ae", "æ", .08) },
        new Dictionary<NameDomain, NameDomainProfile>
        {
            [NameDomain.PersonGiven] = D(1,3,3,11,.46),
            [NameDomain.PersonFamily] = D(2,3,4,14,.32, suffixes:T("son","sen","gard","ulf"), suffixChance:.4),
            [NameDomain.Settlement] = D(1,3,4,16,.26, suffixes:T("heim","vik","gard","holt","fell"), suffixChance:.68),
            [NameDomain.Country] = D(2,3,5,15,.46, suffixes:T("mark","land","heim"), suffixChance:.48),
            [NameDomain.Mountain] = D(1,3,4,16,.2, suffixes:T("fell","horn","skar"), suffixChance:.62),
            [NameDomain.Monster] = D(1,3,3,13,.5),
            [NameDomain.Language] = D(1,3,4,13,.42, suffixes:T("ic","ish"), suffixChance:.62)
        },
        new Dictionary<NameDomain, IReadOnlyList<string>>
        {
            [NameDomain.PersonGiven] = new[] { "Arne","Bren","Dagrun","Eirik","Frena","Gunnar","Hald","Inga","Jorun","Kelda","Rurik","Signe","Torven","Yrsa" },
            [NameDomain.PersonFamily] = new[] { "Arnson","Bergsen","Dalgard","Eriksen","Frostulf","Grimsen","Haldgard","Ravensson","Skorulf","Torvald" },
            [NameDomain.Settlement] = new[] { "Arvik","Bjorheim","Dunholt","Frostgard","Grimvik","Haldheim","Karsfell","Skorheim","Torvik","Vargard" },
            [NameDomain.Country] = new[] { "Ardmark","Bjorland","Dunheim","Frostmark","Grimland","Karsheim","Skorland","Torheim" }
        },
        "Hard northern names with dense codas and compact syllables.", new HashSet<string>(new[]{"northern","hard","compact"}, StringComparer.OrdinalIgnoreCase));

    public static LanguageProfile Sylvan() => new(
        "sylvan", "Sylvan", "common",
        T("","c","el","f","l","m","n","r","s","th","v","y"),
        TW(("a",2),("e",3),("i",3),("o",1),("u",1),("ae",2),("ia",2),("ie",2),("ui",1)),
        TW(("",7),("l",2),("n",2),("r",2),("s",1),("th",1),("iel",1)),
        new[] { "ON", "ON", "ONC", "NC" },
        new[] { "kk", "gg", "bb", "dd", "ttt" },
        new[] { new OrthographicRewriteRule("ii", "i", .6), new OrthographicRewriteRule("aa", "a", .5), new OrthographicRewriteRule("yy", "y", 1) },
        new Dictionary<NameDomain, NameDomainProfile>
        {
            [NameDomain.PersonGiven] = D(2,4,4,14,.5),
            [NameDomain.PersonFamily] = D(2,4,5,16,.42, suffixes:T("iel","wen","rien","thir"), suffixChance:.34),
            [NameDomain.Settlement] = D(2,4,5,18,.38, suffixes:T("iel","lorn","vale","wen"), suffixChance:.3),
            [NameDomain.Country] = D(3,5,6,20,.52),
            [NameDomain.Forest] = D(2,4,5,18,.38, suffixes:T("weald","grove","lorn"), suffixChance:.3),
            [NameDomain.Deity] = D(3,5,6,18,.58),
            [NameDomain.Language] = D(2,4,5,16,.5, suffixes:T("en","iel","ic"), suffixChance:.4)
        },
        new Dictionary<NameDomain, IReadOnlyList<string>>
        {
            [NameDomain.PersonGiven] = new[] { "Aelira","Caeriel","Elarin","Faelwen","Ilyren","Laeriel","Melian","Naevia","Rielan","Saelith","Thalia","Vaerien" },
            [NameDomain.PersonFamily] = new[] { "Aelthir","Caelwen","Elarien","Faeriel","Laerith","Melwen","Naerien","Rielthir","Saelwen","Vaeriel" },
            [NameDomain.Settlement] = new[] { "Aeloria","Caelwen","Elariel","Faelorn","Ilyrien","Laeriel","Naeviel","Rielora","Saelorn","Vaeriel" },
            [NameDomain.Country] = new[] { "Aeloria","Caerithiel","Elarion","Faeloria","Ilyrien","Naevoria","Saelirien","Vaelorien" }
        },
        "Lyrical woodland names with open syllables, liquids, and vowel sequences.", new HashSet<string>(new[]{"sylvan","lyrical","woodland"}, StringComparer.OrdinalIgnoreCase));

    public static LanguageProfile Kharic() => new(
        "kharic", "Kharic", "common",
        T("b","d","g","gr","k","kh","kr","m","r","t","th","v","z"),
        TW(("a",3),("e",1),("i",1),("o",2),("u",3)),
        TW(("",1),("d",2),("g",2),("k",2),("m",1),("n",2),("r",3),("rk",2),("rd",2),("th",1)),
        new[] { "ONC", "ONC", "ON" },
        new[] { "aaa", "eee", "iii", "uuu", "khkh" },
        new[] { new OrthographicRewriteRule("kk", "k", .5), new OrthographicRewriteRule("ggg", "gg", 1) },
        new Dictionary<NameDomain, NameDomainProfile>
        {
            [NameDomain.PersonGiven] = D(1,3,3,11,.42),
            [NameDomain.PersonFamily] = D(1,3,4,13,.28, suffixes:T("dur","grim","kar","rak"), suffixChance:.44),
            [NameDomain.Settlement] = D(1,3,4,14,.2, prefixes:T("Kar","Dur","Kaz"), suffixes:T("dum","grad","rak","hold"), prefixChance:.22, suffixChance:.55),
            [NameDomain.Country] = D(2,3,5,14,.4, suffixes:T("kar","dum","rak"), suffixChance:.5),
            [NameDomain.Mountain] = D(1,3,4,14,.18, suffixes:T("kar","dur","peak"), suffixChance:.42),
            [NameDomain.Weapon] = D(1,2,3,12,.2, suffixes:T("bite","rend","breaker"), suffixChance:.2),
            [NameDomain.Language] = D(1,3,4,12,.36, suffixes:T("ic","an"), suffixChance:.6)
        },
        new Dictionary<NameDomain, IReadOnlyList<string>>
        {
            [NameDomain.PersonGiven] = new[] { "Barek","Durgan","Grom","Kadra","Kharek","Morga","Rudek","Torga","Varek","Zuran" },
            [NameDomain.PersonFamily] = new[] { "Bardur","Durgar","Gromrak","Khardur","Morgar","Rudgrim","Torkar","Vardum","Zorak" },
            [NameDomain.Settlement] = new[] { "Bardum","Durgrad","Gromhold","Karrak","Khazdum","Morgrad","Rudrak","Torhold","Varkar" },
            [NameDomain.Country] = new[] { "Bardukar","Durgarum","Kharadum","Morgarak","Rudakar","Toradum","Varkar" }
        },
        "Dense stone-and-forge naming with short vowels and strong codas.", new HashSet<string>(new[]{"stone","forge","dense"}, StringComparer.OrdinalIgnoreCase));

    public static LanguageProfile Umbral() => new(
        "umbral", "Umbral", "common",
        T("","d","g","k","m","n","r","s","sh","th","v","x","z"),
        TW(("a",2),("e",2),("i",2),("o",2),("u",2),("ae",1),("ei",1),("ou",1)),
        TW(("",3),("n",2),("r",2),("s",2),("th",2),("x",1),("z",1),("sh",1),("rn",1),("ss",1)),
        new[] { "ONC", "ON", "NC", "ONC" },
        new[] { "aaa", "eee", "iii", "ooo", "uuu", "xxz", "zzx" },
        new[] { new OrthographicRewriteRule("sssh", "ssh", 1), new OrthographicRewriteRule("thth", "th", 1), new OrthographicRewriteRule("ae", "aë", .06) },
        new Dictionary<NameDomain, NameDomainProfile>
        {
            [NameDomain.PersonGiven] = D(2,4,4,13,.52),
            [NameDomain.PersonFamily] = D(2,4,5,15,.46),
            [NameDomain.Country] = D(2,4,5,17,.52, suffixes:T("eth","oth","ara","yx"), suffixChance:.34),
            [NameDomain.Dungeon] = D(1,4,4,18,.42, suffixes:T("vault","hollow","crypt"), suffixChance:.22),
            [NameDomain.Monster] = D(1,4,3,15,.58, suffixes:T("ith","spawn","shade"), suffixChance:.18),
            [NameDomain.Spell] = D(2,4,4,18,.58),
            [NameDomain.Deity] = D(3,5,6,18,.62),
            [NameDomain.Artifact] = D(2,4,5,19,.52),
            [NameDomain.Language] = D(2,4,5,15,.48, suffixes:T("ic","ish","an"), suffixChance:.45)
        },
        new Dictionary<NameDomain, IReadOnlyList<string>>
        {
            [NameDomain.PersonGiven] = new[] { "Azerin","Drethis","Eshara","Kavren","Morveth","Nerith","Rhazel","Serev","Thyren","Veshara","Zorath" },
            [NameDomain.Country] = new[] { "Azareth","Drevaroth","Esharia","Kharoth","Morveth","Nerasha","Rhazeth","Serevoth","Vexara","Zorath" },
            [NameDomain.Monster] = new[] { "Ashraith","Drevith","Gloomshade","Kharspawn","Morith","Neresh","Rhazith","Thornshade","Vexspawn","Zorraith" },
            [NameDomain.Spell] = new[] { "Azareth","Drethis","Esharoth","Kavreth","Morvessa","Nerith","Rhazelor","Sereveth","Vexara","Zorath" }
        },
        "Ominous occult naming with sibilants, dark vowels, and unusual finals.", new HashSet<string>(new[]{"occult","ominous","dark"}, StringComparer.OrdinalIgnoreCase));
}
