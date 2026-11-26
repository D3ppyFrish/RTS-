using System;
using System.Collections.Generic;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>A monster lair: a den in the wild and the pack that guards it (see <see cref="MonsterSystem"/>).</summary>
public sealed class Lair
{
    internal Lair(int id, UnitDef monster, int x, int y, int z)
    {
        Id = id;
        Monster = monster;
        X = x;
        Y = y;
        Z = z;
        HomeX = x;
        HomeY = y;
        HomeZ = z;
    }

    public int Id { get; }
    /// <summary>The kind of monster that lives here; its <see cref="UnitDef.Lair"/> says how the lair works.</summary>
    public UnitDef Monster { get; }
    /// <summary>The den's column.</summary>
    public int X { get; }
    public int Z { get; }
    /// <summary>The level a unit standing at the den has its feet at.</summary>
    public int Y { get; }
    /// <summary>The pack's monsters (ids) still in the wild, in spawn order.</summary>
    public IReadOnlyList<int> Pack => PackIds;
    /// <summary>The pack is gone: each of its monsters was killed or beaten.</summary>
    public bool Cleared { get; internal set; }
    /// <summary>The player whose unit got the last of the pack, once the lair is cleared; otherwise -1.</summary>
    public int ClearedBy { get; internal set; } = -1;
    /// <summary>A player used the den once the lair was cleared (see <see cref="MonsterSystem"/>): it is gone from the map.</summary>
    public bool Used { get; internal set; }
    /// <summary>The player who used the den, or -1.</summary>
    public int UsedBy { get; internal set; } = -1;
    /// <summary>The building put up on the den when it was used, or 0.</summary>
    public int Building { get; internal set; }
    /// <summary>
    /// The wild side's totem at the den (see <see cref="LairDef.Totem"/>), a building id, or 0 for a lair with none.
    /// It stays set once the totem is down; the lair is cleared then.
    /// </summary>
    public int Totem { get; internal set; }
    // The player whose unit last struck the totem, or -1.
    internal int TotemHitBy = -1;

    /// <summary>
    /// How far from the den (blocks) the pack roams (see <see cref="LairDef.Roam"/>), or 0 if it stays home: only
    /// the lairs spread across the map roam.
    /// </summary>
    public int Roam { get; internal set; }
    /// <summary>
    /// Where the pack is at home now: the den, or for a pack that roams the spot it wandered to last. It guards
    /// round here, and its leash is measured from here.
    /// </summary>
    public int HomeX { get; internal set; }
    public int HomeY { get; internal set; }
    public int HomeZ { get; internal set; }
    // The tick a roaming pack next moves on, if it is not fighting.
    internal long NextRoam;
    /// <summary>The tick the next monster is born to a growing pack (see <see cref="LairDef.GrowTicks"/>), or 0 if it never grows.</summary>
    public long NextBirth { get; internal set; }
    /// <summary>
    /// The tick this lair next tries to found a new one (see <see cref="LairDef.SpreadTicks"/>), or 0 if it never does:
    /// only lairs spread across the map, and those they founded, spread.
    /// </summary>
    public long NextSpread { get; internal set; }
    /// <summary>The lair whose pack founded this one (see <see cref="LairDef.SpreadTicks"/>), or 0 for one placed at match start.</summary>
    public int Parent { get; internal set; }

    internal readonly List<int> PackIds = new();
    /// <summary>The pack's monsters (ids) following a blood trail, in the order they picked it up.</summary>
    internal readonly List<int> TrackerIds = new();
    /// <summary>The pack's monsters (ids) following a blood trail (see <see cref="UnitDef.TracksBlood"/>), in the order they picked it up.</summary>
    public IReadOnlyList<int> Trackers => TrackerIds;
    // The player whose unit last killed or beat one of the pack, or -1.
    internal int LastBeatenBy = -1;
    /// <summary>
    /// For a lair that lurks (see <see cref="LairDef.Lurk"/>): the shallow-water columns by the den its pack waits in,
    /// nearest the den first, one for each monster of the pack (the k-th of <see cref="Pack"/> waits in the k-th, round
    /// again if there are fewer); empty if there is no water near enough, and then the pack waits on the bank.
    /// </summary>
    public IReadOnlyList<(int X, int Y, int Z)> Pools => PoolSpots;
    internal readonly List<(int X, int Y, int Z)> PoolSpots = new();
}

/// <summary>
/// Wild monsters, and the Orcs' taming of them. Monsters belong to no player: they are the wild side,
/// <see cref="Simulation.WildPlayer"/>, which fights everyone. They live in lairs: every match has one on the
/// way from each start area to the map centre (<see cref="PlaceAtStarts"/>), and more are spread across the map by
/// biome (<see cref="PlaceWild"/>): a den with a pack around it. The
/// pack guards its den: a monster with nothing to do goes after the unit nearest the den within the lair's
/// guard radius (<see cref="LairDef.Guard"/>), workers included, and walks back to the den when there is none.
/// One lured further than the leash (<see cref="LairDef.Leash"/>) breaks off and goes home. Monsters never break
/// and rout (see <see cref="CombatSystem"/>).
///
/// A faction that tames monsters (<see cref="Content.FactionDef.TamesMonsters"/>, the Orcs) does not kill
/// them: a wild monster one of its units would kill is beaten instead (<see cref="UnitStore.Subdued"/>). It
/// is the player's now, with 1 hit point left, but it takes no orders and does not fight until the player
/// chooses (<see cref="TameCommand"/>): recruit it, and it joins the army at full health; or ride it, and the
/// nearest of the player's units that can (<see cref="UnitDef.Ride"/>), within <see cref="RideRange"/>, climbs
/// on and becomes the rider type, keeping its share of hit points, while the monster is gone. Nobody goes
/// after a beaten monster on their own, though an attack order or a stray blast can still kill it.
///
/// A lair is cleared when its pack is gone. If the player whose unit got the last of the pack does not tame
/// monsters, they get the lair's reward (<see cref="LairDef.Reward"/>) in their storage nearest the den. A lair with a
/// totem (<see cref="LairDef.Totem"/>, the giant spiders' at the bottom of their hill caves) is cleared only when the
/// totem is knocked down, and its reward goes to the player who struck it last.
///
/// A cleared den can then be used, once, by any player with a unit within <see cref="DenRange"/> of it whose
/// faction has a prize for beating that monster (<see cref="LairDef.Prizes"/>, see <see cref="DenCommand"/>):
/// a building put up on the den, a site their workers build (the Orcs' wolf lodge at a wolf den, anyone's boar
/// pen at a boar wallow); a form, which the nearest of the units that can take it takes (the Settlers'
/// Wolfheart); or a relic, which the nearest of the fighting units without one takes up (the Settlers' giant's
/// heart). The den is gone from the map either way.
/// All numbers are starting guesses to be tuned in playtests.
/// </summary>
public sealed class MonsterSystem
{
    /// <summary>Ticks between a lair's looks at its pack and the units near it (spread over lairs by id).</summary>
    public const int GuardInterval = 4;
    /// <summary>Most levels between a den and a unit its pack goes after.</summary>
    public const int GuardHeight = 4;
    /// <summary>A monster with nothing to do further than this from its den (blocks) walks back to it.</summary>
    public const int HomeRadius = 4;
    /// <summary>How near a beaten monster (blocks) a unit has to be to climb on and ride it.</summary>
    public const int RideRange = 12;
    /// <summary>The furthest a start area's lair is from it (blocks), toward the map centre and at most two thirds of the way.</summary>
    public const int LairDistance = 40;
    /// <summary>The nearest a start area's lair can be to it (blocks); with no ground for a den further out, there is no lair.</summary>
    public const int MinLairDistance = 20;
    /// <summary>The fewest columns between a den and a building.</summary>
    public const int BuildingGap = 10;
    /// <summary>How near a cleared den (blocks) one of a player's units has to be for them to use it.</summary>
    public const int DenRange = 8;
    /// <summary>
    /// How far from its den (blocks) a monster that tracks blood (<see cref="UnitDef.TracksBlood"/>) follows a trail
    /// before it gives up and goes home: well past its usual leash.
    /// </summary>
    public const int TrackRange = 48;

    // Where a pack's monsters spawn around the den: every other column out to 8 blocks, nearest first.
    private static readonly (int X, int Z)[] PackSpots = MakePackSpots();

    private readonly Simulation _sim;
    private readonly List<Lair> _lairs = new();
    private readonly List<Lair> _herds = new();
    private readonly List<int> _home = new();
    // Lurking monsters (ids, ascending) that came up out of the water at someone and have not struck their first blow.
    private readonly List<int> _ambush = new();
    private int _nextId = 1;

    public MonsterSystem(Simulation sim)
    {
        _sim = sim;
    }

    private UnitStore U => _sim.Units;
    private VoxelWorld World => _sim.World;
    private EconomySystem Economy => _sim.Economy;
    private UnitDef Def(int i) => _sim.Content.Units[U.Def[i]];

    /// <summary>Every lair, cleared ones too, in the order they were placed.</summary>
    public IReadOnlyList<Lair> Lairs => _lairs;

