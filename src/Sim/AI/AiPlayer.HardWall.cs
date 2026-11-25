using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// How Hard plays each faction (see AiStyle in the game data). A faction the computer plays balanced (the
// Settlers) gets a wall round the middle of its town, and a quarter of its soldiers stay behind it when the
// rest attack; a faction it plays in waves (the Orcs) builds no wall and sends nearly everyone. This file is
// the wall (laid as barriers, then made wooden and stone out of what it can spare), the archers on it, and
// the guard.
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
    /// Hard, balanced: while its wall is the only thing it is short of stone for (the stone to make it a
    /// stone wall, mostly), it keeps at most this many workers at its quarry (it keeps that many there early
    /// on anyway, see <see cref="MostMasons"/>): the wall gets its stone as it comes in, and is not worth
    /// taking more workers off the fields and the trees.
    /// </summary>
    public const int KeepMasons = 3;
    /// <summary>
    /// Hard, balanced: at most this many columns of its wall wait to be built or are being upgraded at a time
    /// (see <see cref="HardWall"/>).
    /// </summary>
    public const int KeepOpenSites = 6;
    /// <summary>
    /// Hard, balanced: it upgrades its wall only out of what it can spare: a column at a time while it has
    /// this much wood, and this much stone, left over on top of what it keeps in hand for anything else.
    /// Soldiers and houses come first; the wall gets stronger as the goods pile up.
    /// </summary>
    public const int KeepSpareWood = 100, KeepSpareStone = StoneWanted;
    /// <summary>
    /// Hard, balanced: an archer on its wall this near (columns) to the enemies at its town stays up there
    /// and shoots; the others at home go to meet them as ever.
    /// </summary>
    public const int KeepPostHold = 20;
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
    // The stone its wall wants this look (0 when it wants none), how much of that it keeps in hand (the
    // price of what it is laying; an upgrade is never saved for), and what the tier it is upgrading to
    // still wanted of stone the last time it looked.
    private int _ringStone, _ringKept, _upgradeStone;
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
    /// The wall is laid as barriers, the first tier, which cost a little wood; once no column is missing it
    /// is made stronger (see <see cref="StrengthenWall"/>).
    /// </summary>
    private void HardWall(Simulation sim, Building hall, Census c, Action<Command> issue)
    {
        _ringStone = _ringKept = 0;
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
            else _ringStone = _upgradeStone;
            return;
        }
        _ringAt = sim.Tick + KeepLookTicks;
        _ringCost = null;
        _upgradeStone = 0;
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
        Towers(sim, enemy, issue);
        StrengthenWall(sim, WallSides(x0, z0, x1, z1, enemy.Item1, enemy.Item2), issue);
    }

    /// <summary>
    /// Hard, balanced: with its wall laid all round, it upgrades it a tier at a time, the whole line to
    /// wooden walls (which its archers stand on, see <see cref="PostGuard"/>) before any of it to stone, the
    /// side toward the enemy first and a few columns at a time (see <see cref="KeepOpenSites"/>), each paid
    /// for out of what it can spare (see <see cref="KeepSpareWood"/>). It never saves up for this: while
    /// soldiers, houses or research want the goods, the wall waits. A tier it has not the tech for waits too.
    /// </summary>
    private void StrengthenWall(Simulation sim, List<(string Name, List<(int X, int Z, bool Gate)> Columns)> sides, Action<Command> issue)
    {
        int open = OpenWallSites(sim);
        var none = Array.Empty<int>();
        var stock = sim.Economy.Stockpile(Player);
        int tiers = 0;
        foreach (var d in sim.Content.Buildings)
            if (d.IsWall) tiers = Math.Max(tiers, d.Tier);
        for (int tier = 0; tier < tiers; tier++)
        {
            bool left = false;
            int started = 0, stone = 0, spent = 0;
            BuildingDef? becomes = null;
            foreach (var (_, columns) in sides)
            foreach (var (x, z, _) in columns)
            {
                var b = sim.Buildings.AtColumn(x, z);
                if (b == null || b.Owner != Player || !b.Def.IsWall || b.Def.Tier != tier) continue;
                if (b.Def.Upgrade is not { } to) continue;
                if (TowerOn(sim, x, z) is { Active: false }) continue; // a tower is going up over it: it is the tower's now
                if (b.State != BuildingState.Active || b.Upgrade != null)
                {
                    left = true; // still going up, or being upgraded now
                    continue;
                }
                var result = sim.Economy.CanUpgrade(b);
                if (result is not (PlaceResult.Ok or PlaceResult.CannotAfford)) continue; // no tech yet, or no room above
                left = true;
                stone += Price(to.Cost, Good.Stone);
                // A faced wall is three sites' work: itself, and the facing that goes up on each side of it after.
                int sites = to.Facing != null ? 3 : 1;
                if (open + sites > KeepOpenSites || result != PlaceResult.Ok || !SpareForWall(stock, to.Cost)) continue;
                issue(new UpgradeCommand(Player, none, b.Id, false));
                Spend(to.Cost);
                stone -= Price(to.Cost, Good.Stone);
                spent += Price(to.Cost, Good.Stone);
                becomes ??= to;
                open += sites;
                started++;
            }
            if (started > 0 && sim.Tick >= _strengthenNote)
            {
                _strengthenNote = sim.Tick + 60 * Tps;
                Note(sim, $"upgrades {started} columns of its wall to {becomes!.Name.ToLowerInvariant()}");
            }
            if (left)
            {
                // What the tier still wants of stone: a few workers stay at the quarry for it (see KeepMasons).
                _ringStone = _upgradeStone = stone;
                _ringKept = spent;
                return;
            }
        }
    }

    /// <summary>
    /// True if it can pay the cost of a column's upgrade out of what it can spare: every good of it, on top of
    /// what it keeps in hand, with <see cref="KeepSpareWood"/> wood left over and, if it takes stone,
    /// <see cref="KeepSpareStone"/> stone.
    /// </summary>
    private bool SpareForWall(int[] stock, GoodAmount[] cost)
    {
        foreach (var g in cost)
            if (stock[(int)g.Good] < g.Amount + _keep[(int)g.Good]) return false;
        int wood = Price(cost, Good.Wood), stone = Price(cost, Good.Stone);
        return stock[(int)Good.Wood] >= wood + _keep[(int)Good.Wood] + KeepSpareWood
            && (stone == 0 || stock[(int)Good.Stone] >= stone + _keep[(int)Good.Stone] + KeepSpareStone);
    }

    // When it may next write in its journal that it is upgrading its wall (once a minute, not every look).
    private long _strengthenNote;

    /// <summary>
    /// The columns of its wall waiting to be built or being upgraded; one being upgraded to a faced wall counts
    /// for three, with the facing that goes up beside it when that is done.
    /// </summary>
    private int OpenWallSites(Simulation sim)
    {
        int open = 0;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.IsWall && b.WantsBuilders && !b.Planned) open += b.Upgrade?.Facing != null ? 3 : 1;
        return open;
    }

    /// <summary>True if the unit stands on a wall or gate, or is on its way to a post on one.</summary>
    private static bool Posted(Simulation sim, int i) => sim.Units.Job[i] == UnitJob.Post || sim.Economy.WallUnder(i) != null;

    /// <summary>
    /// Hard, balanced: its archers and other ranged soldiers at home stand on its wall, by the gate nearest
    /// the enemy, once the wall there can be stood on: they see and shoot further from up there, and blows
    /// from the ground do not reach them. Those posted (or on their way up) are taken out of
    /// <paramref name="home"/>, so they are not called back to where the others gather. They come down when
    /// a wave they are part of sets out, or to meet enemies somewhere else.
    /// </summary>
    private void PostGuard(Simulation sim, List<int> home, (int X, int Z) enemy, Action<Command> issue)
    {
        if (_ring == null || _gates.Count == 0) return;
        var u = sim.Units;
        var econ = sim.Economy;
        var gate = _gates[0];
        foreach (var g in _gates)
            if (Dist2(g.X, g.Z, enemy.X, enemy.Z) < Dist2(gate.X, gate.Z, enemy.X, enemy.Z)) gate = g;
        var holding = new List<int>();
        HoldGates(sim, home, holding, issue);
        home.RemoveAll(holding.Contains);
        var going = new List<int>();
        // A finished tower of its own by that gate is named first: its posts are the nearest, and those on it aim
        // better (see Towers). Else the gate, once it can be stood on.
        var at = sim.Buildings.AtColumn(gate.X, gate.Z);
        var towers = GateTowers(sim, gate.X, gate.Z);
        UpTheTowers(sim, towers, issue);
        foreach (var t in towers)
            if (econ.PostFree(t)) { at = t; break; }
        if (at != null && at.Owner == Player && at.Active && at.Def.Walkway)
        {
            // As many as there are free columns to stand on along that side, within reach of the gate: the
            // line's own, or where it is faced with stone (or a tower stands) the columns on either side of it.
            int free = 0;
            for (int k = -EconomySystem.PostReach; k <= EconomySystem.PostReach; k++)
            for (int d = -1; d <= 1; d++)
            {
                int x = gate.AlongX ? gate.X + k : gate.X + d, z = gate.AlongX ? gate.Z + d : gate.Z + k;
                var b = sim.Buildings.AtColumn(x, z);
                if (b != null && b.Owner == Player && econ.PostFree(b, x, z)) free++;
            }
            foreach (int id in home)
            {
                if (going.Count >= free) break;
                int i = u.IndexOf(id);
                if (i >= 0 && !Posted(sim, i) && econ.CanPost(i) && u.Rout[i] == 0 && u.Target[i] == 0) going.Add(id);
            }
            if (going.Count > 0) issue(new PostCommand(Player, going.ToArray(), at.Id));
        }
        home.RemoveAll(id => going.Contains(id) || (u.IndexOf(id) is >= 0 and var i && Posted(sim, i)));
    }

    /// <summary>
    /// Hard: each of its gates that has murder holes (see <see cref="CombatSystem.Held"/>) gets a ranged soldier
    /// from home posted by it, the nearest one free, unless one of its units already stands on the wall there or
    /// is on its way up: an unheld gate drops no stones. Those sent are added to <paramref name="going"/>.
    /// </summary>
    private void HoldGates(Simulation sim, List<int> home, List<int> going, Action<Command> issue)
    {
        var u = sim.Units;
        var econ = sim.Economy;
        if (!econ.HasMurderHoles(Player)) return;
        foreach (var g in _gates)
        {
            var at = sim.Buildings.AtColumn(g.X, g.Z);
            if (at == null || at.Owner != Player || !at.Active || !at.Def.Walkway || at.Def.MurderHoles is not { } holes) continue;
            bool held = false;
            for (int i = 0; i < u.Count && !held; i++)
            {
                if (u.Owner[i] != Player) continue;
                if (u.Job[i] == UnitJob.Post) held = at.ColumnDistance(u.JobX[i], u.JobZ[i]) <= holes.Held;
                else if (econ.WallUnder(i) != null) held = at.ColumnDistance(u.X[i].FloorToInt(), u.Z[i].FloorToInt()) <= holes.Held;
            }
            if (held) continue;
            int best = -1;
            long bestD = long.MaxValue;
            foreach (int id in home)
            {
                int i = u.IndexOf(id);
                if (i < 0 || going.Contains(id) || Posted(sim, i) || !econ.CanPost(i) || u.Rout[i] != 0 || u.Target[i] != 0) continue;
                long d = Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), g.X, g.Z);
                if (d < bestD) (best, bestD) = (id, d);
            }
            if (best < 0) return; // nobody left at home to send
            going.Add(best);
            issue(new PostCommand(Player, new[] { best }, at.Id));
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
        int open = OpenWallSites(sim);
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
            _ringStone += Price(def.Cost, Good.Stone);
            _ringKept += Price(def.Cost, Good.Stone);
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
        _ringStone += Price(cost, Good.Stone);
        _ringKept += Price(cost, Good.Stone);
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
