using System.Collections.Generic;
using Godot;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Game;

/// <summary>
/// Small pixel pictures for the UI (issue #46): one per good, for people and time, per building and per
/// tech, drawn at 12 by 12 pixels and shown at twice that so they match the blocky world. Buildings and
/// techs name theirs with <c>icon</c> in the data (the id if not given); a name with no picture gets a plain
/// square. Units use their type badge (see <see cref="UnitIcons"/>) in their owner's colour.
/// </summary>
public static class UiIcons
{
    /// <summary>Screen pixels per picture pixel.</summary>
    public const int Scale = 2;

    private static readonly Dictionary<string, Texture2D> Cache = new();

    /// <summary>The picture called <paramref name="name"/>, or a plain square if there is none.</summary>
    public static Texture2D Get(string name)
    {
        if (Cache.TryGetValue(name, out var tex)) return tex;
        tex = ImageTexture.CreateFromImage(Paint(Glyphs.TryGetValue(name, out var rows) ? rows : Missing, c => Palette.TryGetValue(c, out var col) ? col : null));
        Cache[name] = tex;
        return tex;
    }

    public static bool Has(string name) => Glyphs.ContainsKey(name);

    public static Texture2D For(Good good) => Get(good switch
    {
        Good.Food => "food",
        Good.Wood => "wood",
        Good.Stone => "stone",
        Good.IronOre => "iron_ore",
        Good.Coal => "coal",
        Good.Crystal => "crystal",
        _ => "iron",
    });