    /// <summary>
    /// The herds of wild game (see <see cref="UnitDef.Game"/>, <see cref="PlaceHerds"/>), kept apart from the monster
    /// lairs: a herd guards nothing, pays no reward, leaves no den and counts for nothing that clearing a lair does.
    /// A herd shot out to the last is <see cref="Lair.Cleared"/> and gone for good.
    /// </summary>
    public IReadOnlyList<Lair> Herds => _herds;

    /// <summary>The herd wild game unit i belongs to, or null.</summary>
    public Lair? HerdOf(int i)
    {
        int id = U.Id[i];
        foreach (var herd in _herds)
            if (!herd.Cleared && herd.PackIds.Contains(id)) return herd;
        return null;
    }

    public Lair? GetLair(int id)
    {
        foreach (var lair in _lairs)
            if (lair.Id == id) return lair;
        return null;
    }

    /// <summary>True if unit i is a wild monster (it belongs to no player).</summary>
    public bool IsWild(int i) => U.Owner[i] == Simulation.WildPlayer;

    /// <summary>
    /// True if wild monster i leaves unit j alone: j is a worker whose player has a tech that spares workers (Beast
    /// lore, see <see cref="Content.TechEffectType.SpareWorkers"/>), or it knows the deep caves and is in them (the delver,
    /// see <see cref="UnitDef.CaveWise"/>). The monster does not pick it, track its blood or
    /// go after it at the den, though it still answers a worker that hits it. A city-state's guards are not monsters.
    /// </summary>
    public bool Spares(int i, int j) => IsWild(i) && U.Town[i] == 0 && Spared(j);

    /// <summary>
    /// True if the wild monsters leave unit j alone (see <see cref="Spares"/>): a worker of a player with Beast lore, or a
    /// unit that knows the deep caves (<see cref="UnitDef.CaveWise"/>) while it is in them.
    /// </summary>
    private bool Spared(int j)
    {
        var def = Def(j);
        return def.Role == UnitRole.Worker && Economy.SparesWorkers(U.Owner[j]) || def.CaveWise && Economy.DeepGround(U.Y[j]);
    }

