using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;

namespace Rts.Sim.AI;

// How Hard plays each faction (see AiStyle in the game data). A faction the computer plays balanced (the
// Settlers) gets a wall round the middle of its town, and a quarter of its soldiers stay behind it when the
// rest attack; a faction it plays in waves (the Orcs) builds no wall and sends nearly everyone. This file is
// the wall and the guard.
public sealed partial class AiPlayer
{
    /// <summary>
    /// Hard, balanced: its wall runs round the middle of its town: the town hall, the yard in front of its
    /// door where its soldiers gather (see <see cref="GatheringGround"/>), and room for its first farms and
    /// houses, which stand as near the town hall as they would without a wall (a farmer carries every load
    /// to storage, so a wall between the fields and the town hall would cost it a good part of its food).
    /// The line is this many columns out from the town hall on three sides and <see cref="KeepYard"/> out on
    /// the door side (+z). There is a gate <see cref="KeepGate"/> columns wide in the middle of the north
    /// and south sides, and two in the west and the east sides, one level with the town hall and one with
    /// the yard. It builds nothing within <see cref="KeepLane"/> columns of a gate, in or out, in line with it.
    /// </summary>
    public const int KeepGap = 16, KeepYard = 20, KeepGate = 3, KeepLane = 4;
    /// <summary>Hard, balanced: it starts on its wall once it has a barracks and this many workers.</summary>
    public const int KeepWorkers = 16;
    /// <summary>
    /// Hard, balanced: while its wall is the only thing it is short of stone for, it keeps at most this many
    /// workers at its quarry (it keeps that many there early on anyway, see <see cref="MostMasons"/>): the
    /// wall goes up as the stone comes in, and is not worth taking more workers off the fields and the trees.
    /// </summary>
    public const int KeepMasons = 3;
    /// <summary>Hard, balanced: at most this many columns of its wall wait to be built at a time (see <see cref="HardWall"/>).</summary>
    public const int KeepOpenSites = 6;
    /// <summary>Hard, balanced: ticks between looks at its wall (what is not laid yet, and what has been knocked out).</summary>
    public const int KeepLookTicks = 10 * Tps;
    /// <summary>
    /// Hard, balanced: a wave leaves this share (percent) of the soldiers at home behind, to hold the wall
    /// (a faction it plays in waves leaves <see cref="HardGuardPercent"/>). The part it sends is what has to
    /// be stronger than the enemy (see <see cref="HardEdgePercent"/>), so it attacks with a bigger army in
    /// all than a faction played in waves would. Played over 20 seeds against Hard's Orcs, a quarter cost it
    /// nothing against leaving 15 percent; 35 percent made its attacks late and weak, and lost it matches.
    /// </summary>
    public const int KeepGuardPercent = 25;

    /// <summary>
    /// Hard, balanced: a building of its own more than this many columns outside the line of its wall is an
    /// outpost. Its soldiers do not go out after enemies there: they would leave through the gates a few
    /// at a time for a fight far from home, and be beaten in parts. They meet the enemy at the town.
    /// A town hall is never an outpost.
    /// </summary>
    public const int KeepReach = 24;

    // The line of its wall (the corners), the town hall it is round, whether it has looked for room for one,
    // when it next looks at the wall, and what the next stretch costs (kept in hand until it can pay).
    private (int X0, int Z0, int X1, int Z1)? _ring;
    // The middle column of each gate, and whether its side runs along x (north and south) or z.
    private readonly List<(int X, int Z, bool AlongX)> _gates = new();
    private int _ringHall;
    private bool _ringPlanned;
    private long _ringAt;
    private GoodAmount[]? _ringCost;
    // The stone kept in hand for the wall this look (0 when it wants none).
    private int _ringStone;
    // The columns of the side it is laying that are still to lay, in order along the side.
    private readonly List<(int X, int Z, bool Gate)> _laying = new();

    /// <summary>How the computer plays its faction (see <see cref="AiStyle"/>).</summary>
    private AiStyle Style(Simulation sim) => sim.Economy.Faction(Player).AiStyle;

    /// <summary>Hard: the line its wall runs along (the corners of the rectangle), if it means to have one.</summary>
    public (int X0, int Z0, int X1, int Z1)? TownWall => _ring;

    /// <summary>Hard: the middle column of each gate in its wall.</summary>
    public IReadOnlyList<(int X, int Z, bool AlongX)> TownGates => _gates;

    /// <summary>Hard: the share (percent) of its soldiers at home that a wave leaves behind, by how it plays its faction.</summary>
    private int HomeGuardPercent(Simulation sim) => Style(sim) == AiStyle.Balanced ? KeepGuardPercent : HardGuardPercent;

    /// <summary>True if column (x, z) is more than <see cref="KeepReach"/> columns outside the line of its wall (never, without one).</summary>
    private bool Outpost(int x, int z) =>
        _ring is var (x0, z0, x1, z1) && (x < x0 - KeepReach || x > x1 + KeepReach || z < z0 - KeepReach || z > z1 + KeepReach);