    /// <summary>A unit type's badge in <paramref name="team"/> colour with a dark rim.</summary>
    public static Texture2D For(UnitRole role, Color team)
    {
        string key = $"role:{(int)role}:{team.ToHtml()}";
        if (Cache.TryGetValue(key, out var tex)) return tex;
        var glyph = UnitIcons.Glyphs[(int)role % UnitIcons.Glyphs.Length];
        var rim = new Color(0.1f, 0.08f, 0.06f);
        // Grow the glyph by a pixel all round for the rim, keeping it 12 by 12 by drawing it a pixel in.
        var rows = new string[12];
        for (int y = 0; y < 12; y++)
        {
            var row = new char[12];
            for (int x = 0; x < 12; x++)
            {
                bool on = Lit(glyph, x, y), near = false;
                for (int dy = -1; dy <= 1 && !on; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (Lit(glyph, x + dx, y + dy)) near = true;
                row[x] = on ? 'X' : near ? 'k' : '.';
            }
            rows[y] = new string(row);
        }
        tex = ImageTexture.CreateFromImage(Paint(rows, c => c == 'X' ? team : c == 'k' ? rim : null));
        Cache[key] = tex;
        return tex;
    }

    private static bool Lit(string[] glyph, int x, int y) => x >= 0 && y >= 0 && y < glyph.Length && x < glyph[y].Length && glyph[y][x] == 'X';

    private static Image Paint(string[] rows, System.Func<char, Color?> colour)
    {
        var image = Image.CreateEmpty(12 * Scale, 12 * Scale, false, Image.Format.Rgba8);
        for (int y = 0; y < 12 && y < rows.Length; y++)
        for (int x = 0; x < 12 && x < rows[y].Length; x++)
        {
            if (colour(rows[y][x]) is not { } c) continue;
            image.FillRect(new Rect2I(x * Scale, y * Scale, Scale, Scale), c);
        }
        return image;
    }

    private static readonly string[] Missing =
    {
        "............", ".kkkkkkkkkk.", ".kssssssssk.", ".kssssssssk.", ".kssssssssk.", ".kssssssssk.",
        ".kssssssssk.", ".kssssssssk.", ".kssssssssk.", ".kssssssssk.", ".kkkkkkkkkk.", "............",
    };

    private static readonly Dictionary<char, Color> Palette = new()
    {
        ['k'] = Color.Color8(26, 20, 16),
        ['w'] = Color.Color8(245, 240, 225),
        ['y'] = Color.Color8(236, 196, 72),
        ['Y'] = Color.Color8(180, 132, 40),
        ['o'] = Color.Color8(214, 150, 70),
        ['O'] = Color.Color8(160, 98, 42),
        ['b'] = Color.Color8(140, 92, 52),
        ['B'] = Color.Color8(92, 58, 32),
        ['g'] = Color.Color8(96, 170, 72),
        ['G'] = Color.Color8(52, 110, 48),
        ['s'] = Color.Color8(150, 150, 150),
        ['S'] = Color.Color8(98, 98, 104),
        ['l'] = Color.Color8(205, 205, 205),
        ['r'] = Color.Color8(200, 60, 50),
        ['R'] = Color.Color8(130, 34, 30),
        ['c'] = Color.Color8(40, 40, 46),
        ['C'] = Color.Color8(70, 70, 80),
        ['i'] = Color.Color8(150, 165, 190),
        ['I'] = Color.Color8(100, 112, 135),
        ['u'] = Color.Color8(176, 96, 52),
        ['p'] = Color.Color8(232, 184, 140),
        ['n'] = Color.Color8(80, 120, 210),
        ['v'] = Color.Color8(160, 110, 230),
        ['t'] = Color.Color8(90, 210, 220),
        ['f'] = Color.Color8(255, 120, 40),
    };

    /// <summary>The pictures by name: one character per pixel, a letter from <see cref="Palette"/> or . for clear.</summary>
    private static readonly Dictionary<string, string[]> Glyphs = new()
    {
        // Dusk Bloom: the Dusk Staff's violet bloom round a cyan light, with sparks of healing.
        ["dusk_bloom"] = new[]
        {
            "t....tt....t", "...kvvvvk...", "..kvvttvvk..", "..kvtwwtvk..", "..kvvttvvk..", "t..kvvvvk..t",
            ".....kSk....", ".....kSk....", ".....ktk....", ".....kSk....", ".....kSk....", "......k.....",
        },
        // Earth Wall: a block of earth risen from the ground, grass on top, with sparks of magic.
        ["earth_wall"] = new[]
        {
            "t.........v.", "...kkkkkk...", "..kggggggk.t", "..kGGGGGGk..", "..kbbBbbbk..", "v.kbBbbbBk..",
            "..kbbbbBbk..", "..kBbbbbbk..", "..kbbBbbbk..", "kkkbbbbbBkkk", "kBbBbbBbbBbk", "kkkkkkkkkkkk",
        },
        // Ice Bridge: a slab of ice over blue water, with sparks of magic.
        ["ice_bridge"] = new[]
        {
            "v.........t.", "....t.......", "..........v.", "kkkkkkkkkkkk", "kwwlwwwwlwwk", "klwwtwwlwwtk",
            "ktttltttlttk", "kkkkkkkkkkkk", "nnnnnnnnnnnn", "nntnnnnnntnn", "nnnnnntnnnnn", "nnnnnnnnnnnn",
        },
        // Chasm: the ground split open, too deep to climb out of, with sparks of magic.
        ["chasm"] = new[]
        {
            "t.........v.", ".....t......", "kkkk....kkkk", "gggk....kggg", "GgGbk..kbGgG", "bbbbk..kbbbb",
            "bBbbk..kbBbb", "bbbbbkkbbbbb", "bbBbbbbbbBbb", "bbbbbbBbbbbb", "BbbbbbbbbbbB", "kkkkkkkkkkkk",
        },
        // Grow Forest: trees sprung up from the ground, with sparks of magic.
        ["grow_forest"] = new[]
        {
            "..kkk.....t.", ".kgggk......", "kggGggk.kkk.", "kgGggGkkgggk", "kggggGkgGggk", ".kkGkk.kgGk.",
            "t..kbk..kbk.", "...kbk..kbk.", "...kbk..kbkv", "...kbk..kbk.", "kkkkkkkkkkkk", "gGgggGgggGgg",
        },
        ["food"] = new[]
        {
            "............", "............", "....kkkkk...", "..kkoyoyokk.", ".koyoooyooyk", ".kooyooooyok",
            "kooooooooook", "kOoooooooook", "kOOooooooOOk", ".kOOOOOOOOk.", "..kkkkkkkk..", "............",
        },
        ["wood"] = new[]
        {
            "............", "............", "...kkkkkkk..", "..kbbbbbbbkk", ".kbBbbbBbkok", "kbbbbbbbkoyk",
            "kBbbBbbbkoyk", "kbbbbbBbkook", "kbBbbbbbbkk.", ".kkkkkkkkk..", "............", "............",
        },
        ["stone"] = new[]
        {
            "............", "............", "....kkkk....", "...kllssk...", "..klssssskk.", ".klsssssSssk",
            ".ksssSsssssk", "kssssssSsssk", "kSsssssssSSk", ".kSSSSSSSSk.", "..kkkkkkkk..", "............",
        },
        ["iron_ore"] = new[]
        {
            "............", "............", "....kkkk....", "...kllssk...", "..klsusssk..", ".klsuussSskk",
            ".kssssssusk.", "kssSsssuussk", "kSsuussssSSk", ".kSSSSSSSSk.", "..kkkkkkkk..", "............",
        },
        ["coal"] = new[]
        {
            "............", "............", "............", "....kkk.....", "...kssckkk..", "..kscccksck.",
            "..kcccckcck.", ".kkkkkkscccc", "kssckscckcck", "kcccckcccck.", ".kkkk.kkkk..", "............",
        },
        ["iron"] = new[]
        {
            "............", "............", "............", "....kkkkkk..", "...kllllllk.", "..kliiiiilk.",
            ".kkkkkkkkIk.", ".kliiiiilkIk", ".kiiiiiiikk.", ".kIIIIIIIk..", ".kkkkkkkkk..", "............",
        },
        // Crystal: a cluster of cyan points growing from a rock.
        ["crystal"] = new[]
        {
            "............", ".....kk.....", "....ktwk....", "..k.ktwk.k..", ".ktkkttkktk.", ".ktwkttkwtk.",
            ".kttkttktvk.", "..ktkvtkvk..", "..kvkvvkvk..", ".kkkkkkkkkk.", "kSsssssssSSk", ".kkkkkkkkkk.",
        },
        // Mana: a glowing violet drop with a spark in it.
        ["mana"] = new[]
        {
            ".....kk.....", ".....kvk....", "....kvvk....", "...kvvvvk...", "...kvwvvk...", "..kvwtvvvk..",
            "..kvttvvvk..", ".kvvtvvvvvk.", ".kvvvvvvvvk.", "..kvvvvvvk..", "...kkvvkk...", ".....kk.....",
        },
        // Shrine: a stone plinth under a pointed roof, a crystal glowing in it.
        ["shrine"] = new[]
        {
            ".....kk.....", "....kSSk....", "...kSSSSk...", "..kkkkkkkk..", "..ks.kk.sk..", "..ks.kt.sk..",
            "..kskttksk..", "..kskwtksk..", "..ks.kk.sk..", ".kkkkkkkkkk.", "kSsssssssSSk", "kkkkkkkkkkkk",
        },
        ["people"] = new[]
        {
            "....kkkk....", "...kppppk...", "...kppppk...", "...kppppk...", "....kkkk....", "..kknnnnkk..",
            ".knnnnnnnnk.", ".knknnnnknk.", ".knknnnnknk.", ".kkkbbbbkkk.", "...kbkkbk...", "...kkk.kkk..",
        },
        ["sun"] = new[]
        {
            ".....yy.....", ".y...yy...y.", "..y......y..", "....kkkk....", "...kyyyyk...", "yy.kyywyk.yy",
            "yy.kyyyyk.yy", "...kYyyyk...", "....kkkk....", "..y......y..", ".y...yy...y.", ".....yy.....",
        },
        ["moon"] = new[]
        {
            "....kkkk....", "...kwwlkk...", "..kwwlk.....", ".kwwlk......", ".kwwlk......", ".kwwlk......",
            ".kwwlk......", ".kwwllk.....", "..kwwllkk.kk", "..kkwwwllllk", "....kkwwwkk.", "......kkk...",
        },
        ["time"] = new[]
        {
            "..kkkkkkkk..", "..kbbbbbbk..", "...kyyyyk...", "...kyyyyk...", "....kyyk....", ".....kk.....",
            ".....kk.....", "....kwyk....", "...kwwyyk...", "...kyyyyk...", "..kbbbbbbk..", "..kkkkkkkk..",
        },
        ["town_hall"] = new[]
        {
            ".....kr.....", ".....krrk...", ".....kk.....", ".kk.kssk.kk.", ".kskkssskksk", ".kssssssssk.",
            ".kslssslssk.", ".ksssssssskk", ".kslsBBslsk.", ".ksssBBsssk.", ".kSSSBBSSSk.", ".kkkkkkkkkk.",
        },
        ["storehouse"] = new[]
        {
            "............", ".kkkkkkkkkk.", ".kobbbbbbok.", ".kbobbbbobk.", ".kbbobbobbk.", ".kbbbookbbk.",
            ".kbbbookbbk.", ".kbbobbobbk.", ".kbobbbbobk.", ".kobbbbbbok.", ".kkkkkkkkkk.", "............",
        },
        ["house"] = new[]
        {
            ".....kk.....", "....krrk....", "...krrrrk...", "..krrrrrrk..", ".krrrrrrrrk.", "kkkkkkkkkkkk",
            ".kwwwwwwwwk.", ".kwnnwwbbwk.", ".kwnnwwbbwk.", ".kwwwwwbbwk.", ".kwwwwwbbwk.", ".kkkkkkkkkk.",
        },
        // Fishing hut: a fish leaping over blue water.
        ["fishing_hut"] = new[]
        {
            "............", "....kkkk....", "..kkllllk.kk", ".klklllllkik", "kllllllllkik", ".kiiiiiiikik",
            "..kkkkkkk.kk", "............", "nnnnnnnnnnnn", "ntnnnntnnnnt", "nnnntnnnnnnn", "nnnnnnnnnnnn",
        },
        // Forester's lodge: a young tree with a spade beside it.
        ["forester_lodge"] = new[]
        {
            "....kkkk....", "...kggggk...", "..kgGggGgk..", "..kggGgggk.k", "..kgggGggkkk", "...kgggk.kbk",
            "....kbk..kb.", "....kbk..kb.", "....kbk.kSSk", "..kkkbkkkSSk", ".kGGGGGGGkkk", "kkkkkkkkkkkk",
        },
        ["farm"] = new[]
        {
            "..y...y...y.", ".yyy.yyy.yyy", ".yYy.yYy.yYy", ".yyy.yyy.yyy", "..Y...Y...Y.", "..g...g...g.",
            ".g.g.g.g.g.g", "..g...g...g.", "..g...g...g.", "kkkkkkkkkkkk", "bBbBbBbBbBbB", "kkkkkkkkkkkk",
        },
        ["barracks"] = new[]
        {
            "kk........kk", "klk......klk", ".klk....klk.", "..klk..klk..", "...klkklk...", "....kllk....",
            "....kllk....", "...klkklk...", ".kkbk..kbkk.", ".kbbk..kbbk.", "kbk......kbk", "kk........kk",
        },
        ["smelter"] = new[]
        {
            "....kk......", "....kSk.....", "..kkkSSkkk..", ".kssssssssk.", ".kslssslssk.", ".ksskkkkssk.",
            ".kskfyyfksk.", ".kskffffksk.", ".ksskkkkssk.", ".kssssssssk.", ".kSSSSSSSSk.", ".kkkkkkkkkk.",
        },
        ["wall"] = new[]
        {
            "............", "kkk.kkk.kkk.", "ksk.ksk.ksk.", "kskkkskkkskk", "kssssssssssk", "kSkkkSkkkkSk",
            "kssskssskssk", "kkkkkkkkkkkk", "kskssskssssk", "kkkkkkkkkkkk", "ksssksssksSk", "kkkkkkkkkkkk",
        },
        ["gate"] = new[]
        {
            "kkk.kkk.kkk.", "kskkkskkkskk", "kssssssssssk", "ksssskkssssk", "ksskkbbkkssk", "kskbbbbbbksk",
            "kskbBbbBbksk", "kskbbbbbbksk", "kskbBbbBbksk", "kskbbbbbbksk", "kSkbBbbBbkSk", "kkkkkkkkkkkk",
        },
        ["tower"] = new[]
        {
            ".kk..kk..kk.", ".kskkskkksk.", ".kssssssssk.", ".kkkkkkkkkk.", "..kssssssk..", "..ksskksSk..",
            "..ksskkssk..", "..kSsssssk..", "..kssssSsk..", "..kskkkksk..", "..kskbbksk..", "..kkkkkkkk..",
        },
        ["bridge"] = new[]
        {
            "............", "............", ".k..k..k..k.", ".kb.kb.kb.kb", "kkkkkkkkkkkk", "kobobobobobk",
            "kkkkkkkkkkkk", "gkB......Bkg", "GkB......BkG", "nkBnnnnnnBkn", "nnnninnnnnin", "nninnnnninnn",
        },
        ["torch"] = new[]
        {
            ".....y......", "....yfy.....", "....fyf.....", "...ffyff....", "...rfffr....", "....kkk.....",
            "....kbk.....", "....kbk.....", "....kBk.....", "....kbk.....", "....kBk.....", "....kkk.....",
        },
        ["campfire"] = new[]
        {
            "............", ".....y......", "....yfy.....", "...fyyyf....", "..ffyffyf...", "..rfffffr...",
            ".kbbkffkbbk.", "kBBbbkkbbBBk", ".kkbbbbbbkk.", "kssksssskssk", "kSSSSSSSSSSk", ".kkkkkkkkkk.",
        },
        ["road"] = new[]
        {
            "gggkssskgggg", "ggkssskggggg", "gkssskgggggg", "gksssskggggg", "ggksssskgggg", "gggksssskggg",
            "ggggksssskgg", "gggggksssskg", "ggggksssskgg", "gggksssskggg", "ggksssskgggg", "gkssssskgggg",
        },
        ["pick"] = new[]
        {
            "..kkkkkkkk..", ".klllllllllk", "kllkkbbkklk.", "klk.kbbk.kk.", "kk..kbbk....", "....kbbk....",
            "....kbbk....", "....kbbk....", "....kbbk....", "....kbbk....", "....kBBk....", ".....kk.....",
        },
        ["bow"] = new[]
        {
            "kkk.........", "kbbkk.......", ".kBbbk......", ".kk.kbk..kk.", ".kl..kbk.kwk", ".kl..kbbkkkk",
            "kkkkkkkkkllk", ".kl..kbbkkkk", ".kl..kbk.kwk", ".kk.kbk..kk.", ".kBbbk......", "kbbkk.......",
        },
        ["star"] = new[]
        {
            ".....kk.....", ".....kvk....", "....kvvk....", "kkkkkvwvkkkk", ".kvvvwwwvvk.", "..kvvwwwvk..",
            "...kvwwvk...", "...kvvvvk...", "..kvvkkvvk..", "..kvk..kvk..", ".kvk....kvk.", ".kk......kk.",
        },
        ["bricks"] = new[]
        {
            "............", "............", "...kkkkkk...", "...kroorrk..", "...kRRRRRk..", "kkkkkkkkkkkk",
            "kroorkroork.", "kRRRRkRRRRk.", "kkkkkkkkkkkk", "kroorrkroork", "kRRRRRkRRRRk", "kkkkkkkkkkkk",
        },
        ["sprout"] = new[]
        {
            "............", "......kk....", ".kk..kggk...", "kggk.kgGk...", "kgGgkkGk....", ".kGGgkGk....",
            "..kkkGk.....", ".....Gk.....", "....kGk.....", "..kkbbbkk...", ".kbBbbbBbk..", "kkkkkkkkkkk.",
        },
        ["horse"] = new[]
        {
            "....kk.kk...", "...kBkkBk...", "...kbbbbBk..", "..kbbbbbbBk.", "..kbkbbbbbk.", ".kbbbbbbbbBk",
            "kbbbbkbbbbBk", "kbbbkkbbbbBk", ".kkk.kbbbbBk", ".....kbbbbBk", "....kbbbbbBk", "....kkkkkkkk",
        },
        ["wheel"] = new[]
        {
            "....kkkk....", "..kkbbbbkk..", ".kbbkbbkbbk.", ".kbk.kk.kbk.", "kbbkkbbkkbbk", "kbkbbkkbbkbk",
            "kbkbbkkbbkbk", "kbbkkbbkkbbk", ".kbk.kk.kbk.", ".kbbkbbkbbk.", "..kkbbbbkk..", "....kkkk....",
        },
        ["flame"] = new[]
        {
            ".....k......", "....kfk.....", "....kffk....", "...kfffk.k..", "..kffyffkfk.", "..kfyyyffk..",
            ".kffyyyyffk.", ".kfyywwyyfk.", ".kfywwwwyfk.", ".kfywwwwyfk.", "..kfyyyyfk..", "...kkkkkk...",
        },
        ["shovel"] = new[]
        {
            "....kkkk....", "....kbbk....", ".....kk.....", ".....kbk....", ".....kbk....", ".....kbk....",
            "...kkkkkkk..", "...klllllk..", "...klllllk..", "...klllllk..", "....klllk...", ".....kkk....",
        },
        ["hammer"] = new[]
        {
            "............", "..kkkkkkkk..", ".klllllllllk", ".klllllllllk", ".kIIIIIIIIk.", "..kkkbbkkk..",
            "....kbbk....", "....kbbk....", "....kbbk....", "....kbbk....", "....kBBk....", ".....kk.....",
        },
        ["shield"] = new[]
        {
            ".kkkkkkkkkk.", ".knnnnnnnnk.", ".knnnyynnnk.", ".knnnyynnnk.", ".knyyyyyynk.", ".knyyyyyynk.",
            ".knnnyynnnk.", "..knnyynnk..", "..knnyynnk..", "...knnnnk...", "....knnk....", ".....kk.....",
        },
        ["drum"] = new[]
        {
            "k........k..", ".k......k...", "..kkkkkkkkk.", ".klllllllllk", ".kwwwwwwwwwk", ".kRkrrkrrkRk",
            ".krrkrrkrrrk", ".krkrrrkrrrk", ".kRrkrkrrrRk", ".krrrkrrrrrk", ".kRRRRRRRRRk", "..kkkkkkkkk.",
        },
        // The giant's heart: a lump of grey stone with a glowing amber core.
        ["giant_heart"] = new[]
        {
            "............", "...kkkkkk...", "..kssSssssk.", ".ksSsyysSsk.", ".kssyffysSk.", ".kSsyffyssk.",
            ".kssyyyysSk.", ".ksSssssssk.", "..ksSsssSk..", "...kssSsk...", "....kssk....", ".....kk.....",
        },
        // An old ruin's carved stone tablet, gold writing on grey, standing in grass.
        ["ruin"] = new[]
        {
            "....kkkk....", "...klllsk...", "..klsssssk..", "..ksysysSk..", "..kssyssSk..", "..ksyyysSk..",
            "..kssySsSk..", "..ksyssSsk..", ".kkssSssskk.", ".kGgggggGGk.", "..kkkkkkkk..", "............",
        },
        // The sunstone circlet: a gold band with a glowing orange stone at its front.
        ["sunstone_circlet"] = new[]
        {
            "............", "............", ".....kk.....", "....kfwk....", "..kkkffkkk..", ".kyyykkyyyk.",
            "kyYk....kYyk", "kyk......kyk", "kYyk....kyYk", ".kYyyyyyyYk.", "..kkkkkkkk..", "............",
        },
        // A boar's tusk, curved, on a strap.
        ["tusk"] = new[]
        {
            "............", "..........k.", ".........kwk", "........kwlk", ".......kwlk.", "......kwlk..",
            "....kkwlk...", "..kkwwlk....", ".kwwllk.....", "kbbkkk......", "kBk.........", ".k..........",
        },
        // The bog lantern: a cold green light in a cage of reeds, on a hook.
        ["bog_lantern"] = new[]
        {
            ".....kk.....", "....k..k....", ".....kk.....", "...kGkkGk...", "..kGgttgGk..", "..kgtwwtgk..",
            "..kgtwwtgk..", "..kGgttgGk..", "...kGkkGk...", "....kGGk....", ".....kk.....", "............",
        },
        // The crab shell shield: a round red shell with ridges.
        ["crab_shell"] = new[]
        {
            "............", "...kkkkkk...", "..krrRRrrk..", ".krrRrrRrrk.", ".krRrrrrRrk.", ".krrrRRrrrk.",
            ".krRrrrrRrk.", ".krrRrrRrrk.", "..krrRRrrk..", "...kkkkkk...", "....kppk....", ".....kk.....",
        },
        // The wyrm crystal: a violet crystal, glowing at its heart.
        ["wyrm_crystal"] = new[]
        {
            ".....kk.....", "....kvvk....", "...kvttvk...", "...kvwtvk...", "..kvtwwtvk..", "..kvtwwtvk..",
            "..kvttttvk..", "..kvttttvk..", "...kvttvk...", "...kvttvk...", "....kvvk....", ".....kk.....",
        },
        ["paw"] = new[]
        {
            "............", "..kk....kk..", ".kBBk..kBBk.", ".kBBk..kBBk.", "..kkkkkkkk..", "kk.kBBBBk.kk",
            "kBkBBBBBBkBk", "kBkBBBBBBkBk", ".kkBBBBBBkk.", "..kBBBBBBk..", "...kBBBBk...", "....kkkk....",
        },
    };
}