    /// <summary>Hash of the lairs (the monsters themselves are units and hash with them).</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(_nextId);
        Mix(_lairs.Count);
        foreach (var lair in _lairs)
        {
            Mix(lair.Id); Mix(lair.Monster.Index); Mix(lair.X); Mix(lair.Y); Mix(lair.Z);
            Mix(lair.Cleared ? 1 : 0); Mix(lair.ClearedBy); Mix(lair.LastBeatenBy);
            Mix(lair.Used ? 1 : 0); Mix(lair.UsedBy); Mix(lair.Building); Mix(lair.Totem); Mix(lair.TotemHitBy);
            Mix(lair.Roam); Mix(lair.HomeX); Mix(lair.HomeY); Mix(lair.HomeZ); Mix(lair.NextRoam); Mix(lair.NextBirth);
            Mix(lair.NextSpread); Mix(lair.Parent);
            Mix(lair.PackIds.Count);
            foreach (int id in lair.PackIds) Mix(id);
            Mix(lair.TrackerIds.Count);
            foreach (int id in lair.TrackerIds) Mix(id);
            Mix(lair.PoolSpots.Count);
            foreach (var (px, py, pz) in lair.PoolSpots) { Mix(px); Mix(py); Mix(pz); }
        }
        Mix(_ambush.Count);
        foreach (int id in _ambush) Mix(id);
        Mix(_herds.Count);
        foreach (var herd in _herds)
        {
            Mix(herd.Id); Mix(herd.Monster.Index); Mix(herd.X); Mix(herd.Y); Mix(herd.Z); Mix(herd.Cleared ? 1 : 0);
            Mix(herd.Roam); Mix(herd.HomeX); Mix(herd.HomeY); Mix(herd.HomeZ); Mix(herd.NextRoam); Mix(herd.NextBirth);
            Mix(herd.PackIds.Count);
            foreach (int id in herd.PackIds) Mix(id);
        }
        return h;
    }

    // --- Lairs -------------------------------------------------------------------------------------

    /// <summary>
    /// Match setup: a lair for every start area, in the order of the starts, of each lair monster in turn
    /// (<see cref="Content.ContentDb.LairMonsters"/>). The den goes <see cref="LairDistance"/> blocks from the start
    /// toward the map centre (no more than two thirds of the way), or nearer where that spot is under water,
    /// too close to a building or off the map, but no nearer than <see cref="MinLairDistance"/>. Call after
    /// placing the starting buildings. Returns the lairs placed.
    /// </summary>
    public List<Lair> PlaceAtStarts()
    {
        var placed = new List<Lair>();
        var monsters = _sim.Content.LairMonsters;
        if (monsters.Count == 0) return placed;
        var starts = _sim.Map.Starts;
        int cx = World.SizeX / 2, cz = World.SizeZ / 2;
        for (int k = 0; k < starts.Count; k++)
        {
            var s = starts[k];
            long dx = cx - s.X, dz = cz - s.Z;
            long len = IntMath.ISqrt(dx * dx + dz * dz);
            if (len == 0) (dx, dz, len) = (1, 0, 1);
            int far = (int)Math.Min(LairDistance, len * 2 / 3);
            for (int d = far; d >= MinLairDistance; d--)
            {
                int x = s.X + (int)(dx * d / len), z = s.Z + (int)(dz * d / len);
                if (!SiteFits(x, z)) continue;
                if (Place(monsters[k % monsters.Count], x, z) is { } lair) placed.Add(lair);
                break;
            }
        }
        return placed;
    }

    /// <summary>
    /// How many times as many lairs for its size the land beyond the default map's area has
    /// (<see cref="Rts.Sim.World.RegionGenerator.DefaultSize"/>): a bigger map is not only bigger but wilder, its far reaches
    /// thick with dens.
    /// </summary>
    public const int BeyondWildness = 2;

    /// <summary>
    /// How many lairs of a monster with <paramref name="perMap"/> for each 512 by 512 blocks (<see cref="LairDef.Count"/>)
    /// a map of <paramref name="area"/> columns gets (rounded down): that many for each 512 by 512 blocks up to the
    /// default map's area, and <see cref="BeyondWildness"/> times as many for the land beyond it. The default map
    /// and smaller ones get the same as ever.
    /// </summary>
    public static int WildCount(int perMap, long area)
    {
        long usual = (long)Rts.Sim.World.RegionGenerator.DefaultSize * Rts.Sim.World.RegionGenerator.DefaultSize;
        long weighted = area <= usual ? area : usual + (area - usual) * BeyondWildness;
        return (int)(perMap * weighted / (512L * 512L));
    }

    /// <summary>The nearest a lair spread across the map (see <see cref="PlaceWild"/>) is to a start area (blocks).</summary>
    public const int WildStartGap = 100;
    /// <summary>The nearest a lair spread across the map is to any other lair or to a city-state's town (blocks).</summary>
    public const int WildGap = 40;
    /// <summary>Columns tried for each lair spread across the map before it is given up.</summary>
    private const int WildTries = 600;

    /// <summary>
    /// Match setup: the lairs spread across the map (issue #45), for each monster with a
    /// <see cref="LairDef.Biome"/> in data order, <see cref="LairDef.Count"/> of them for each 512 by 512 blocks of
    /// map (rounded down; <see cref="WildCount"/>, more beyond the default map's area). Each den is on dry ground of its biome where <see cref="SiteFits"/>, at least
    /// <see cref="WildStartGap"/> from every start and <see cref="WildGap"/> from every other lair and town, at a
    /// column picked from a random stream of the map's seed; a lair with no such column in
    /// <see cref="WildTries"/> tries is left out. Call after placing the starts' lairs and the city-states.
    /// Returns the lairs placed.
    /// </summary>
    public List<Lair> PlaceWild()
    {
        var placed = new List<Lair>();
        var monsters = _sim.Content.WildMonsters;
        long area = (long)World.SizeX * World.SizeZ;
        for (int k = 0; k < monsters.Count; k++)
        {
            var monster = monsters[k];
            var def = monster.Lair!;
            int count = WildCount(def.Count, area);
            var rng = new DeterministicRandom(_sim.Seed ^ 0x3A1D5EEDUL ^ (ulong)(k + 1) * 0x9E3779B97F4A7C15UL);
            if (def.Pass && PlaceInPasses(monster, placed) > 0) continue;
            if (def.Cave && PlaceInCaves(monster, count, placed) > 0) continue;
            for (int n = 0; n < count; n++)
            for (int t = 0; t < WildTries; t++)
            {
                int x = rng.Range(0, World.SizeX), z = rng.Range(0, World.SizeZ);
                if (_sim.Map.BiomeAt(x, z) != def.Biome || !WildSiteFits(x, z)) continue;
                if (Place(monster, x, z) is { } lair)
                {
                    placed.Add(lair);
                    if (def.Roam > 0) StartRoaming(lair);
                    if (def.SpreadTicks > 0) StartSpreading(lair);
                }
                break;
            }
        }
        return placed;
    }

    /// <summary>
    /// The giant spiders' nests in the hill caves (<see cref="RegionMap.HillCaves"/>): a lair of the monster at the bottom
    /// of each cave in map order, up to <paramref name="count"/>, its den at the back of the chamber, away from the way in,
    /// where its totem stands (<see cref="LairDef.Totem"/>). A cave within <see cref="WildStartGap"/> of a start, or within
    /// <see cref="WildGap"/> of another lair or a town, is passed over. Returns how many caves are taken (none on a map with no hill cave, and the
    /// monster is then spread over its biome instead).
    /// </summary>
    private int PlaceInCaves(UnitDef monster, int count, List<Lair> placed)
    {
        int got = 0;
        var caves = _sim.Map.HillCaves;
        for (int k = 0; k < caves.Count && got < count; k++)
        {
            var (x, y, z) = caves[k];
            if (!WildGapsFit(x, z)) continue;
            // The den at the back of the chamber: from its middle, away from the mouth.
            int dx = 0, dz = 0;
            if (k < _sim.Map.HillCaveMouths.Count)
            {
                var m = _sim.Map.HillCaveMouths[k];
                dx = Math.Sign(m.X - x);
                dz = Math.Sign(m.Z - z);
            }
            int back = Math.Max(0, Rts.Sim.World.RegionGenerator.HillCaveChamber / 2);
            if (Place(monster, x - dx * back, z - dz * back, near: y) is not { } lair) continue;
            placed.Add(lair);
            got++;
        }
        return got;
    }

    /// <summary>
    /// The wild side's totem (see <see cref="LairDef.Totem"/>) put up on the lair's den, its middle column on the den: the
    /// ground under it made solid and the room it takes cleared. Before the pack spawns, which stands round it.
    /// </summary>
    /// <summary>The glade (columns from the den) round a totem out in the open, where no tree stands.</summary>
    public const int TotemGlade = 7;
    // How far (columns) a tree's crown spreads from its trunk, for clearing a glade's leaves.
    private const int GladeCrown = 4;

    private void RaiseTotem(Lair lair, Economy.BuildingDef def)
    {
        var w = World;
        int x0 = lair.X - def.SizeX / 2, z0 = lair.Z - def.SizeZ / 2;
        int x1 = x0 + def.SizeX - 1, z1 = z0 + def.SizeZ - 1;
        if (!w.InBounds(x0, 0, z0) || !w.InBounds(x1, 0, z1) || lair.Y + def.Height >= w.SizeY) return;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            if (!BlockInfo.IsSolid(w.Get(x, lair.Y - 1, z))) w.Set(x, lair.Y - 1, z, BlockType.Soil);
            for (int y = lair.Y; y <= lair.Y + def.Height; y++)
                if (w.Get(x, y, z) != BlockType.Air) w.Set(x, y, z, BlockType.Air);
        }
        // A totem out in the open (the giant hornets' hive in the forests) stands in a glade: no tree within
        // TotemGlade columns of the den, nor any tree's crown over it, so it is seen. One underground (the spiders') is left as it is.
        bool open = true;
        for (int y = lair.Y; y < w.SizeY && open; y++)
        {
            var t = w.Get(lair.X, y, lair.Z);
            open = !BlockInfo.IsSolid(t) || BlockInfo.IsTrunk(t) || BlockInfo.IsLeaves(t);
        }
        if (open)
        {
            // Trunks within the glade go, and the leaves within it; so do the crowns of the trees felled (leaves within
            // GladeCrown columns of a trunk gone), but not the leaves hard by a trunk left standing.
            int reach = TotemGlade + GladeCrown;
            var felled = new List<(int X, int Z)>();
            var kept = new List<(int X, int Z)>();
            for (int z = lair.Z - reach - 1; z <= lair.Z + reach + 1; z++)
            for (int x = lair.X - reach - 1; x <= lair.X + reach + 1; x++)
            {
                if (!w.InBounds(x, 0, z)) continue;
                bool inGlade = (x - lair.X) * (x - lair.X) + (z - lair.Z) * (z - lair.Z) <= TotemGlade * TotemGlade;
                bool any = false;
                for (int y = Math.Max(1, lair.Y - 1); y < w.SizeY; y++)
                {
                    if (!BlockInfo.IsTrunk(w.Get(x, y, z))) continue;
                    any = true;
                    if (inGlade) w.Set(x, y, z, BlockType.Air);
                }
                if (any) (inGlade ? felled : kept).Add((x, z));
            }
            for (int z = lair.Z - reach; z <= lair.Z + reach; z++)
            for (int x = lair.X - reach; x <= lair.X + reach; x++)
            {
                if (!w.InBounds(x, 0, z)) continue;
                bool clear = (x - lair.X) * (x - lair.X) + (z - lair.Z) * (z - lair.Z) <= TotemGlade * TotemGlade;
                if (!clear)
                    foreach (var (fx, fz) in felled)
                        if (Math.Abs(fx - x) <= GladeCrown && Math.Abs(fz - z) <= GladeCrown) { clear = true; break; }
                if (clear)
                    foreach (var (kx, kz) in kept)
                        if (Math.Abs(kx - x) <= 1 && Math.Abs(kz - z) <= 1) { clear = false; break; }
                if (!clear) continue;
                for (int y = Math.Max(1, lair.Y - 1); y < w.SizeY; y++)
                    if (BlockInfo.IsLeaves(w.Get(x, y, z))) w.Set(x, y, z, BlockType.Air);
            }
        }
        var b = _sim.Buildings.Add(Simulation.WildPlayer, def, x0, lair.Y, z0);
        foreach (var t in def.Blocks) w.Set(b.X + t.X, b.Y + t.Y, b.Z + t.Z, t.Block);
        b.Placed = def.Blocks.Count;
        b.Work = def.BuildTicks;
        b.State = BuildingState.Active;
        lair.Totem = b.Id;
    }

    /// <summary>True if the lair has a totem (see <see cref="Lair.Totem"/>) and it is down.</summary>
    public bool TotemDown(Lair lair) => lair.Totem != 0 && _sim.Buildings.Get(lair.Totem) == null;

    /// <summary>The lair's totem while it stands, or null.</summary>
    public Building? TotemOf(Lair lair) => lair.Totem != 0 ? _sim.Buildings.Get(lair.Totem) : null;

    /// <summary>A unit of the player's struck building b: if it is a lair's totem, the player is the one who struck it last.</summary>
    internal void TotemHit(Building b, int player)
    {
        if (player < 0 || player >= Simulation.WildPlayer || b.Owner != Simulation.WildPlayer) return;
        foreach (var lair in _lairs)
            if (lair.Totem == b.Id && !lair.Cleared) lair.TotemHitBy = player;
    }

    /// <summary>
    /// The design doc's stone giants that "guard passes": a lair of the monster in the middle of each of the map's
    /// mountain passes (<see cref="RegionMap.Passes"/>), so the short way over the hills goes past it. A pass already
    /// held by a lair of the monster (a city-state's own, within <see cref="PassHeld"/> columns) keeps that one; a
    /// pass is otherwise taken whatever towns or lairs are near, but not within <see cref="WildStartGap"/> of a start.
    /// Returns how many passes are guarded (none on a map with no pass, and the monster is then spread over its biome
    /// instead).
    /// </summary>
    private int PlaceInPasses(UnitDef monster, List<Lair> placed)
    {
        int got = 0;
        foreach (var (x, _, z) in _sim.Map.Passes)
        {
            bool held = false;
            foreach (var other in _lairs)
                if (other.Monster == monster && IntMath.Distance(x, z, other.X, other.Z) <= PassHeld) held = true;
            if (held) { got++; continue; }
            bool nearStart = false;
            foreach (var s in _sim.Map.Starts)
                if (IntMath.Distance(x, z, s.X, s.Z) < WildStartGap) nearStart = true;
            if (nearStart || !SiteFits(x, z) || Place(monster, x, z) is not { } lair) continue;
            placed.Add(lair);
            got++;
        }
        return got;
    }

    /// <summary>Columns from a pass's middle within which a lair of the pass's monster already guards it.</summary>
    public const int PassHeld = 8;

    /// <summary>The nearest a herd of wild game is placed to a start (blocks): near enough to hunt from a first town.</summary>
    public const int HerdStartGap = 48;
    /// <summary>The nearest a herd is placed to another herd, a lair or a town (blocks).</summary>
    public const int HerdGap = 32;

    /// <summary>How much further than <see cref="HerdStartGap"/> from its start a start's own herd may graze (blocks).</summary>
    public const int HerdStartSpread = 40;

    /// <summary>
    /// Match setup: the herds of wild game (<see cref="Content.ContentDb.GameAnimals"/>, in data order). First one
    /// herd of each for every start, on dry ground of its biome between <see cref="HerdStartGap"/> and
    /// <see cref="HerdStartGap"/> + <see cref="HerdStartSpread"/> blocks from it, so every town has game to hunt; then,
    /// like <see cref="PlaceWild"/>, <see cref="LairDef.Count"/> for each 512 by 512 blocks of map anywhere on their
    /// biome. Every herd keeps <see cref="HerdGap"/> from the others, the lairs and the towns, and the spots come from
    /// a random stream of their own. Every herd roams. Call last, after every lair is placed. Returns the herds placed.
    /// </summary>
    public List<Lair> PlaceHerds()
    {
        var placed = new List<Lair>();
        var game = _sim.Content.GameAnimals;
        long area = (long)World.SizeX * World.SizeZ;
        for (int k = 0; k < game.Count; k++)
        {
            var animal = game[k];
            var def = animal.Lair!;
            int count = (int)(def.Count * area / (512L * 512L));
            var rng = new DeterministicRandom(_sim.Seed ^ 0x4E3D7A11UL ^ (ulong)(k + 1) * 0x9E3779B97F4A7C15UL);
            foreach (var start in _sim.Map.Starts)
            for (int t = 0; t < WildTries; t++)
            {
                int r = HerdStartGap + HerdStartSpread;
                int x = start.X + rng.Range(-r, r + 1), z = start.Z + rng.Range(-r, r + 1);
                if (IntMath.Distance(x, z, start.X, start.Z) > r || !World.InBounds(x, 0, z)) continue;
                if (_sim.Map.BiomeAt(x, z) != def.Biome || !HerdSiteFits(x, z)) continue;
                if (Place(animal, x, z) is { } herd)
                {
                    placed.Add(herd);
                    if (def.Roam > 0) StartRoaming(herd);
                }
                break;
            }
            for (int n = 0; n < count; n++)
            for (int t = 0; t < WildTries; t++)
            {
                int x = rng.Range(0, World.SizeX), z = rng.Range(0, World.SizeZ);
                if (_sim.Map.BiomeAt(x, z) != def.Biome || !HerdSiteFits(x, z)) continue;
                if (Place(animal, x, z) is { } herd)
                {
                    placed.Add(herd);
                    if (def.Roam > 0) StartRoaming(herd);
                }
                break;
            }
        }
        return placed;
    }

    private bool HerdSiteFits(int x, int z)
    {
        foreach (var s in _sim.Map.Starts)
            if (IntMath.Distance(x, z, s.X, s.Z) < HerdStartGap) return false;
        foreach (var lair in _lairs)
            if (IntMath.Distance(x, z, lair.X, lair.Z) < HerdGap) return false;
        foreach (var herd in _herds)
            if (IntMath.Distance(x, z, herd.X, herd.Z) < HerdGap) return false;
        foreach (var town in _sim.Cities.Towns)
            if (!town.Deep && IntMath.Distance(x, z, town.X, town.Z) < HerdGap) return false;
        if (!SiteFits(x, z)) return false;
        int y = Walk.GroundLevel(World, x, z);
        return World.Get(x, y - 1, z) is not (BlockType.Water or BlockType.Lava or BlockType.Bog or BlockType.Crust or BlockType.Quicksand or BlockType.Scree or BlockType.LooseRock);
    }

    /// <summary>The smallest map (blocks across) whose cavern has monsters in it.</summary>
    public const int CavernMapSize = 384;
    /// <summary>The furthest a cavern lair is from the cavern's way in, walking (columns), and the most cells looked at.</summary>
    private const int CavernReach = 160, CavernCells = 200_000;

    /// <summary>
    /// Match setup: the lairs in the map's cavern (issue #65), for each monster with a <see cref="LairDef.Cavern"/>
    /// in data order, <see cref="LairDef.Count"/> of them. The cavern's floor is walked from its first chamber (where
    /// the way in from the surface comes down) under the roof only; a monster that lives along the cavern has its
    /// dens spread out along the walk, at (n + 1) / (count + 1) of the way to the cell furthest in, and a boss (deepest)
    /// is at that furthest cell. The den and pack stand on the cave floor. A bigger map's further caverns (issue #67,
    /// see <see cref="RegionMap.Caverns"/>) get the same, bar the boss, who lives in the main one only. No cavern, or a map smaller than
    /// <see cref="CavernMapSize"/> across (test and scripted maps), no lairs. Returns the lairs placed.
    /// </summary>
    public List<Lair> PlaceCavern()
    {
        var placed = new List<Lair>();
        var monsters = _sim.Content.CavernMonsters;
        if (monsters.Count == 0 || _sim.Map.Caverns.Count == 0) return placed;
        if (World.SizeX < CavernMapSize || World.SizeZ < CavernMapSize) return placed;
        for (int c = 0; c < _sim.Map.Caverns.Count; c++)
        {
            var cave = _sim.Map.Caverns[c];
            // On a deep map the cavern's tunnel down to the deep caves is not the cavern: its lairs stay in it.
            var floor = CavernFloor(cave.X, cave.Y, cave.Z, _sim.Map.Deeps.Count > 0 ? cave.Y - CavernDrop : int.MinValue);
            _groundCache = null;
            if (floor.Count == 0) continue;
            int deepest = floor[^1].Steps;
            foreach (var monster in monsters)
            {
                var def = monster.Lair!;
                // The boss (deepest) keeps to the main cavern; a further one (issue #67) has only the others.
                if (c > 0 && def.Cavern == CavernSpot.Deepest) continue;
                for (int n = 0; n < def.Count; n++)
                {
                    int want = def.Cavern == CavernSpot.Deepest ? deepest : deepest * (n + 1) / (def.Count + 1);
                    // The first cell that far in (the floor is in walking order).
                    int k = floor.FindIndex(f => f.Steps >= want);
                    var (x, y, z, _) = floor[k < 0 ? floor.Count - 1 : k];
                    if (Place(monster, x, z, near: y) is { } lair) placed.Add(lair);
                }
            }
        }
        PlaceDeeps(monsters, placed);
        return placed;
    }

    /// <summary>How far below its first chamber (layers) a hill cavern's lairs may be, on a map with deep caves.</summary>
    public const int CavernDrop = 8;

    /// <summary>
    /// The nests in the deep caves (see <see cref="RegionMap.Deeps"/>): in every
    /// <see cref="Rts.Sim.World.RegionGenerator.DeepNestEvery"/>th chamber, a lair of a monster that lives along a cavern, taking
    /// those monsters in turn, bar a chamber under a start (within <see cref="WildStartGap"/>).
    /// </summary>
    private void PlaceDeeps(IReadOnlyList<UnitDef> monsters, List<Lair> placed)
    {
        var along = new List<UnitDef>();
        foreach (var m in monsters) if (m.Lair!.Cavern == CavernSpot.Along) along.Add(m);
        if (along.Count == 0) return;
        for (int k = 0, n = 0; k < _sim.Map.Deeps.Count; k += Rts.Sim.World.RegionGenerator.DeepNestEvery)
        {
            var (x, y, z) = _sim.Map.Deeps[k];
            bool underStart = false;
            foreach (var s in _sim.Map.Starts)
                if (IntMath.Distance(x, z, s.X, s.Z) < WildStartGap) underStart = true;
            if (underStart) continue;
            if (Place(along[n++ % along.Count], x, z, near: y) is { } lair) placed.Add(lair);
        }
    }

    /// <summary>
    /// The cave floor reachable on foot from the cavern's first chamber, staying under the roof (a solid block
    /// above, so not up the way in) and no lower than <paramref name="lowest"/>, in walking order: each cell with its
    /// steps from the start.
    /// </summary>
    public List<(int X, int Y, int Z, int Steps)> CavernFloor(int cx, int cy, int cz, int lowest = int.MinValue)
    {
        var cells = new List<(int X, int Y, int Z, int Steps)>();
        int sy = Walk.NearestStandLevel(World, cx, cz, cy, GuardHeight * 2);
        if (sy < 0 || !Roofed(cx, sy, cz)) return cells;
        var seen = new HashSet<long>();
        long Key(int x, int y, int z) => ((long)x * World.SizeY + y) * World.SizeZ + z;
        cells.Add((cx, sy, cz, 0));
        seen.Add(Key(cx, sy, cz));
        for (int head = 0; head < cells.Count && cells.Count < CavernCells; head++)
        {
            var (x, y, z, steps) = cells[head];
            if (steps >= CavernReach) continue;
            foreach (var (dx, dz) in Steps4)
            {
                int nx = x + dx, nz = z + dz;
                if (!World.InBounds(nx, 0, nz) || !Walk.TryStep(World, x, z, y, nx, nz, out int ny)) continue;
                if (ny < lowest || !Roofed(nx, ny, nz) || !seen.Add(Key(nx, ny, nz))) continue;
                cells.Add((nx, ny, nz, steps + 1));
            }
        }
        return cells;
    }

    private static readonly (int X, int Z)[] Steps4 = { (1, 0), (0, 1), (-1, 0), (0, -1) };

    // Underground: the column's ground (not counting tree tops) is well above a unit standing with its feet at y.
    private int[]? _groundCache;
    private bool Roofed(int x, int y, int z)
    {
        _groundCache ??= Array.ConvertAll(new int[World.SizeX * World.SizeZ], _ => int.MinValue);
        ref int g = ref _groundCache[x * World.SizeZ + z];
        if (g == int.MinValue) g = Walk.GroundLevel(World, x, z);
        return g > y + 2;
    }

    private bool WildSiteFits(int x, int z)
    {
        if (!WildGapsFit(x, z) || !SiteFits(x, z)) return false;
        int y = Walk.GroundLevel(World, x, z);
        return World.Get(x, y - 1, z) is not (BlockType.Water or BlockType.Lava or BlockType.Bog or BlockType.Crust or BlockType.Quicksand or BlockType.Scree or BlockType.LooseRock);
    }

    /// <summary>True if column (x, z) is <see cref="WildStartGap"/> from every start and <see cref="WildGap"/> from every lair and town on the land.</summary>
    private bool WildGapsFit(int x, int z)
    {
        foreach (var s in _sim.Map.Starts)
            if (IntMath.Distance(x, z, s.X, s.Z) < WildStartGap) return false;
        foreach (var lair in _lairs)
            if (IntMath.Distance(x, z, lair.X, lair.Z) < WildGap) return false;
        foreach (var town in _sim.Cities.Towns)
            if (!town.Deep && IntMath.Distance(x, z, town.X, town.Z) < WildGap) return false;
        return true;
    }

    /// <summary>
    /// Puts a lair of the monster with its den at column (x, z), on the ground there, and spawns its pack on
    /// the ground around it (every other column, nearest first, skipping columns more than
    /// <see cref="GuardHeight"/> levels off the den's). With <paramref name="near"/>, the den and pack stand at
    /// the levels nearest it rather than on the highest ground (under the dusk tree's branches, say). Null if
    /// there is no ground at (x, z).
    /// </summary>
    public Lair? Place(UnitDef monster, int x, int z, int near = -1)
    {
        if (monster.Lair is not { } def) throw new ArgumentException($"'{monster.Id}' keeps no lair", nameof(monster));
        if (!World.InBounds(x, 0, z)) return null;
        int Ground(int gx, int gz) => near >= 0 ? Walk.NearestStandLevel(World, gx, gz, near, GuardHeight) : Walk.GroundLevel(World, gx, gz);
        int y = Ground(x, z);
        if (y < 0) return null;
        var lair = new Lair(_nextId++, monster, x, y, z);
        if (def.Totem is { } totem) RaiseTotem(lair, totem);
        if (def.Lurk)
        {
            FindPools(lair, def.Guard, def.Pack);
            foreach (var (px, py, pz) in lair.PoolSpots)
            {
                int id = _sim.SpawnUnit(Simulation.WildPlayer, monster.Id, px, pz, py);
                if (id > 0) lair.PackIds.Add(id);
            }
        }
        foreach (var (ox, oz) in PackSpots)
        {
            if (lair.PackIds.Count == def.Pack) break;
            int px = x + ox, pz = z + oz;
            if (!World.InBounds(px, 0, pz)) continue;
            int py = Ground(px, pz);
            if (py < 0 || Math.Abs(py - y) > GuardHeight) continue;
            int id = _sim.SpawnUnit(Simulation.WildPlayer, monster.Id, px, pz, py);
            if (id > 0) lair.PackIds.Add(id);
        }
        if (def.GrowTicks > 0) lair.NextBirth = _sim.Tick + def.GrowTicks;
        (monster.IsGame ? _herds : _lairs).Add(lair);
        return lair;
    }

    /// <summary>
    /// A lurking lair's pools (see <see cref="Lair.Pools"/>): up to <paramref name="most"/> columns within
    /// <paramref name="radius"/> of the den where a unit stands with its feet in shallow water, no more than
    /// <see cref="GuardHeight"/> levels off the den's, nearest the den first (ties by row, then column), each at least
    /// two columns from the others so the pack spreads round the water.
    /// </summary>
    private void FindPools(Lair lair, int radius, int most)
    {
        var found = new List<(int D, int X, int Y, int Z)>();
        for (int z = lair.Z - radius; z <= lair.Z + radius; z++)
        for (int x = lair.X - radius; x <= lair.X + radius; x++)
        {
            int dx = x - lair.X, dz = z - lair.Z, d = dx * dx + dz * dz;
            if (d > radius * radius || !World.InBounds(x, 0, z)) continue;
            int y = Walk.GroundLevel(World, x, z);
            if (y < 0 || Math.Abs(y - lair.Y) > GuardHeight || World.Get(x, y, z) != BlockType.Water) continue;
            found.Add((d, x, y, z));
        }
        found.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        foreach (var (_, x, y, z) in found)
        {
            if (lair.PoolSpots.Count == most) break;
            bool apart = true;
            foreach (var (px, _, pz) in lair.PoolSpots)
                if (Math.Abs(px - x) < 2 && Math.Abs(pz - z) < 2) apart = false;
            if (apart) lair.PoolSpots.Add((x, y, z));
        }
    }

    /// <summary>
    /// True if unit i is a lurking monster lying in the water (see <see cref="LairDef.Lurk"/>): wild, with nothing to
    /// fight, its feet in water. Players with fog of war on see it only from close by (see
    /// <see cref="VisionSystem.SpotSubmerged"/>), and the game draws it sunk to its eyes.
    /// </summary>
    public bool Submerged(int i)
    {
        var u = U;
        if ((uint)i >= (uint)u.Count || !IsWild(i) || u.Target[i] != 0 || u.Subdued[i]) return false;
        if (Def(i).Lair is not { Lurk: true }) return false;
        return World.Get(u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt()) == BlockType.Water;
    }

    /// <summary>
    /// Unit i strikes a blow: if it is a lurking monster that came up out of the water at its target and has not
    /// struck yet, the extra percent of damage its first blow does (<see cref="LairDef.Ambush"/>), and it has
    /// sprung its ambush; otherwise 0.
    /// </summary>
    internal int Spring(int i)
    {
        if (_ambush.Count == 0) return 0;
        int k = _ambush.BinarySearch(U.Id[i]);
        if (k < 0) return 0;
        _ambush.RemoveAt(k);
        return Def(i).Lair?.Ambush ?? 0;
    }

    /// <summary>True if unit i came up out of the water at someone and has not struck its first blow yet.</summary>
    public bool Ambushing(int i) => _ambush.Count > 0 && _ambush.BinarySearch(U.Id[i]) >= 0;

    /// <summary>
    /// True if a den can go at column (x, z): ground that is not under water, well inside the map and away from
    /// buildings and city-state towns.
    /// </summary>
    internal bool SiteFits(int x, int z)
    {
        const int margin = 8;
        if (x < margin || z < margin || x >= World.SizeX - margin || z >= World.SizeZ - margin) return false;
        int y = Walk.GroundLevel(World, x, z);
        if (y < 0 || World.Get(x, y, z) != BlockType.Air) return false;
        foreach (var b in _sim.Buildings.All)
            if (b.ColumnDistance(x, z) <= BuildingGap) return false;
        foreach (var town in _sim.Cities.Towns)
            if (!town.Deep && town.ColumnDistance(x, z) <= BuildingGap) return false;
        return true;
    }

    private static (int X, int Z)[] MakePackSpots()
    {
        var spots = new List<(int X, int Z)>();
        for (int z = -8; z <= 8; z += 2)
        for (int x = -8; x <= 8; x += 2)
            if (x != 0 || z != 0) spots.Add((x, z));
        spots.Sort((a, b) =>
        {
            int c = (a.X * a.X + a.Z * a.Z).CompareTo(b.X * b.X + b.Z * b.Z);
            if (c != 0) return c;
            c = a.Z.CompareTo(b.Z);
            return c != 0 ? c : a.X.CompareTo(b.X);
        });
        return spots.ToArray();
    }

    // --- The tick ----------------------------------------------------------------------------------

    /// <summary>
    /// After the fighting: each lair forgets the monsters it lost, is cleared when none are left (paying out
    /// its reward), and every <see cref="GuardInterval"/> ticks sets its pack on intruders or sends it home.
    /// </summary>
    public void Step()
    {
        if (_herds.Count > 0) StepHerds();
        if (_lairs.Count == 0) return;
        long tick = _sim.Tick;
        // An ambusher that died, was beaten, or came out of the water with nobody to fight springs no ambush.
        if (_ambush.Count > 0) _ambush.RemoveAll(id => U.IndexOf(id) is var i && (i < 0 || !IsWild(i) || (U.Target[i] == 0 && !Submerged(i))));
        _founded.Clear();
        foreach (var lair in _lairs)
        {
            // A lair cleared by knocking its totem down (see LairDef.Totem) still guards its den while any of its pack is left.
            if (lair.Cleared && lair.PackIds.Count == 0) continue;
            lair.PackIds.RemoveAll(id => U.IndexOf(id) is var i && (i < 0 || !IsWild(i)));
            // A tracker that lost its quarry (it died, or the tracker gave up the fight) is a plain guard again.
            lair.TrackerIds.RemoveAll(id => U.IndexOf(id) is var i && (i < 0 || !IsWild(i) || U.Target[i] == 0));
            if (!lair.Cleared && (lair.Totem != 0 ? TotemDown(lair) : lair.PackIds.Count == 0)) Clear(lair);
            // A nest whose totem stands (the giant hornets' hive) hatches its swarm again even once every one of it is dead.
            if (lair.PackIds.Count == 0 && !lair.Cleared && lair.Totem != 0 && lair.NextBirth > 0 && tick >= lair.NextBirth) Grow(lair);
            if (lair.PackIds.Count > 0)
            {
                if (lair.Monster.Lair!.Ambush > 0) LieInWait(lair);
                if (!lair.Cleared && lair.NextBirth > 0 && tick >= lair.NextBirth) Grow(lair);
                if (!lair.Cleared && lair.NextSpread > 0 && tick >= lair.NextSpread) Spread(lair);
                if ((tick + lair.Id) % GuardInterval == 0) Guard(lair);
            }
        }
        if (_founded.Count > 0) _lairs.AddRange(_founded);
    }

    // --- Spreading ---------------------------------------------------------------------------------

    /// <summary>The furthest a new lair is founded from the den of the lair it spread from (blocks).</summary>
    public const int SpreadRange = 64;
    /// <summary>The nearest a new lair is founded to any player's building (blocks): packs spread through the wild, not into towns.</summary>
    public const int SpreadBuildingGap = 40;
    /// <summary>Columns tried each time a lair spreads; with none that fits it tries again next time.</summary>
    private const int SpreadTries = 40;
    // Lairs founded this tick, added to the list once the tick's look at every lair is done.
    private readonly List<Lair> _founded = new();

    /// <summary>
    /// The most lairs of the monster spread across the map that stand at once (see <see cref="LairDef.SpreadTo"/>), for
    /// the map's size: at least one more than were placed at match start.
    /// </summary>
    public int SpreadCap(UnitDef monster)
    {
        var def = monster.Lair!;
        long area = (long)World.SizeX * World.SizeZ;
        int placed = Math.Max(1, (int)(def.Count * area / (512L * 512L)));
        return Math.Max(placed + 1, (int)(def.SpreadTo * area / (512L * 512L)));
    }

    /// <summary>
    /// The lair founds new ones from now on (see <see cref="LairDef.SpreadTicks"/>), the first once twice that time has
    /// passed (a lair settles in before it spreads), then every time; nothing for a monster whose lairs do not spread.
    /// </summary>
    public void StartSpreading(Lair lair)
    {
        int every = lair.Monster.Lair!.SpreadTicks;
        if (every > 0) lair.NextSpread = _sim.Tick + 2L * every;
    }

    /// <summary>The lairs of the monster spread across the map (placed there or founded since) that are not cleared.</summary>
    public int Standing(UnitDef monster)
    {
        int n = 0;
        foreach (var lair in _lairs)
            if (lair.Monster == monster && !lair.Cleared && lair.NextSpread > 0) n++;
        foreach (var lair in _founded)
            if (lair.Monster == monster) n++;
        return n;
    }

    /// <summary>
    /// A lair's time to spread has come (see <see cref="LairDef.SpreadTicks"/>): if its pack is at full strength (at
    /// least <see cref="LairDef.Pack"/> of it in the wild), none of it is fighting, and fewer than
    /// <see cref="SpreadCap"/> lairs of its monster stand, a new pack of <see cref="LairDef.Pack"/> is born by its den
    /// and sets off for a new den of its own: a column within <see cref="SpreadRange"/> of the old den, in the same
    /// biome, that fits as a lair spread across the map does at match start (well away from the starts, other lairs and
    /// towns) and is at least <see cref="SpreadBuildingGap"/> from every building. The new lair roams, grows and spreads
    /// as its monster's do. It tries again a while later either way.
    /// </summary>
    private void Spread(Lair lair)
    {
        var def = lair.Monster.Lair!;
        lair.NextSpread = _sim.Tick + def.SpreadTicks;
        if (lair.PackIds.Count < def.Pack) return;
        foreach (int id in lair.PackIds)
            if (U.Target[U.IndexOf(id)] != 0) return;
        if (Standing(lair.Monster) >= SpreadCap(lair.Monster)) return;
        for (int t = 0; t < SpreadTries; t++)
        {
            const int r = SpreadRange, w = 2 * r + 1;
            uint h = DeterministicRandom.Hash(lair.Id * SpreadTries + t, (int)(_sim.Tick & 0x7FFFFFFF), _sim.Seed ^ 0x5E7D0A11UL);
            int x = lair.X + (int)(h % w) - r, z = lair.Z + (int)(h / w % w) - r;
            if (IntMath.Distance(x, z, lair.X, lair.Z) > r || !World.InBounds(x, 0, z)) continue;
            if (_sim.Map.BiomeAt(x, z) != def.Biome || !SpreadSiteFits(x, z)) continue;
            int y = Walk.GroundLevel(World, x, z);
            if (Math.Abs(y - lair.Y) > GuardHeight * 3) continue;
            Found(lair, x, y, z);
            return;
        }
    }

    private bool SpreadSiteFits(int x, int z)
    {
        if (!WildSiteFits(x, z)) return false;
        foreach (var other in _founded)
            if (IntMath.Distance(x, z, other.X, other.Z) < WildGap) return false;
        foreach (var b in _sim.Buildings.All)
            if (b.ColumnDistance(x, z) < SpreadBuildingGap) return false;
        return true;
    }

    /// <summary>
    /// A new lair of the parent's monster with its den at (x, y, z): its pack is born round the parent's den, on the
    /// first of the pack's spots there that are no more than <see cref="GuardHeight"/> levels off it, and walks to the
    /// new den.
    /// </summary>
    private void Found(Lair parent, int x, int y, int z)
    {
        var monster = parent.Monster;
        var def = monster.Lair!;
        var lair = new Lair(_nextId++, monster, x, y, z) { Parent = parent.Id };
        foreach (var (ox, oz) in PackSpots)
        {
            if (lair.PackIds.Count == def.Pack) break;
            int px = parent.X + ox, pz = parent.Z + oz;
            if (!World.InBounds(px, 0, pz)) continue;
            int py = Walk.GroundLevel(World, px, pz);
            if (py < 0 || Math.Abs(py - parent.Y) > GuardHeight) continue;
            int id = _sim.SpawnUnit(Simulation.WildPlayer, monster.Id, px, pz, py);
            if (id > 0) lair.PackIds.Add(id);
        }
        if (lair.PackIds.Count == 0) return;
        if (def.GrowTicks > 0) lair.NextBirth = _sim.Tick + def.GrowTicks;
        if (def.Roam > 0) StartRoaming(lair);
        StartSpreading(lair);
        _founded.Add(lair);
        _sim.Issue(new MoveCommand(Simulation.WildPlayer, lair.PackIds.ToArray(), x, z, y));
    }

    /// <summary>
    /// The herds of wild game: each forgets the animals it lost and is gone for good once none are left; the rest
    /// grow back (see <see cref="LairDef.GrowTicks"/>), wander on together and call home any that strayed past the
    /// leash. Game never fights and goes after no one.
    /// </summary>
    private void StepHerds()
    {
        long tick = _sim.Tick;
        foreach (var herd in _herds)
        {
            if (herd.Cleared) continue;
            herd.PackIds.RemoveAll(id => U.IndexOf(id) is var i && (i < 0 || !IsWild(i)));
            if (herd.PackIds.Count == 0)
            {
                herd.Cleared = true;
                continue;
            }
            if (herd.NextBirth > 0 && tick >= herd.NextBirth) Grow(herd);
            if ((tick + herd.Id) % GuardInterval == 0) Graze(herd);
        }
    }

    /// <summary>A herd with nothing chasing it home wanders on when its time comes; strays past the leash walk back.</summary>
    private void Graze(Lair herd)
    {
        var u = U;
        Fix64 leash = Fix64.FromInt(herd.Monster.Lair!.Leash);
        _home.Clear();
        foreach (int id in herd.PackIds)
        {
            int i = u.IndexOf(id);
            bool goingHome = u.Order[i] == UnitOrder.Move && u.OrderX[i] == herd.HomeX && u.OrderZ[i] == herd.HomeZ;
            if (!goingHome && Distance2(i, herd) > leash * leash) _home.Add(id);
        }
        if (herd.Roam > 0 && _sim.Tick >= herd.NextRoam && MoveOn(herd))
        {
            _home.Clear();
            _home.AddRange(herd.PackIds);
        }
        if (_home.Count > 0) _sim.Issue(new MoveCommand(Simulation.WildPlayer, _home.ToArray(), herd.HomeX, herd.HomeZ, herd.HomeY));
    }

    /// <summary>
    /// A growing pack's time has come (see <see cref="LairDef.GrowTicks"/>): one more of its monster is born on the
    /// ground by the den, at the first of the pack's spots round it that is no more than <see cref="GuardHeight"/>
    /// levels off the den's, unless the pack is already <see cref="LairDef.GrowTo"/> strong. The next birth is
    /// due a while later either way.
    /// </summary>
    private void Grow(Lair lair)
    {
        var def = lair.Monster.Lair!;
        lair.NextBirth = _sim.Tick + def.GrowTicks;
        if (lair.PackIds.Count >= def.GrowTo) return;
        for (int k = 0; k < PackSpots.Length; k++)
        {
            var (ox, oz) = PackSpots[(lair.PackIds.Count + k) % PackSpots.Length];
            int px = lair.X + ox, pz = lair.Z + oz;
            if (!World.InBounds(px, 0, pz)) continue;
            int py = Walk.GroundLevel(World, px, pz);
            if (py < 0 || Math.Abs(py - lair.Y) > GuardHeight) continue;
            int id = _sim.SpawnUnit(Simulation.WildPlayer, lair.Monster.Id, px, pz, py);
            if (id > 0) lair.PackIds.Add(id);
            return;
        }
    }

    /// <summary>
    /// The lair's pack is gone, or for a lair with a totem (see <see cref="LairDef.Totem"/>) the totem is down: whoever got
    /// the last of the pack, or struck the totem last, gets its reward, unless they tame monsters, and influence with the
    /// city-states it was near (see <see cref="Economy.CityStateSystem"/>).
    /// </summary>
    private void Clear(Lair lair)
    {
        lair.Cleared = true;
        lair.ClearedBy = lair.Totem != 0 ? lair.TotemHitBy : lair.LastBeatenBy;
        int p = lair.ClearedBy;
        Economy.Discover(p, Rts.Sim.Content.Discovery.Lair);
        if (lair.Monster.Lair!.Pass) Economy.Discover(p, Rts.Sim.Content.Discovery.Giant);
        if (p < 0 || p == Simulation.WildPlayer || Economy.Faction(p).TamesMonsters) return;
        var reward = lair.Monster.Lair!.Reward;
        if (reward.Length == 0 || Economy.NearestStorage(p, lair.X, lair.Z) is not { } store) return;
        foreach (var g in reward) store.Stock[(int)g.Good] += g.Amount;
    }

    /// <summary>
    /// The pack's monsters with nothing to do go after the intruder nearest the den, or walk back to the den
    /// if there is none; those past the leash break off and go home.
    /// </summary>
    private void Guard(Lair lair)
    {
        var u = U;
        var def = lair.Monster.Lair!;
        Fix64 leash = Fix64.FromInt(def.Leash), home = Fix64.FromInt(HomeRadius);
        Fix64 trackLeash = Fix64.FromInt(Math.Max(def.Leash, TrackRange));
        int intruder = -2; // not looked for yet
        bool fighting = false;
        _home.Clear();
        for (int k = 0; k < lair.PackIds.Count; k++)
        {
            int id = lair.PackIds[k];
            int i = u.IndexOf(id);
            Fix64 d2 = Distance2(i, lair);
            bool goingHome = u.Order[i] == UnitOrder.Move && !u.FightsOnTheWay(i)
                && ((u.OrderX[i] == lair.HomeX && u.OrderZ[i] == lair.HomeZ) || HeadedForPool(lair, k, i));
            if (u.Target[i] != 0) fighting = true;
            Fix64 reach = lair.TrackerIds.Contains(id) ? trackLeash : leash;
            if (d2 > reach * reach)
            {
                lair.TrackerIds.Remove(id);
                if (!goingHome) _home.Add(id);
                continue;
            }
            // Busy: fighting, or chasing something it spotted itself.
            if (u.Target[i] != 0 || (u.Order[i] != UnitOrder.Idle && !goingHome)) continue;
            if (intruder == -2) intruder = Intruder(lair);
            if (intruder >= 0) Attack(i, intruder);
            else if (Track(lair, i) is int quarry and >= 0)
            {
                Attack(i, quarry);
                lair.TrackerIds.Add(id);
            }
            else if (lair.PoolSpots.Count > 0)
            {
                // A lurker goes back to its own pool and lies in the water there.
                var (px, py, pz) = lair.PoolSpots[k % lair.PoolSpots.Count];
                bool there = u.X[i].FloorToInt() == px && u.Z[i].FloorToInt() == pz;
                bool headed = u.Order[i] == UnitOrder.Move && u.OrderX[i] == px && u.OrderZ[i] == pz;
                // Not a plain move order: that would stop at the water's edge.
                if (!there && !headed) _sim.SendUnit(i, px, pz, py);
                continue;
            }
            else if (!goingHome && d2 > home * home) _home.Add(id);
        }
        if (lair.Roam > 0 && !fighting && intruder < 0 && _sim.Tick >= lair.NextRoam && MoveOn(lair))
        {
            // The pack wanders on together: all of it heads for the new spot.
            _home.Clear();
            _home.AddRange(lair.PackIds);
        }
        if (_home.Count > 0) _sim.Issue(new MoveCommand(Simulation.WildPlayer, _home.ToArray(), lair.HomeX, lair.HomeZ, lair.HomeY));
    }

    // --- Roaming -----------------------------------------------------------------------------------

    /// <summary>Seconds a roaming pack stays at a spot before it wanders on, unless it is fighting.</summary>
    public const int RoamSeconds = 20;
    /// <summary>Spots tried each time a roaming pack wanders on; with none that fits it stays where it is.</summary>
    private const int RoamTries = 8;

    /// <summary>The lair's pack roams from now on, as far as its monster's <see cref="LairDef.Roam"/> from the den.</summary>
    public void StartRoaming(Lair lair)
    {
        lair.Roam = lair.Monster.Lair!.Roam;
        lair.NextRoam = _sim.Tick + RoamSeconds * Simulation.TicksPerSecond + lair.Id % GuardInterval;
    }

    /// <summary>
    /// A roaming pack picks the next spot it wanders to: a column within its roam of the den, on dry ground no
    /// further than <see cref="GuardHeight"/> levels from the den and away from buildings, picked from the map's
    /// seed, the lair and the tick. False if none of <see cref="RoamTries"/> fits (it tries again later).
    /// </summary>
    private bool MoveOn(Lair lair)
    {
        lair.NextRoam = _sim.Tick + RoamSeconds * Simulation.TicksPerSecond;
        int r = lair.Roam;
        for (int t = 0; t < RoamTries; t++)
        {
            uint h = DeterministicRandom.Hash(lair.Id * RoamTries + t, (int)(_sim.Tick & 0x7FFFFFFF), _sim.Seed ^ 0x2B0A3F17UL);
            int x = lair.X + (int)(h % (uint)(2 * r + 1)) - r, z = lair.Z + (int)(h / (uint)(2 * r + 1) % (uint)(2 * r + 1)) - r;
            if (IntMath.Distance(x, z, lair.X, lair.Z) > r || !SiteFits(x, z)) continue;
            int y = Walk.GroundLevel(World, x, z);
            if (Math.Abs(y - lair.Y) > GuardHeight * 3) continue;
            if (World.Get(x, y - 1, z) is BlockType.Water or BlockType.Lava or BlockType.Bog or BlockType.Crust or BlockType.Quicksand or BlockType.Scree or BlockType.LooseRock) continue;
            lair.HomeX = x;
            lair.HomeY = y;
            lair.HomeZ = z;
            return true;
        }
        return false;
    }

    /// <summary>
    /// For a monster that tracks blood: the unit whose fresh trail (see <see cref="World.BloodSystem.FreshTrail"/>)
    /// is the freshest within the monster's sight, if that unit is still within <see cref="TrackRange"/> of the
    /// den and is not wild or beaten; or -1.
    /// </summary>
    private int Track(Lair lair, int i)
    {
        var def = Def(i);
        if (!def.TracksBlood || _sim.Blood.Count == 0) return -1;
        var u = U;
        Fix64 range = Fix64.FromInt(TrackRange);
        int id = _sim.Blood.FreshTrail(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), def.Sight.FloorToInt(), source =>
        {
            int j = u.IndexOf(source);
            return j >= 0 && !IsWild(j) && !u.Subdued[j] && !Spares(i, j) && Distance2(j, lair) <= range * range;
        });
        return id == 0 ? -1 : u.IndexOf(id);
    }

    /// <summary>The unit nearest the den (ties to the lowest id) within its guard radius that is not wild or beaten, or -1.</summary>
    private int Intruder(Lair lair)
    {
        var u = U;
        Fix64 guard = Fix64.FromInt(lair.Monster.Lair!.Guard);
        Fix64 r2 = guard * guard;
        int best = -1;
        Fix64 bestD = Fix64.MaxValue;
        for (int j = 0; j < u.Count; j++)
        {
            if (IsWild(j) || u.Subdued[j] || Spared(j) || Math.Abs(u.Y[j] - lair.HomeY) > GuardHeight) continue;
            Fix64 d = Distance2(j, lair);
            if (d > r2) continue;
            if (d < bestD || (d == bestD && u.Id[j] < u.Id[best]))
            {
                best = j;
                bestD = d;
            }
        }
        return best;
    }

    /// <summary>True if unit i, the k-th of a lurking lair's pack, is walking back to its pool.</summary>
    private bool HeadedForPool(Lair lair, int k, int i)
    {
        if (lair.PoolSpots.Count == 0) return false;
        var (px, _, pz) = lair.PoolSpots[k % lair.PoolSpots.Count];
        return U.OrderX[i] == px && U.OrderZ[i] == pz;
    }

    /// <summary>
    /// Each of a lurking lair's pack lying in the water now lies in wait: whatever it goes after first, whether the
    /// lair sent it or it saw someone itself, its first blow coming up out of the water is an ambush.
    /// </summary>
    private void LieInWait(Lair lair)
    {
        foreach (int id in lair.PackIds)
        {
            if (!Submerged(U.IndexOf(id))) continue;
            int k = _ambush.BinarySearch(id);
            if (k < 0) _ambush.Insert(~k, id);
        }
    }

    /// <summary>Monster i goes after unit t until one of them dies (or the leash pulls it home).</summary>
    private void Attack(int i, int t)
    {
        var u = U;
        u.ClearCombat(i);
        u.Combat[i] = CombatOrder.Attack;
        u.Target[i] = u.Id[t];
        u.Order[i] = UnitOrder.Idle;
        u.Blocked[i] = 0;
        u.Field[i] = 0;
    }

    /// <summary>Squared distance from unit i to where the lair's pack is at home now (the den, unless it roams).</summary>
    private Fix64 Distance2(int i, Lair lair)
    {
        Fix64 dx = U.X[i] - (Fix64.FromInt(lair.HomeX) + Fix64.Half), dz = U.Z[i] - (Fix64.FromInt(lair.HomeZ) + Fix64.Half);
        return dx * dx + dz * dz;
    }

    /// <summary>Squared distance from unit i to the lair's den itself.</summary>
    private Fix64 DenDistance2(int i, Lair lair)
    {
        Fix64 dx = U.X[i] - (Fix64.FromInt(lair.X) + Fix64.Half), dz = U.Z[i] - (Fix64.FromInt(lair.Z) + Fix64.Half);
        return dx * dx + dz * dz;
    }

    // --- Beating monsters --------------------------------------------------------------------------

    /// <summary>Unit i, a wild monster, was killed or beaten by a unit of the player's (in this tick's fighting).</summary>
    internal void Beaten(int i, int player)
    {
        int id = U.Id[i];
        // A monster with a bounty (the dark power's corrupted beasts) pays it into the player's storage nearest it.
        var bounty = Def(i).Bounty;
        if (bounty.Length > 0 && player >= 0 && player < Simulation.WildPlayer
            && Economy.NearestStorage(player, U.X[i].FloorToInt(), U.Z[i].FloorToInt()) is { } store)
            foreach (var g in bounty) store.Stock[(int)g.Good] += g.Amount;
        foreach (var lair in _lairs)
            if (!lair.Cleared && lair.PackIds.Contains(id)) lair.LastBeatenBy = player;
    }

    /// <summary>True if the blow that kills unit <paramref name="dead"/> beats it instead: it is a wild monster and the killer's faction tames monsters.</summary>
    internal bool Tames(int dead, int killer) =>
        IsWild(dead) && U.Town[dead] == 0 && !Def(dead).IsGame && !IsWild(killer) && Economy.Faction(U.Owner[killer]).TamesMonsters;

    /// <summary>Unit i, a wild monster, is beaten: it is the player's now, with 1 hit point, and waits for their choice.</summary>
    internal void Subdue(int i, int player)
    {
        var u = U;
        u.Owner[i] = player;
        u.Subdued[i] = true;
        u.Hp[i] = 1;
        u.StopBleeding(i); // a beaten monster is bound up, or it would bleed out before its owner chooses
        u.ClearJob(i);
        u.ClearCombat(i);
        u.ClearFormation(i);
        u.Order[i] = UnitOrder.Idle;
        u.OrderX[i] = u.OrderZ[i] = 0;
        u.Group[i] = 0;
        u.Blocked[i] = 0;
        u.Field[i] = 0;
        u.Cooldown[i] = u.Rout[i] = u.HitBy[i] = 0;
        u.Morale[i] = CombatSystem.MaxMorale;
        u.Stance[i] = UnitStance.Aggressive;
        u.Auto[i] = u.Held[i] = false;
    }

    /// <summary>
    /// The unit that would climb on beaten monster i if the player chose to ride it: the nearest of its owner's
    /// units of the monster's rider type within <see cref="RideRange"/> (ties to the lowest id) that is neither
    /// beaten nor running away; -1 if there is none or nobody rides that monster.
    /// </summary>
    public int RiderFor(int i)
    {
        var u = U;
        if (i < 0 || !u.Subdued[i] || Def(i).Ride is not { } ride) return -1;
        Fix64 r2 = Fix64.FromInt(RideRange * RideRange);
        int best = -1;
        Fix64 bestD = Fix64.MaxValue;
        for (int j = 0; j < u.Count; j++)
        {
            if (u.Owner[j] != u.Owner[i] || u.Def[j] != ride.Rider.Index || u.Subdued[j] || u.Rout[j] > 0) continue;
            if (Math.Abs(u.Y[j] - u.Y[i]) > GuardHeight) continue;
            Fix64 dx = u.X[j] - u.X[i], dz = u.Z[j] - u.Z[i];
            Fix64 d = dx * dx + dz * dz;
            if (d > r2) continue;
            if (d < bestD || (d == bestD && u.Id[j] < u.Id[best]))
            {
                best = j;
                bestD = d;
            }
        }
        return best;
    }

    /// <summary>The player's beaten monsters among the command's units are recruited or ridden, in the command's order.</summary>
    internal void Apply(TameCommand c)
    {
        if (!Enum.IsDefined(c.Choice)) return;
        var u = U;
        var seen = new HashSet<int>();
        foreach (int id in c.Units)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Owner[i] != c.PlayerId || !u.Subdued[i] || !seen.Add(id)) continue;
            if (c.Choice == TameChoice.Recruit) Recruit(i);
            else Ride(i);
        }
    }

    /// <summary>Beaten monster i joins its owner's army, back at full health.</summary>
    private void Recruit(int i)
    {
        var u = U;
        u.Subdued[i] = false;
        u.Hp[i] = Economy.MaxHp(u.Owner[i], Def(i));
    }

    /// <summary>The nearest unit that can climbs on beaten monster i and becomes its rider type; the monster is gone.</summary>
    private void Ride(int i)
    {
        int r = RiderFor(i);
        if (r < 0) return;
        var u = U;
        var ride = Def(i).Ride!;
        int owner = u.Owner[r];
        long oldMax = Math.Max(1, Economy.MaxHp(owner, ride.Rider)), newMax = Math.Max(1, Economy.MaxHp(owner, ride.Becomes));
        u.Def[r] = ride.Becomes.Index;
        u.Hp[r] = (int)Math.Clamp(u.Hp[r] * newMax / oldMax, 1, newMax);
        if (ride.Rider.Role == UnitRole.Worker && ride.Becomes.Role != UnitRole.Worker)
        {
            // A worker who rides off to fight drops its work, as one that evolves into a soldier does.
            if (u.Job[r] != UnitJob.None)
            {
                u.ClearJob(r);
                u.Order[r] = UnitOrder.Idle;
                u.Blocked[r] = 0;
                u.Field[r] = 0;
            }
            u.CarryAmount[r] = 0;
            u.Auto[r] = u.Held[r] = false;
        }
        // A relic the mount bore falls where it stood (its rider, if it has none, picks it up there).
        _sim.Items.DropFrom(i);
        u.RemoveAt(i);
    }

    // --- Dens --------------------------------------------------------------------------------------

    /// <summary>What the player's faction makes of the lair's den once it is cleared, its prize there, or null if nothing.</summary>
    public Content.DenUse? DenUse(int player, Lair lair) =>
        player >= 0 && player != Simulation.WildPlayer ? lair.Monster.Lair!.PrizeFor(Economy.Faction(player)) : null;

    /// <summary>True if the player's faction has a prize at any monster's den.</summary>
    public bool HasPrizes(int player)
    {
        if (player < 0 || player == Simulation.WildPlayer) return false;
        var faction = Economy.Faction(player);
        foreach (var m in _sim.Content.Units)
            if (m.Lair?.PrizeFor(faction) != null) return true;
        return false;
    }

    /// <summary>
    /// The unit among <paramref name="units"/> (indexes) nearest the den within <see cref="DenRange"/> (ties to
    /// the lowest id) that can use it: any unit for a building, one that can take the form for a form, a
    /// fighting unit without a relic for a relic. -1 if none.
    /// </summary>
    public int DenUser(Lair lair, IEnumerable<int> units, Content.DenUse use)
    {
        var u = U;
        Fix64 r2 = Fix64.FromInt(DenRange * DenRange);
        int best = -1;
        Fix64 bestD = Fix64.MaxValue;
        foreach (int j in units)
        {
            if (j < 0 || u.Subdued[j] || u.Rout[j] > 0 || IsWild(j) || u.Town[j] != 0) continue;
            if (use.Form is { } form && form.EvolvesFrom != Def(j)) continue;
            if (use.Relic != null && (u.Relic[j] != 0 || Def(j).Damage <= 0 || Def(j).Role == UnitRole.Worker)) continue;
            if (Math.Abs(u.Y[j] - lair.Y) > GuardHeight) continue;
            Fix64 d = DenDistance2(j, lair);
            if (d > r2) continue;
            if (d < bestD || (d == bestD && u.Id[j] < u.Id[best]))
            {
                best = j;
                bestD = d;
            }
        }
        return best;
    }

    /// <summary>True if the player can use the den now: it is cleared and unused, their faction has a use for it, and a unit of theirs among the units can.</summary>
    public bool CanUseDen(int player, Lair lair, IEnumerable<int> units) =>
        lair.Cleared && !lair.Used && DenUse(player, lair) is { } use && DenUser(lair, Owned(player, units), use) >= 0;

    private IEnumerable<int> Owned(int player, IEnumerable<int> units)
    {
        foreach (int j in units)
            if (j >= 0 && U.Owner[j] == player) yield return j;
    }

    /// <summary>The player uses the den, if they can (see the class notes); it is gone from the map.</summary>
    internal void Apply(DenCommand c)
    {
        if (GetLair(c.Lair) is not { } lair || !lair.Cleared || lair.Used || DenUse(c.PlayerId, lair) is not { } use) return;
        var units = _sim.OwnedUnits(c);
        int user = DenUser(lair, units, use);
        if (user < 0) return;
        if (use.Building is { } def)
        {
            if (Economy.BuildOnDen(c, def, lair.X, lair.Z) is not { } b) return;
            lair.Building = b.Id;
        }
        else if (use.Form is { } form)
        {
            if (form.Faction != null && form.Faction != Economy.Faction(c.PlayerId)) return;
            if (Economy.MissingTech(c.PlayerId, form.Requires) != null) return;
            _sim.Evolution.Evolve(user, form);
        }
        else if (use.Relic is { } relic)
        {
            U.Relic[user] = (short)(relic.Index + 1);
            U.Hp[user] += relic.Hp;
        }
        lair.Used = true;
        lair.UsedBy = c.PlayerId;
    }
}