    /// <summary>
    /// Picks the line of its wall, once, before it has built anything: nothing goes up on the line from
    /// then on (see <see cref="ClearOfWall"/>), so the wall can be laid whole when it has the stone. Only
    /// for a faction it plays balanced, and only where the line fits on the map.
    /// </summary>
    private void PlanWall(Simulation sim, Building hall)
    {
        if (_ringPlanned) return;
        _ringPlanned = true;
        if (Style(sim) != AiStyle.Balanced) return;
        int x0 = hall.X - KeepGap, z0 = hall.Z - KeepGap, x1 = hall.MaxX + KeepGap, z1 = hall.MaxZ + KeepYard;
        var w = sim.World;
        if (x0 < 2 || z0 < 2 || x1 > w.SizeX - 3 || z1 > w.SizeZ - 3) return;
        _ring = (x0, z0, x1, z1);
        _ringHall = hall.Id;
        var (hx, hz) = Middle(hall);
        int yard = (hall.MaxZ + 1 + z1) / 2;
        _gates.Add((hx, z0, true));
        _gates.Add((hx, z1, true));
        foreach (int x in new[] { x0, x1 })
        {
            _gates.Add((x, hz, false));
            _gates.Add((x, yard, false));
        }
        Note(sim, $"marks out a wall round its town hall, {x0},{z0} to {x1},{z1}");
    }

