using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Ships at the smart levels (Easy and up).
//
// Docks: with a finished barracks and DockWorkers workers, it lays out a dock on the shore within DockRadius of its
// town hall, where the water by the dock opens onto at least OpenWater columns a ship can sail (not a pond); with no
// sawmill yet it lays that out first, for the planks.
//
// Transport ships and ferrying: once its way by land to the enemy (the town hall it knows, or where it guesses one
// stands) is cut off or more than FerryRatio times the straight way (and that is at least FerryLeast columns), and a
// ship from its dock could put soldiers ashore by the enemy, its docks keep ShipsWanted ships. A wave out whose way by
// land is like that is ferried: its free ships put in at the shore nearest the wave, its soldiers walk there and
// board, and each ship, full (or once nobody else is coming aboard), sails them to the shore nearest their target from
// which they can walk to it and puts them ashore; then it comes back for more. Once all are across, the wave goes on
// from where it landed. It looks again a few times on the way (a wave held up by a river looks for ships).
//
// Bombard ships: while it knows of an enemy wall, gate or tower that a ship from its dock could shell from the water,
// its docks keep BombardShipsWanted bombard ships. They sail out with a wave: while a wave of its own is out after a
// building, they shell the enemy towers (then walls and gates) within RamLook of it that they can reach from the water,
// else the building itself, so the wave on land is their screen. With ranged soldiers at home and a transport ship
// free, up to EscortCrew of them board it and sail with the bombard ships as their escort, shooting from the deck. A
// bombard ship below ShipHurtPercent of its hit points, and all of them once the wave is over, sail home.
//
// What it works out about the water it works out afresh (a chart: see SailFrom) rather than through the boat
// system's own ways over the water, so that its looks change nothing in the game.
public sealed partial class AiPlayer
{
    /// <summary>Workers it has before it lays out a dock.</summary>
    public const int DockWorkers = 14;
    /// <summary>How far (columns) from its town hall it looks for the shore to put a dock on.</summary>
    public const int DockRadius = 48;
    /// <summary>Columns of water a ship from a dock must be able to sail for the dock to be worth it.</summary>
    public const int OpenWater = 300;
    /// <summary>Ticks between its looks at the sea: room for a dock, and whether it needs ships.</summary>
    public const int SeaLookTicks = 30 * Tps;
    /// <summary>Transport ships its docks keep while the way to the enemy is by sea; bombard ships while it knows a wall by the shore.</summary>
    public const int ShipsWanted = 2, BombardShipsWanted = 2;
    /// <summary>
    /// A way by land more than this many times the straight way is long: it goes by sea instead. Only when the straight
    /// way is at least <see cref="FerryLeast"/> columns.
    /// </summary>
    public const int FerryRatio = 2, FerryLeast = 40;
    /// <summary>How far (columns) round the target it looks for a shore to land on, and round the wave for one to board from.</summary>
    public const int FerryLook = 40, PickLook = 40;
    /// <summary>Looks a wave takes at going by sea, at most, and the ticks between them.</summary>
    public const int FerryChecks = 3, FerryRecheckTicks = 30 * Tps;
    /// <summary>A ferry gives up after this long; a ship waits this long for soldiers coming aboard before it sails with those it has.</summary>
    public const int FerryTicks = 5 * 60 * Tps, BoardWaitTicks = 25 * Tps;
    /// <summary>Ranged soldiers aboard a bombard ships' escort, at most.</summary>
    public const int EscortCrew = 6;
    /// <summary>A bombard ship below this share (percent) of its hit points sails home.</summary>
    public const int ShipHurtPercent = 35;

    private long _seaLook, _dockLook;
    private bool _seaWanted, _shoreTargets;
    /// <summary>A crossing by sea under way: where its soldiers board, where they are put ashore, its ships, and who has been aboard.</summary>
    private sealed class Crossing
    {
        public int PX, PZ, LX, LZ;
        public long Start;
        public readonly List<int> Ships = new(), Rode = new();
        // For a lift: the soldiers joining the wave that it takes across (the wave's ferry takes the wave).
        public readonly List<int> Members = new();
    }

    // The wave's ferry under way, and its looks at going by sea.
    private Crossing? _ferry;
    private long _ferryNext;
    private int _ferryChecks;
    // Hard: the lift under way taking soldiers joining the wave across to it, and when it next looks for one.
    private Crossing? _lift;
    private long _liftNext;
    // When each ship began waiting for soldiers to board it (ship id to tick). Looked up only.
    private readonly Dictionary<int, long> _boardSince = new();
    // The bombard ships' escort: the transport ship and the ranged soldiers sent aboard it.
    private int _escort;
    private bool _escortIn;
    private readonly List<int> _escortCrew = new();
    private int _fleetGoal;
    private long _fleetLook;
    /// <summary>Ticks between its bombard ships' looks for something to shell, while they have found nothing.</summary>
    public const int FleetLookTicks = 10 * Tps;

    /// <summary>True while a wave of its own is being ferried across the water.</summary>
    public bool Ferrying => _ferry != null;
    /// <summary>Where the ferry under way puts the wave ashore, or null.</summary>
    public (int X, int Z)? FerryLanding => _ferry is { } f ? (f.LX, f.LZ) : null;
    /// <summary>True while soldiers joining a wave out are being taken across the water to it.</summary>
    public bool Lifting => _lift != null;
    /// <summary>Where the lift under way puts the soldiers joining the wave ashore, or null.</summary>
    public (int X, int Z)? LiftLanding => _lift is { } l ? (l.LX, l.LZ) : null;
    /// <summary>True if its last look found the way to the enemy by land cut off or long, and a way by sea.</summary>
    public bool SeaRoute => _seaWanted;
    /// <summary>The transport ship escorting its bombard ships (unit id), or 0.</summary>
    public int Escort => _escort;