    /// <summary>
    /// The four sides of its wall, each as its columns in order along the side and whether each is a gate
    /// column (the corners belong to the north and south sides), the side nearest (ex, ez) first.
    /// </summary>
    private List<(string Name, List<(int X, int Z, bool Gate)> Columns)> WallSides(int x0, int z0, int x1, int z1, int ex, int ez)
    {
        int h = KeepGate / 2;
        var sides = new List<(long D, int K, string Name, List<(int X, int Z, bool Gate)> Columns)>();
        // A side along x at z = at (from x = lo to hi), or along z at x = at.
        void Add(string name, bool alongX, int at, int lo, int hi)
        {
            var columns = new List<(int X, int Z, bool Gate)>();
            for (int k = lo; k <= hi; k++)
            {
                bool gate = false;
                foreach (var g in _gates)
                    gate |= g.AlongX == alongX && (alongX ? g.Z : g.X) == at && Math.Abs((alongX ? g.X : g.Z) - k) <= h;
                columns.Add(alongX ? (k, at, gate) : (at, k, gate));
            }
            int mid = (lo + hi) / 2;
            sides.Add((alongX ? Dist2(mid, at, ex, ez) : Dist2(at, mid, ex, ez), sides.Count, name, columns));
        }
        Add("north", true, z0, x0, x1);
        Add("south", true, z1, x0, x1);
        Add("west", false, x0, z0 + 1, z1 - 1);
        Add("east", false, x1, z0 + 1, z1 - 1);
        sides.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.K.CompareTo(b.K));
        var list = new List<(string, List<(int X, int Z, bool Gate)>)>();
        foreach (var s in sides) list.Add((s.Name, s.Columns));
        return list;
    }

    /// <summary>
    /// Hard, balanced: lays its wall and keeps it whole. Every <see cref="KeepLookTicks"/> it goes round the
    /// line, the side toward the enemy first, and takes on the first side that has columns missing (never
    /// laid, knocked out since, or where a tree stood) as soon as it can pay for all of them; until then it
    /// keeps the price in hand. A side is laid a few columns at a time, from one end to the other, never
    /// more than <see cref="KeepOpenSites"/> waiting to be built: every site calls the nearest worker with
    /// nothing to do, so a whole side at once would call in half its workers from the trees, most of them
    /// for nothing. Its automatic workers build the columns as they do any site, and move on along the wall.
    /// </summary>
    private void HardWall(Simulation sim, Building hall, Census c, Action<Command> issue)
    {
        _ringStone = 0;
        if (_ring is not var (x0, z0, x1, z1) || hall.Id != _ringHall) return;
        if (c.Barracks < 1 || c.Workers < KeepWorkers) return;
        var wall = FirstBuilding(sim, BuildingKind.Wall);
        var gate = FirstBuilding(sim, BuildingKind.Gate);
        if (wall == null || gate == null) return;
        if (_laying.Count > 0)
        {
            LayNext(sim, wall, gate, issue);
            return;
        }
        if (sim.Tick < _ringAt)
        {
            if (_ringCost != null) SaveForWall(_ringCost);
            return;
        }
        _ringAt = sim.Tick + KeepLookTicks;
        _ringCost = null;
        var (hx, hz) = Middle(hall);
        var guesses = HallGuesses(sim, hx, hz);
        var enemy = KnownHall(hx, hz) is { } k ? (k.X, k.Z) : guesses.Count > 0 ? guesses[0] : (sim.World.SizeX / 2, sim.World.SizeZ / 2);
        foreach (var (name, columns) in WallSides(x0, z0, x1, z1, enemy.Item1, enemy.Item2))
        {
            int walls = 0, gates = 0;
            foreach (var (x, z, isGate) in columns)
            {
                if (!Missing(sim, isGate ? gate : wall, x, z)) continue;
                _laying.Add((x, z, isGate));
                if (isGate) gates++; else walls++;
            }
            if (_laying.Count == 0) continue;
            var cost = Sum(wall.Cost, walls, gate.Cost, gates);
            if (!Affordable(sim, cost))
            {
                _laying.Clear();
                _ringCost = cost;
                SaveForWall(cost);
                return;
            }
            Note(sim, $"lays {walls + gates} columns of the {name} side of its wall");
            LayNext(sim, wall, gate, issue);
            return;
        }
    }

    /// <summary>True if the column of its wall has nothing on it and could take a wall or gate now (goods aside).</summary>
    private bool Missing(Simulation sim, BuildingDef def, int x, int z) =>
        sim.Buildings.AtColumn(x, z) == null && sim.Economy.CanPlace(Player, def, x, z, out _) is PlaceResult.Ok or PlaceResult.CannotAfford;

    /// <summary>
    /// Lays the next columns of the side it is laying, as many as leave no more than <see cref="KeepOpenSites"/>
    /// of its wall waiting to be built, and keeps the price of the rest in hand.
    /// </summary>
    private void LayNext(Simulation sim, BuildingDef wall, BuildingDef gate, Action<Command> issue)
    {
        int open = 0;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.IsWall && b.State == BuildingState.Site) open++;
        var none = Array.Empty<int>();
        while (_laying.Count > 0 && open < KeepOpenSites)
        {
            var (x, z, isGate) = _laying[0];
            var def = isGate ? gate : wall;
            if (!Missing(sim, def, x, z))
            {
                _laying.RemoveAt(0); // built on, or something is in the way now: the next look at the wall sees to it
                continue;
            }
            if (!Affordable(sim, def.Cost)) break; // something it wants more took the goods: it waits
            _laying.RemoveAt(0);
            issue(new WallCommand(Player, none, def.Id, x, z, x, z));
            Spend(def.Cost);
            open++;
        }
        int walls = 0, gates = 0;
        foreach (var (_, _, isGate) in _laying)
            if (isGate) gates++; else walls++;
        if (walls + gates > 0) SaveForWall(Sum(wall.Cost, walls, gate.Cost, gates));
    }

    /// <summary>Keeps the price of the next stretch of wall in hand (see <see cref="Save"/>), and notes how much of it is stone.</summary>
    private void SaveForWall(GoodAmount[] cost)
    {
        Save(cost);
        _ringStone = Price(cost, Good.Stone);
    }

    /// <summary>The price of <paramref name="n"/> of one thing and <paramref name="m"/> of another, good by good.</summary>
    private static GoodAmount[] Sum(GoodAmount[] a, int n, GoodAmount[] b, int m)
    {
        var total = new int[Goods.Count];
        foreach (var g in a) total[(int)g.Good] += g.Amount * n;
        foreach (var g in b) total[(int)g.Good] += g.Amount * m;
        var list = new List<GoodAmount>();
        foreach (var good in Goods.All)
            if (total[(int)good] > 0) list.Add(new GoodAmount(good, total[(int)good]));
        return list.ToArray();
    }

    /// <summary>
    /// True if a building with its corner at (x, z) keeps clear of its wall: a column off the line (a wall
    /// column cannot stand right beside another building, which would leave a hole), and out of the way
    /// through each gate (see <see cref="KeepLane"/>). Always true without a wall.
    /// </summary>
    private bool ClearOfWall(BuildingDef def, int x, int z) => ClearOfWall(x, z, x + def.SizeX - 1, z + def.SizeZ - 1, 1);

    /// <summary>True if the rectangle keeps <paramref name="margin"/> columns off the line of its wall, and out of the way through each gate.</summary>
    private bool ClearOfWall(int ax, int az, int bx, int bz, int margin)
    {
        if (_ring is not var (x0, z0, x1, z1)) return true;
        bool touches = ax - margin <= x1 && bx + margin >= x0 && az - margin <= z1 && bz + margin >= z0;
        bool inside = ax - margin > x0 && bx + margin < x1 && az - margin > z0 && bz + margin < z1;
        if (touches && !inside) return false;
        // The ways through the gates, across the wall.
        int h = KeepGate / 2;
        foreach (var (gx, gz, alongX) in _gates)
        {
            int wx = alongX ? h : KeepLane, wz = alongX ? KeepLane : h;
            if (ax <= gx + wx && bx >= gx - wx && az <= gz + wz && bz >= gz - wz) return false;
        }
        return true;
    }
}