    /// <summary>True if the building trains only ships (a dock): it is not counted as a barracks.</summary>
    public static bool Docks(Simulation sim, BuildingDef def)
    {
        if (def.Kind != BuildingKind.Barracks || def.Trains.Length == 0) return false;
        foreach (string id in def.Trains)
            if (!sim.Content.TryGetUnit(id, out var d) || !d.IsBoat) return false;
        return true;
    }

    private static bool Transport(UnitDef def) => def.Role == UnitRole.Boat && def.Seats > 0;
    private static bool Bombarder(UnitDef def) => def.Sails && def.CanAttack;

    private BuildingDef? DockDef(Simulation sim)
    {
        foreach (var d in sim.Content.Buildings)
            if (d.UpgradeOf == null && Docks(sim, d) && sim.Economy.Unlocked(Player, d)) return d;
        return null;
    }

    /// <summary>Its units of the kind, by id in index order.</summary>
    private List<int> Own(Simulation sim, Func<UnitDef, bool> kind)
    {
        var u = sim.Units;
        var list = new List<int>();
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && kind(sim.Content.Units[u.Def[i]])) list.Add(u.Id[i]);
        return list;
    }

    /// <summary>How many of its units are aboard ship i.</summary>
    private int Aboard(Simulation sim, int i)
    {
        var u = sim.Units;
        int id = u.Id[i], n = 0;
        for (int j = 0; j < u.Count; j++)
            if (u.Aboard[j] == id) n++;
        return n;
    }

    // --- Economy -----------------------------------------------------------------------------------

    /// <summary>
    /// The smart levels, every <see cref="SeaLookTicks"/>: a dock (a sawmill first) once there is shore near its town
    /// hall; whether it needs transport ships and bombard ships; and its docks training them.
    /// </summary>
    private void Shipyard(Simulation sim, Building hall, int workers, Action<Command> issue)
    {
        if (!Smart) return;
        var dockDef = DockDef(sim);
        if (dockDef == null) return;
        Building? dock = null;
        bool site = false, sawmill = false, barracks = false;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player) continue;
            if (Docks(sim, b.Def))
            {
                if (b.Active) dock ??= b;
                else site = true;
            }
            else if (b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == Good.Planks) sawmill = true;
            else if (IsBarracks(sim, b.Def) && b.Active) barracks = true;
        }
        if (dock == null)
        {
            if (site || !barracks || workers < DockWorkers || sim.Tick < _dockLook) return;
            _dockLook = sim.Tick + SeaLookTicks;
            LayOutDock(sim, hall, dockDef, sawmill, issue);
            return;
        }
        if (sim.Tick >= _seaLook)
        {
            _seaLook = sim.Tick + SeaLookTicks;
            LookAtSea(sim, hall, dock);
        }
        TrainShips(sim, issue);
    }

    /// <summary>A dock on the shore near the town hall, onto open water; with no sawmill, the sawmill first.</summary>
    private void LayOutDock(Simulation sim, Building hall, BuildingDef dockDef, bool sawmill, Action<Command> issue)
    {
        var at = Middle(hall);
        if (!sim.Automation.FindRoom(Player, dockDef, at.X, at.Z, DockRadius, out int x, out int z, (bx, bz) => ShoreGround(sim, dockDef, bx, bz))) return;
        // Water a ship can get out onto from there: not a pond.
        Chart(sim);
        if (!Launch(sim, x, z, x + dockDef.SizeX - 1, z + dockDef.SizeZ - 1, out int wx, out int wz) || SailFrom(sim, wx, wz) < OpenWater)
        {
            _dockLook = sim.Tick + 10 * SeaLookTicks;
            return;
        }
        BuildingDef next = dockDef;
        if (!sawmill)
        {
            if (Making(sim, Good.Planks) is not { } mill) return;
            next = mill;
        }
        if (!Affordable(sim, next.Cost)) return;
        if (next == dockDef)
        {
            issue(new BuildCommand(Player, Array.Empty<int>(), dockDef.Id, x, z));
            if (Hard) Spend(dockDef.Cost);
        }
        else if (Hard) Want(sim, next, hall, HardTownRadius, false, issue);
        else PlaceNear(sim, next, at, Automation.AutomationSystem.PlanRadius, issue);
        Note(sim, next == dockDef ? $"lays out a dock at {x},{z}" : "lays out a sawmill for the planks of a dock");
    }

    /// <summary>
    /// Whether it needs transport ships (the way to the enemy by land is cut off or long, and a ship from the dock can
    /// land soldiers by them) and bombard ships (it knows of an enemy wall, gate or tower a ship from the dock can shell).
    /// </summary>
    private void LookAtSea(Simulation sim, Building hall, Building dock)
    {
        Chart(sim);
        if (!Launch(sim, dock.X, dock.Z, dock.MaxX, dock.MaxZ, out int wx, out int wz)) return;
        SailFrom(sim, wx, wz);
        var (hx, hz) = Middle(hall);
        var goal = EnemyHall(sim, hx, hz) is { } eh ? (eh.AX, eh.AZ) : SearchGoal(sim, hx, hz, hx, hz);
        if (goal is var (gx, gz))
        {
            var (sx, sz) = Approach(sim, hall);
            int g = sim.Walkable.NearestNode(sx, sz, hall.Y);
            _seaWanted = ByLandFar(sim, g, gx, gz) && Shore(sim, gx, gz, FerryLook, true, out _, out _);
        }
        _shoreTargets = false;
        var bombard = ShipDef(sim, dock.Def, Bombarder);
        if (bombard == null || !sim.Economy.Unlocked(Player, bombard)) return;
        int reach = BoatSystem.ReachOf(bombard);
        foreach (var k in _known.Values)
        {
            if (k.Kind is not (BuildingKind.Wall or BuildingKind.Gate or BuildingKind.Tower) || !Rival(k.Owner)) continue;
            if (sim.Buildings.Get(k.Id) is not { } b || b.Owner != k.Owner || b.Planned || !Shellable(sim, b, reach)) continue;
            _shoreTargets = true;
            return;
        }
    }

    private static UnitDef? ShipDef(Simulation sim, BuildingDef dock, Func<UnitDef, bool> kind)
    {
        foreach (string id in dock.Trains)
            if (sim.Content.TryGetUnit(id, out var d) && kind(d)) return d;
        return null;
    }

    /// <summary>Its docks keep <see cref="ShipsWanted"/> transport ships and <see cref="BombardShipsWanted"/> bombard ships while it needs them, one at a time.</summary>
    private void TrainShips(Simulation sim, Action<Command> issue)
    {
        int transports = 0, bombards = 0;
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player) continue;
            var d = sim.Content.Units[u.Def[i]];
            if (Transport(d)) transports++;
            else if (Bombarder(d)) bombards++;
        }
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !Docks(sim, b.Def)) continue;
            foreach (int q in b.Queue)
            {
                if (Transport(sim.Content.Units[q])) transports++;
                else if (Bombarder(sim.Content.Units[q])) bombards++;
            }
        }
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || !Docks(sim, b.Def) || b.Queue.Count > 0) continue;
            UnitDef? next = null;
            if (_seaWanted && transports < ShipsWanted) next = ShipDef(sim, b.Def, Transport);
            if (next == null && _shoreTargets && bombards < BombardShipsWanted) next = ShipDef(sim, b.Def, Bombarder);
            if (next == null || !sim.Economy.Unlocked(Player, next) || !Affordable(sim, next.Cost)) continue;
            issue(new TrainCommand(Player, b.Id, next.Id));
            if (Hard) Spend(next.Cost);
            if (Transport(next)) transports++;
            else bombards++;
            Note(sim, $"has its dock build a {next.Name.ToLowerInvariant()}");
        }
    }

    // --- Ferrying ----------------------------------------------------------------------------------

    /// <summary>
    /// The smart levels, a wave out: true while it is being ferried (the wave's other orders wait), having started a
    /// ferry if the way by land is cut off or long (see the top of this file).
    /// </summary>
    private bool Ferry(Simulation sim, Action<Command> issue)
    {
        if (_ferry == null && !StartFerry(sim, issue)) return false;
        if (StepCrossing(sim, _ferry!, _wave, issue, out bool over))
        {
            Note(sim, over ? "gives up ferrying its wave" : "has its wave across the water");
            EndCrossing(_ferry!);
            _ferry = null;
            // The wave sets out afresh from where it is.
            _waveBest = int.MaxValue;
            _waveCheck = sim.Tick;
        }
        return _ferry != null;
    }

    private bool StartFerry(Simulation sim, Action<Command> issue)
    {
        if (_ferryChecks >= FerryChecks || sim.Tick < _ferryNext || _waveLair != 0) return false;
        var goal = Target(sim, _waveBuilding) is { } foe ? (foe.AX, foe.AZ) : _waveGuess;
        if (goal is not var (gx, gz)) return false;
        var (cx, cz) = Centroid(sim, _wave);
        if (Dist2(cx, cz, gx, gz) < (long)FerryLeast * FerryLeast) return false;
        _ferryNext = sim.Tick + FerryRecheckTicks;
        _ferryChecks++;
        if (PlanCrossing(sim, _wave, gx, gz) is not { } c) return false;
        _ferry = c;
        _stage = null;
        SetOff(sim, c, _wave, issue);
        Note(sim, $"ferries its wave of {_wave.Count} on {c.Ships.Count} ships from {c.PX},{c.PZ} to {c.LX},{c.LZ}");
        return true;
    }

    /// <summary>
    /// Hard, a wave out and not being ferried itself: soldiers joining it whose way to it by land is cut off or long
    /// are lifted across by ship, as a wave is ferried, and put ashore on the shore nearest the wave from which they
    /// can walk to it; from there they walk on to join it. Soldiers sent to join it while the lift is under way go
    /// with it if they are on its side of the water.
    /// </summary>
    private void Lift(Simulation sim, Action<Command> issue)
    {
        var u = sim.Units;
        if (_lift == null)
        {
            if (_joining.Count == 0 || _ferry != null || _waveLair != 0 || sim.Tick < _liftNext) return;
            _liftNext = sim.Tick + FerryRecheckTicks;
            var (cx, cz) = Centroid(sim, _wave);
            // Those joining that are still far from the wave and on the ground: the lift's to take.
            var far = _joining.FindAll(id => u.IndexOf(id) is var i && i >= 0 && u.Aboard[i] == 0
                && Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz) >= (long)FerryLeast * FerryLeast);
            if (far.Count == 0 || PlanCrossing(sim, far, cx, cz) is not { } c) return;
            c.Members.AddRange(far);
            _lift = c;
            SetOff(sim, c, far, issue);
            Note(sim, $"lifts {far.Count} soldiers joining its wave on {c.Ships.Count} ships from {c.PX},{c.PZ} to {c.LX},{c.LZ}");
            return;
        }
        var lift = _lift;
        lift.Members.RemoveAll(id => !_joining.Contains(id));
        // Those sent to join since, on the side of the water it boards from, go with it.
        foreach (int id in _joining)
        {
            if (lift.Members.Contains(id) || lift.Rode.Contains(id) || u.IndexOf(id) is not (>= 0 and var i) || u.Aboard[i] != 0) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            if (Dist2(x, z, lift.PX, lift.PZ) < Dist2(x, z, lift.LX, lift.LZ)) lift.Members.Add(id);
        }
        if (!StepCrossing(sim, lift, lift.Members, issue, out bool over)) return;
        Note(sim, over ? "gives up lifting soldiers to its wave" : "has the soldiers joining its wave across the water");
        EndCrossing(lift);
        _lift = null;
    }

    /// <summary>True if the soldier joining the wave is the lift's to order about: waiting for it, or aboard.</summary>
    private bool Lifted(Simulation sim, int id) =>
        _lift is { } l && (l.Members.Contains(id) && !l.Rode.Contains(id) || sim.Units.IndexOf(id) is >= 0 and var i && sim.Units.Aboard[i] != 0);

    /// <summary>True if the ship is taking part in a crossing under way (the wave's ferry or a lift).</summary>
    private bool InCrossing(int ship) => _ferry?.Ships.Contains(ship) == true || _lift?.Ships.Contains(ship) == true;

    /// <summary>
    /// A crossing for the group to (gx, gz), if the way there by land is cut off or long and its free ships can take
    /// them: where they board (the shore nearest them that they can walk to) and where they land (the shore nearest
    /// the goal from which they can walk to it). Null if not.
    /// </summary>
    private Crossing? PlanCrossing(Simulation sim, List<int> group, int gx, int gz)
    {
        var u = sim.Units;
        var (cx, cz) = Centroid(sim, group);
        // The group's unit nearest its middle, standing on the ground: where it goes from.
        int from = -1;
        long best = long.MaxValue;
        foreach (int id in group)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Aboard[i] != 0) continue;
            long d = Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz);
            if (d < best) (best, from) = (d, i);
        }
        if (from < 0) return null;
        int node = sim.Walkable.NearestNode(u.X[from].FloorToInt(), u.Z[from].FloorToInt(), u.Y[from]);
        if (!ByLandFar(sim, node, gx, gz)) return null;
        var ships = Own(sim, Transport);
        ships.RemoveAll(id => id == _escort || InCrossing(id) || Aboard(sim, u.IndexOf(id)) > 0);
        if (ships.Count == 0)
        {
            _seaWanted = true; // the next look at the sea decides again
            return null;
        }
        int s = u.IndexOf(ships[0]);
        Chart(sim);
        SailFrom(sim, u.X[s].FloorToInt(), u.Z[s].FloorToInt());
        if (!Shore(sim, gx, gz, FerryLook, true, out int lx, out int lz)) return null;
        if (!Shore(sim, u.X[from].FloorToInt(), u.Z[from].FloorToInt(), PickLook, true, out int px, out int pz, node)) return null;
        // Ships that cannot get to where the group boards stay out of it.
        ships.RemoveAll(id => u.IndexOf(id) is var i && !SailsTo(sim, u.X[i].FloorToInt(), u.Z[i].FloorToInt(), px, pz));
        if (ships.Count == 0) return null;
        var c = new Crossing { PX = px, PZ = pz, LX = lx, LZ = lz, Start = sim.Tick };
        c.Ships.AddRange(ships);
        return c;
    }

    /// <summary>The crossing's ships sail to where the group boards, and the group walks there.</summary>
    private void SetOff(Simulation sim, Crossing c, List<int> group, Action<Command> issue)
    {
        var u = sim.Units;
        issue(new MoveCommand(Player, c.Ships.ToArray(), c.PX, c.PZ));
        var walk = new List<int>();
        foreach (int id in group)
            if (u.IndexOf(id) is var i && i >= 0 && u.Aboard[i] == 0) walk.Add(id);
        if (walk.Count > 0) issue(new MoveCommand(Player, walk.ToArray(), c.PX, c.PZ));
    }

    /// <summary>
    /// One look at a crossing under way: ships to where the group boards, soldiers aboard, full ships across. True
    /// once it is over: all across, or (<paramref name="over"/>) given up on, its ships lost or too long gone.
    /// </summary>
    private bool StepCrossing(Simulation sim, Crossing c, List<int> group, Action<Command> issue, out bool over)
    {
        int px = c.PX, pz = c.PZ, lx = c.LX, lz = c.LZ;
        var u = sim.Units;
        c.Ships.RemoveAll(id => !u.Exists(id));
        // Who has been aboard: one that has been and is ashore again is across (or went down with the ship).
        var waiting = new List<int>();
        int afloat = 0;
        foreach (int id in group)
        {
            int i = u.IndexOf(id);
            if (i < 0) continue;
            if (u.Aboard[i] != 0)
            {
                afloat++;
                if (!c.Rode.Contains(id)) c.Rode.Add(id);
            }
            else if (!c.Rode.Contains(id)) waiting.Add(id);
        }
        over = sim.Tick - c.Start > FerryTicks || c.Ships.Count == 0;
        if ((waiting.Count == 0 && afloat == 0) || over)
        {
            // Done (or given up on): whoever is still aboard is landed, and the group goes on from where it is.
            foreach (int id in c.Ships)
                if (u.IndexOf(id) is var s && Aboard(sim, s) > 0 && u.Job[s] != UnitJob.Land) issue(new MoveCommand(Player, new[] { id }, lx, lz));
            return true;
        }
        var spare = new List<int>(waiting);
        spare.RemoveAll(id => u.IndexOf(id) is var i && u.Job[i] == UnitJob.Board);
        foreach (int id in c.Ships)
        {
            int s = u.IndexOf(id);
            int load = Aboard(sim, s), seats = sim.Content.Units[u.Def[s]].Seats, coming = 0;
            foreach (int w in waiting)
                if (u.IndexOf(w) is var i && u.Job[i] == UnitJob.Board && u.JobTarget[i] == id) coming++;
            bool idle = u.Order[s] == UnitOrder.Idle;
            int sx = u.X[s].FloorToInt(), sz = u.Z[s].FloorToInt();
            if (load > 0)
            {
                // Full, or nobody else coming: across with them.
                _boardSince.TryGetValue(id, out long since);
                bool go = load >= seats || (coming == 0 && spare.Count == 0) || sim.Tick - since > BoardWaitTicks;
                if (go && (idle || u.Job[s] != UnitJob.Land))
                {
                    issue(new MoveCommand(Player, new[] { id }, lx, lz));
                    _boardSince.Remove(id);
                }
                continue;
            }
            if (!idle) continue;
            if (Dist2(sx, sz, px, pz) > (long)(BoatSystem.UnloadReach + 2) * (BoatSystem.UnloadReach + 2))
            {
                if (waiting.Count > 0) issue(new MoveCommand(Player, new[] { id }, px, pz));
                continue;
            }
            // At the shore where the group boards: the nearest waiting soldiers come aboard.
            if (spare.Count == 0 || coming > 0) continue;
            spare.Sort((a, b) =>
            {
                int ia = u.IndexOf(a), ib = u.IndexOf(b);
                int cmp = Dist2(u.X[ia].FloorToInt(), u.Z[ia].FloorToInt(), sx, sz).CompareTo(Dist2(u.X[ib].FloorToInt(), u.Z[ib].FloorToInt(), sx, sz));
                return cmp != 0 ? cmp : a.CompareTo(b);
            });
            var board = spare.GetRange(0, Math.Min(seats, spare.Count));
            spare.RemoveRange(0, board.Count);
            issue(new BoardCommand(Player, board.ToArray(), id));
            _boardSince[id] = sim.Tick;
        }
        // Soldiers not yet aboard or on their way aboard walk to where they board.
        var walk = Unordered(sim, spare, px, pz);
        walk.RemoveAll(id => u.IndexOf(id) is var i && Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), px, pz) <= (long)Slack * Slack);
        if (walk.Count > 0 && sim.Tick % (5 * ArmyTicks) < ArmyTicks) issue(new MoveCommand(Player, walk.ToArray(), px, pz));
        return false;
    }

    private void EndCrossing(Crossing c)
    {
        foreach (int id in c.Ships) _boardSince.Remove(id);
    }

    /// <summary>A new wave (or none): no ferry or lift, and its looks at the sea begin again.</summary>
    private void ResetFerry()
    {
        _ferry = null;
        _lift = null;
        _boardSince.Clear();
        _ferryChecks = 0;
        _ferryNext = 0;
        _liftNext = 0;
        _fleetGoal = 0;
        _fleetLook = 0;
    }

    // --- Bombard ships and their escort ------------------------------------------------------------

    /// <summary>
    /// The smart levels, every army look: bombard ships out with a wave (see the top of this file), their escort, and
    /// ships left with soldiers aboard landing them at home. The escort's crew is taken out of <paramref name="home"/>.
    /// </summary>
    private void Fleet(Simulation sim, Building hall, List<int> home, Action<Command> issue)
    {
        var u = sim.Units;
        _escortCrew.RemoveAll(id => !u.Exists(id));
        if (_escort != 0 && !u.Exists(_escort)) _escort = 0;
        var dock = HomeDock(sim);
        var (hx, hz) = dock != null ? Middle(dock) : Middle(hall);

        var bombards = Own(sim, Bombarder);
        var fit = new List<int>();
        foreach (int id in bombards)
        {
            int i = u.IndexOf(id);
            if (u.Hp[i] * 100 >= sim.Content.Units[u.Def[i]].MaxHp * ShipHurtPercent) fit.Add(id);
            else Home(sim, i, hx, hz, issue);
        }
        int goal = _wave.Count > 0 && _waveLair == 0 && fit.Count > 0 ? FleetGoal(sim, fit[0]) : 0;
        if (goal != 0)
        {
            var send = fit.FindAll(id => u.IndexOf(id) is var i && !(u.Combat[i] == CombatOrder.Attack && u.TargetBuilding[i] == goal));
            if (send.Count > 0) issue(new AttackBuildingCommand(Player, send.ToArray(), goal));
            EscortStep(sim, fit[0], home, hx, hz, issue);
        }
        else
        {
            foreach (int id in fit) Home(sim, u.IndexOf(id), hx, hz, issue);
            if (_escort != 0)
            {
                int e = u.IndexOf(_escort);
                if (Aboard(sim, e) > 0) Home(sim, e, hx, hz, issue);
                else
                {
                    _escort = 0;
                    _escortCrew.Clear();
                }
            }
        }
        home.RemoveAll(_escortCrew.Contains);

        // Ships left with soldiers aboard (a ferry given up, a wave called home) put them ashore at home.
        if (_ferry != null) return;
        foreach (int id in Own(sim, Transport))
        {
            if (id == _escort || InCrossing(id)) continue;
            int s = u.IndexOf(id);
            if (u.Order[s] == UnitOrder.Idle && Aboard(sim, s) > 0) issue(new MoveCommand(Player, new[] { id }, hx, hz));
        }
    }

    /// <summary>Ship i sails back to the shore by the dock at (hx, hz) if it is away (or busy) and not already on its way.</summary>
    private void Home(Simulation sim, int i, int hx, int hz, Action<Command> issue)
    {
        var u = sim.Units;
        bool away = Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), hx, hz) > (long)(BoatSystem.UnloadReach + 4) * (BoatSystem.UnloadReach + 4);
        bool going = u.Order[i] != UnitOrder.Idle && u.Combat[i] != CombatOrder.Attack && Dist2(u.OrderX[i], u.OrderZ[i], hx, hz) <= 4;
        if ((away || u.Combat[i] == CombatOrder.Attack || Aboard(sim, i) > 0 && u.Order[i] == UnitOrder.Idle) && !going)
            issue(new MoveCommand(Player, new[] { u.Id[i] }, hx, hz));
    }

    private Building? HomeDock(Simulation sim)
    {
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Active && Docks(sim, b.Def)) return b;
        return null;
    }

    /// <summary>
    /// What the bombard ships shell while the wave is after a building: the known enemy tower within
    /// <see cref="RamLook"/> of it nearest it that they can shell from the water, else the nearest wall or gate, else
    /// the building itself if they can; 0 if none.
    /// </summary>
    private int FleetGoal(Simulation sim, int lead)
    {
        if (Target(sim, _waveBuilding) is not { } foe) return 0;
        if (_fleetGoal != 0 && sim.Buildings.Get(_fleetGoal) is { } g && Rival(g.Owner)) return _fleetGoal;
        _fleetGoal = 0;
        // Working out the ways over the water takes a while: with nothing found, it looks again only now and then.
        if (sim.Tick < _fleetLook) return 0;
        _fleetLook = sim.Tick + FleetLookTicks;
        var u = sim.Units;
        int i = u.IndexOf(lead);
        int reach = BoatSystem.ReachOf(sim.Content.Units[u.Def[i]]);
        Chart(sim);
        SailFrom(sim, u.X[i].FloorToInt(), u.Z[i].FloorToInt());
        bool bestTower = false;
        long bestD = long.MaxValue;
        foreach (var k in _known.Values)
        {
            if (k.Kind is not (BuildingKind.Wall or BuildingKind.Gate or BuildingKind.Tower) || !Rival(k.Owner)) continue;
            long d = Dist2(k.X, k.Z, foe.X, foe.Z);
            if (d > (long)RamLook * RamLook) continue;
            bool tower = k.Kind == BuildingKind.Tower;
            if (_fleetGoal != 0 && (tower != bestTower ? !tower : d >= bestD)) continue;
            if (sim.Buildings.Get(k.Id) is not { } b || b.Owner != k.Owner || b.Planned || !Shellable(sim, b, reach)) continue;
            (_fleetGoal, bestTower, bestD) = (k.Id, tower, d);
        }
        if (_fleetGoal == 0 && sim.Buildings.Get(foe.Id) is { } t && Rival(t.Owner) && Shellable(sim, t, reach)) _fleetGoal = t.Id;
        return _fleetGoal;
    }

    /// <summary>
    /// The escort: a free transport ship takes up to <see cref="EscortCrew"/> ranged soldiers from home aboard, then
    /// keeps by the lead bombard ship.
    /// </summary>
    private void EscortStep(Simulation sim, int lead, List<int> home, int hx, int hz, Action<Command> issue)
    {
        var u = sim.Units;
        if (_ferry != null) return;
        if (_escort == 0)
        {
            var ships = Own(sim, Transport);
            ships.RemoveAll(id => InCrossing(id) || Aboard(sim, u.IndexOf(id)) > 0);
            if (ships.Count == 0) return;
            var crew = new List<(long D, int Id)>();
            int s0 = u.IndexOf(ships[0]);
            foreach (int id in home)
            {
                int i = u.IndexOf(id);
                if (i < 0 || u.Rout[i] > 0 || u.Aboard[i] != 0 || sim.Content.Units[u.Def[i]].Role != UnitRole.Ranged) continue;
                crew.Add((Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), u.X[s0].FloorToInt(), u.Z[s0].FloorToInt()), id));
            }
            if (crew.Count == 0) return;
            crew.Sort();
            _escort = ships[0];
            _escortIn = false;
            for (int k = 0; k < Math.Min(EscortCrew, crew.Count); k++) _escortCrew.Add(crew[k].Id);
            // It puts in at the shore by the dock first: out on the water nobody can walk aboard.
            issue(new MoveCommand(Player, new[] { _escort }, hx, hz));
            Note(sim, $"sends {_escortCrew.Count} archers aboard a ship to escort its bombard ships");
            return;
        }
        int e = u.IndexOf(_escort);
        if (!_escortIn)
        {
            if (u.Order[e] != UnitOrder.Idle) return;
            issue(new BoardCommand(Player, _escortCrew.ToArray(), _escort));
            _boardSince[_escort] = sim.Tick;
            _escortIn = true;
            return;
        }
        bool boarding = false;
        foreach (int id in _escortCrew)
            if (u.IndexOf(id) is var i && u.Job[i] == UnitJob.Board && u.JobTarget[i] == _escort) boarding = true;
        _boardSince.TryGetValue(_escort, out long since);
        if (boarding && sim.Tick - since <= BoardWaitTicks) return;
        // Those that did not get aboard stay home.
        _escortCrew.RemoveAll(id => u.IndexOf(id) is var i && u.Aboard[i] != _escort && u.Job[i] != UnitJob.Board);
        int l = u.IndexOf(lead);
        int x = u.X[l].FloorToInt(), z = u.Z[l].FloorToInt();
        if (Dist2(u.X[e].FloorToInt(), u.Z[e].FloorToInt(), x, z) <= 16) return;
        if (u.Order[e] != UnitOrder.Idle && Dist2(u.OrderX[e], u.OrderZ[e], x, z) <= 16) return;
        issue(new MoveCommand(Player, new[] { _escort }, x, z));
    }

    // --- The chart: water and land as it works them out -------------------------------------------

    private const short Unscanned = -2;
    private short[]? _top;
    private int[]? _sea;
    private bool[]? _landSeen;
    private readonly List<int> _landQueue = new();
    private readonly PriorityQueue<int, int> _seaQueue = new();

    /// <summary>Begins a look at the water: what it found out before may have changed.</summary>
    private void Chart(Simulation sim)
    {
        var w = sim.World;
        int n = w.SizeX * w.SizeZ;
        if (_top == null || _top.Length != n) _top = new short[n];
        Array.Fill(_top, Unscanned);
    }

    /// <summary>The level of the water on top of column (x, z), or -1 if its top block is not water (or it is off the map).</summary>
    private int WaterTop(Simulation sim, int x, int z)
    {
        var w = sim.World;
        if ((uint)x >= (uint)w.SizeX || (uint)z >= (uint)w.SizeZ) return -1;
        int c = z * w.SizeX + x;
        short t = _top![c];
        if (t != Unscanned) return t;
        t = -1;
        for (int y = w.SizeY - 1; y >= 0; y--)
        {
            var b = w.Get(x, y, z);
            if (b == BlockType.Air) continue;
            if (b == BlockType.Water) t = (short)y;
            break;
        }
        _top[c] = t;
        return t;
    }

    /// <summary>
    /// The ways by water from column (x, z) to every column, as a ship goes (see <see cref="BoatSystem"/>: water at
    /// the same level, no corner cut past land), into <see cref="_sea"/>; how many columns can be reached.
    /// </summary>
    private int SailFrom(Simulation sim, int x, int z)
    {
        var w = sim.World;
        int n = w.SizeX * w.SizeZ;
        if (_sea == null || _sea.Length != n) _sea = new int[n];
        Array.Fill(_sea, -1);
        if (WaterTop(sim, x, z) < 0) return 0;
        int count = 0;
        _seaQueue.Clear();
        _sea[z * w.SizeX + x] = 0;
        _seaQueue.Enqueue(z * w.SizeX + x, 0);
        while (_seaQueue.TryDequeue(out int c, out int d))
        {
            if (d != _sea[c]) continue;
            count++;
            int cx = c % w.SizeX, cz = c / w.SizeX, s = WaterTop(sim, cx, cz);
            for (int k = 0; k < WalkGraph.Dirs.Length; k++)
            {
                var (dx, dz) = WalkGraph.Dirs[k];
                int nx = cx + dx, nz = cz + dz;
                if (WaterTop(sim, nx, nz) != s) continue;
                if (dx != 0 && dz != 0 && (WaterTop(sim, nx, cz) != s || WaterTop(sim, cx, nz) != s)) continue;
                int nc = nz * w.SizeX + nx, nd = d + WalkGraph.DirCost[k];
                if (_sea[nc] >= 0 && _sea[nc] <= nd) continue;
                _sea[nc] = nd;
                _seaQueue.Enqueue(nc, nd);
            }
        }
        return count;
    }

    /// <summary>True if the last <see cref="SailFrom"/> reached the water within <see cref="BoatSystem.UnloadReach"/> columns of (x, z).</summary>
    private bool SailsTo(Simulation sim, int fx, int fz, int x, int z)
    {
        SailFrom(sim, fx, fz);
        var w = sim.World;
        int r = BoatSystem.UnloadReach;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            int cx = x + dx, cz = z + dz;
            if ((uint)cx < (uint)w.SizeX && (uint)cz < (uint)w.SizeZ && _sea![cz * w.SizeX + cx] >= 0) return true;
        }
        return false;
    }

    /// <summary>The water column within two columns of the footprint (x1..x2, z1..z2) a ship is launched onto, nearest its middle.</summary>
    private bool Launch(Simulation sim, int x1, int z1, int x2, int z2, out int wx, out int wz)
    {
        wx = wz = -1;
        long best = long.MaxValue;
        for (int cz = z1 - 2; cz <= z2 + 2; cz++)
        for (int cx = x1 - 2; cx <= x2 + 2; cx++)
        {
            if (cx >= x1 && cx <= x2 && cz >= z1 && cz <= z2) continue;
            if (WaterTop(sim, cx, cz) < 0) continue;
            long d = Dist2(2 * cx, 2 * cz, x1 + x2, z1 + z2);
            if (d < best) (best, wx, wz) = (d, cx, cz);
        }
        return wx >= 0;
    }

    /// <summary>
    /// A bombard ship from where the last <see cref="SailFrom"/> began can shell the building: some water it can sail
    /// to is within <paramref name="reach"/> columns of the building's middle.
    /// </summary>
    private bool Shellable(Simulation sim, Building b, int reach)
    {
        var w = sim.World;
        var (mx, mz) = Middle(b);
        for (int dz = -reach; dz <= reach; dz++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            int cx = mx + dx, cz = mz + dz;
            if (dx * dx + dz * dz > reach * reach || (uint)cx >= (uint)w.SizeX || (uint)cz >= (uint)w.SizeZ) continue;
            if (_sea![cz * w.SizeX + cx] >= 0) return true;
        }
        return false;
    }

    /// <summary>
    /// The ground nearest (x, z), within <paramref name="look"/> columns, that a ship from where the last
    /// <see cref="SailFrom"/> began can put soldiers ashore on (within <see cref="BoatSystem.LandReach"/> of water it
    /// can sail to, at about the water's level), and (with <paramref name="walk"/>) from which they can walk to (x, z),
    /// or (with a <paramref name="node"/> of the walk graph) to which they can walk from that node.
    /// </summary>
    private bool Shore(Simulation sim, int x, int z, int look, bool walk, out int sx, out int sz, int node = -1)
    {
        sx = sz = -1;
        var w = sim.World;
        var spots = new List<(long D, int X, int Z, int Y)>();
        int r = look + BoatSystem.UnloadReach;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            int cx = x + dx, cz = z + dz;
            if ((uint)cx >= (uint)w.SizeX || (uint)cz >= (uint)w.SizeZ || _sea![cz * w.SizeX + cx] < 0) continue;
            int y = WaterTop(sim, cx, cz) + 1;
            // The standing spot by this water nearest (x, z).
            long best = long.MaxValue;
            int ax = -1, az = -1, ay = -1;
            for (int ez = -BoatSystem.LandReach; ez <= BoatSystem.LandReach; ez++)
            for (int ex = -BoatSystem.LandReach; ex <= BoatSystem.LandReach; ex++)
            {
                int gx = cx + ex, gz = cz + ez;
                long d = Dist2(gx, gz, x, z);
                if ((ex == 0 && ez == 0) || d >= best || d > (long)look * look) continue;
                if (WaterTop(sim, gx, gz) >= 0) continue;
                int gy = Walk.NearestStandLevel(w, gx, gz, y, BoatSystem.BoardRise);
                if (gy < 0) continue;
                (best, ax, az, ay) = (d, gx, gz, gy);
            }
            if (ax >= 0) spots.Add((best, ax, az, ay));
        }
        spots.Sort();
        int tried = 0;
        for (int k = 0; k < spots.Count && tried < 6; k++)
        {
            var (d, ax, az, ay) = spots[k];
            if (k > 0 && spots[k - 1].X == ax && spots[k - 1].Z == az) continue;
            tried++;
            int limit = 2 * (int)IntMath.ISqrt(d) + 16;
            bool ok = !walk
                || (node >= 0 ? LandSteps(sim, node, ax, az, limit + FerryRatio * look) >= 0
                    : LandSteps(sim, sim.Walkable.NearestNode(ax, az, ay), x, z, limit) >= 0);
            if (!ok) continue;
            (sx, sz) = (ax, az);
            return true;
        }
        return false;
    }

    /// <summary>
    /// True if the way by land from the walk graph's node to (gx, gz) is cut off, or more than <see cref="FerryRatio"/>
    /// times the straight way.
    /// </summary>
    private bool ByLandFar(Simulation sim, int node, int gx, int gz)
    {
        var g = sim.Walkable;
        if (node < 0) return false;
        int x = g.ColumnX(node), z = g.ColumnZ(node);
        int straight = (int)IntMath.ISqrt(Dist2(x, z, gx, gz));
        if (straight < FerryLeast) return false;
        return LandSteps(sim, node, gx, gz, FerryRatio * straight) < 0;
    }

    /// <summary>
    /// Steps (straight or diagonal, each one) on the walk graph from the node to within two columns of (gx, gz), if
    /// that is at most <paramref name="limit"/>; else -1.
    /// </summary>
    private int LandSteps(Simulation sim, int node, int gx, int gz, int limit)
    {
        var g = sim.Walkable;
        if (node < 0) return -1;
        if (_landSeen == null || _landSeen.Length != g.NodeCount) _landSeen = new bool[g.NodeCount];
        else Array.Clear(_landSeen);
        _landQueue.Clear();
        _landQueue.Add(node);
        _landSeen[node] = true;
        int head = 0;
        for (int steps = 0; steps <= limit && head < _landQueue.Count; steps++)
        {
            int end = _landQueue.Count;
            for (; head < end; head++)
            {
                int n = _landQueue[head];
                if (Math.Abs(g.ColumnX(n) - gx) <= 2 && Math.Abs(g.ColumnZ(n) - gz) <= 2) return steps;
                for (int d = 0; d < WalkGraph.Dirs.Length; d++)
                {
                    int m = g.Step(n, d);
                    if (m < 0 || _landSeen[m]) continue;
                    _landSeen[m] = true;
                    _landQueue.Add(m);
                }
            }
        }
        return -1;
    }
}
