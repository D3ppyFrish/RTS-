using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Siege engines at the smart levels (Easy and up). Its economy: once it has Smelting, a barracks and enough workers,
// a smelter for iron, a sawmill for planks and then a siege workshop, one at a time; the workshop trains catapults
// like any barracks (two at a time, see Weight). Its waves: once a wave is within SiegeReach of its target, each
// catapult in it shoots the nearest tower in its reach, else the nearest wall or gate, else the target itself; one
// with an enemy soldier in its dead ground backs off; and a few of the wave's soldiers go for any enemy soldier
// that comes at a catapult, so the catapults have a screen in front of them. Battering rams: the workshop trains up
// to RamsWanted once it knows of an enemy wall or gate; in a wave a ram goes for the nearest gate within RamLook,
// else the nearest wall, else the target itself, and the same screen of soldiers keeps riders and spearmen off it.
public sealed partial class AiPlayer
{
    /// <summary>Workers it has before it puts up a smelter, a sawmill and a siege workshop.</summary>
    public const int SiegeWorkers = 16;
    /// <summary>A wave this close (columns) to its target has its catapults shoot: a little more than <see cref="StageDistance"/>.</summary>
    public const int SiegeReach = StageDistance + 4;
    /// <summary>A catapult shoots walls, gates and towers this many columns past its range (it walks up to them).</summary>
    public const int SiegeLook = 4;
    /// <summary>
    /// Enemy soldiers this close (columns) to a catapult of a wave are fought by up to <see cref="ScreenSoldiers"/>
    /// of the wave's other soldiers.
    /// </summary>
    public const int ScreenRadius = 9, ScreenSoldiers = 4;
    /// <summary>A catapult with an enemy soldier in its dead ground backs off this many columns from it.</summary>
    public const int BackOff = 8;
    /// <summary>Rams it keeps (with those queued) while it knows of an enemy wall or gate.</summary>
    public const int RamsWanted = 2;
    /// <summary>A ram in a wave goes for a gate or wall this close (columns): it walks up to them.</summary>
    public const int RamLook = SiegeReach + 4;

    /// <summary>The wave's catapults handled this look, and its soldiers screening them: the rest of the wave's orders leave them be.</summary>
    private readonly List<int> _engines = new(), _shield = new();

    /// <summary>True if the building trains only siege engines (a siege workshop): it is not counted as a barracks.</summary>
    public static bool SiegeWorks(Simulation sim, BuildingDef def)
    {
        if (def.Kind != BuildingKind.Barracks || def.Trains.Length == 0) return false;
        foreach (string id in def.Trains)
            if (!sim.Content.TryGetUnit(id, out var d) || !Engine(d)) return false;
        return true;
    }

    /// <summary>A siege engine: a machine of the siege role (a catapult), as against a siege soldier.</summary>
    private static bool Engine(UnitDef def) => def.Role == UnitRole.Siege && def.Machine;

    /// <summary>True if unit i is a siege engine.</summary>
    private static bool EngineAt(Simulation sim, int i) => i >= 0 && Engine(sim.Content.Units[sim.Units.Def[i]]);

    /// <summary>Takes the siege engines out of the list and returns them, in the same order.</summary>
    private static List<int> HoldEngines(Simulation sim, List<int> ids)
    {
        var engines = ids.FindAll(id => EngineAt(sim, sim.Units.IndexOf(id)));
        if (engines.Count > 0) ids.RemoveAll(id => EngineAt(sim, sim.Units.IndexOf(id)));
        return engines;
    }

    /// <summary>A barracks (counted as one: not a siege workshop or a dock).</summary>
    private static bool IsBarracks(Simulation sim, BuildingDef def) => def.Kind == BuildingKind.Barracks && !SiegeWorks(sim, def) && !Docks(sim, def);

    private BuildingDef? SiegeWorksDef(Simulation sim)
    {
        foreach (var d in sim.Content.Buildings)
            if (d.UpgradeOf == null && SiegeWorks(sim, d) && sim.Economy.Unlocked(Player, d)) return d;
        return null;
    }

    private BuildingDef? Making(Simulation sim, Good good)
    {
        foreach (var d in sim.Content.Buildings)
            if (d.Kind == BuildingKind.Workshop && d.Makes == good && d.UpgradeOf == null && sim.Economy.Unlocked(Player, d)) return d;
        return null;
    }

    /// <summary>
    /// The smart levels: with a siege workshop to build (Smelting), a finished barracks and
    /// <see cref="SiegeWorkers"/> workers, it puts up a smelter, then a sawmill, then a siege workshop, each once it
    /// can pay for it and the one before is laid out. Its automatic workers keep the smelter and the sawmill going.
    /// </summary>
    private void SiegeEconomy(Simulation sim, Building hall, int workers, Action<Command> issue)
    {
        if (!Smart || workers < SiegeWorkers) return;
        var works = SiegeWorksDef(sim);
        if (works == null) return;
        bool barracks = false, smelter = false, sawmill = false, shop = false, site = false;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player) continue;
            bool mine = true;
            if (SiegeWorks(sim, b.Def)) shop = true;
            else if (b.Def.Kind == BuildingKind.Barracks) { barracks |= b.Active; mine = false; }
            else if (b.Def.Kind == BuildingKind.Smelter) smelter = true;
            else if (b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == Good.Planks) sawmill = true;
            else mine = false;
            site |= mine && !b.Active;
        }
        if (!barracks || site) return;
        var next = !smelter ? FirstBuilding(sim, BuildingKind.Smelter) : !sawmill ? Making(sim, Good.Planks) : !shop ? works : null;
        if (next == null || !Affordable(sim, next.Cost)) return;
        if (Hard) Want(sim, next, hall, HardTownRadius, false, issue);
        else PlaceNear(sim, next, Middle(hall), Automation.AutomationSystem.PlanRadius, issue);
        Note(sim, $"lays out a {next.Name.ToLowerInvariant()} for siege engines");
    }

    /// <summary>
    /// The smart levels, a wave within <see cref="SiegeReach"/> of its target: orders for its catapults (see the
    /// top of this file) and for the soldiers screening them, who are listed in <see cref="_engines"/> and
    /// <see cref="_shield"/> for the rest of the wave's orders to leave be.
    /// </summary>
    private void Bombard(Simulation sim, Foe foe, Action<Command> issue)
    {
        var u = sim.Units;
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i >= 0 && Engine(sim.Content.Units[u.Def[i]])) _engines.Add(id);
        }
        if (_engines.Count == 0) return;
        var shots = new SortedDictionary<int, List<int>>();
        var threats = new List<int>();
        foreach (int id in _engines)
        {
            int i = u.IndexOf(id);
            var def = sim.Content.Units[u.Def[i]];
            int ex = u.X[i].FloorToInt(), ez = u.Z[i].FloorToInt();
            int goal;
            if (def.Ram)
            {
                // A ram has no dead ground to back out of: it goes on at the gate, and the screen keeps foes off it.
                int foeNear = NearestEnemy(sim, ex, ez, ScreenRadius, soldiersOnly: true, out _);
                if (foeNear >= 0 && !threats.Contains(foeNear)) threats.Add(foeNear);
                goal = Breach(sim, ex, ez, RamLook);
            }
            else
            {
                int close = NearestEnemy(sim, ex, ez, def.MinRange.FloorToInt() + 2, soldiersOnly: true, out _);
                if (close >= 0)
                {
                    // In its dead ground: it cannot hit it, so it backs off while the screen deals with it.
                    threats.Add(close);
                    if (u.Order[i] != UnitOrder.Move)
                    {
                        int nx = u.X[close].FloorToInt(), nz = u.Z[close].FloorToInt();
                        var (bx, bz) = Toward(ex, ez, 2 * ex - nx, 2 * ez - nz, BackOff, sim.World);
                        issue(new MoveCommand(Player, new[] { id }, bx, bz));
                    }
                    continue;
                }
                int near = NearestEnemy(sim, ex, ez, ScreenRadius, soldiersOnly: true, out _);
                if (near >= 0 && !threats.Contains(near)) threats.Add(near);
                goal = Rampart(sim, ex, ez, def.Range.FloorToInt() + SiegeLook);
            }
            if (goal == 0 && sim.Buildings.Get(foe.Id) is { } t && Rival(t.Owner)) goal = t.Id;
            if (goal == 0 || (u.Combat[i] == CombatOrder.Attack && u.TargetBuilding[i] == goal)) continue;
            if (!shots.TryGetValue(goal, out var list)) shots[goal] = list = new List<int>();
            list.Add(id);
        }
        foreach (var (goal, list) in shots) issue(new AttackBuildingCommand(Player, list.ToArray(), goal));
        Screen(sim, threats, issue);
    }

    /// <summary>
    /// The known enemy tower nearest (x, z) within the reach, else the nearest wall or gate; 0 if none. Towers
    /// first: their archers shoot the wave, and a wall only holds it up.
    /// </summary>
    private int Rampart(Simulation sim, int x, int z, int reach)
    {
        int best = 0;
        bool bestTower = false;
        long bestD = (long)reach * reach + 1;
        foreach (var k in _known.Values)
        {
            if (k.Kind is not (BuildingKind.Wall or BuildingKind.Gate or BuildingKind.Tower) || !Rival(k.Owner)) continue;
            if (sim.Buildings.Get(k.Id) is not { } b || b.Owner != k.Owner || b.Planned) continue;
            long d = Dist2(k.X, k.Z, x, z);
            if (d > (long)reach * reach) continue;
            bool tower = k.Kind == BuildingKind.Tower;
            if (best != 0 && (tower != bestTower ? !tower : d >= bestD)) continue;
            (best, bestTower, bestD) = (k.Id, tower, d);
        }
        return best;
    }

    /// <summary>
    /// The known enemy gate nearest (x, z) within the reach, else the nearest wall; 0 if none. Gates first: a ram
    /// breaks them twice as fast, and a gate down opens the way as well as a gap.
    /// </summary>
    private int Breach(Simulation sim, int x, int z, int reach)
    {
        int best = 0;
        bool bestGate = false;
        long bestD = (long)reach * reach + 1;
        foreach (var k in _known.Values)
        {
            if (k.Kind is not (BuildingKind.Wall or BuildingKind.Gate) || !Rival(k.Owner)) continue;
            if (sim.Buildings.Get(k.Id) is not { } b || b.Owner != k.Owner || b.Planned) continue;
            long d = Dist2(k.X, k.Z, x, z);
            if (d > (long)reach * reach) continue;
            bool gate = k.Kind == BuildingKind.Gate;
            if (best != 0 && (gate != bestGate ? !gate : d >= bestD)) continue;
            (best, bestGate, bestD) = (k.Id, gate, d);
        }
        return best;
    }

    /// <summary>True if it knows of an enemy wall or gate standing: its siege workshop then trains rams.</summary>
    public bool KnowsWalls(Simulation sim)
    {
        foreach (var k in _known.Values)
            if (k.Kind is BuildingKind.Wall or BuildingKind.Gate && Rival(k.Owner) && sim.Buildings.Get(k.Id) is { } b && b.Owner == k.Owner && !b.Planned)
                return true;
        return false;
    }

    /// <summary>Its rams, with those queued at its siege workshops.</summary>
    private int Rams(Simulation sim)
    {
        int n = 0;
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && sim.Content.Units[u.Def[i]].Ram) n++;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Def.Kind != BuildingKind.Barracks) continue;
            foreach (int q in b.Queue) if (sim.Content.Units[q].Ram) n++;
        }
        return n;
    }

    /// <summary>
    /// Keeps up to <see cref="ScreenSoldiers"/> of the wave's other soldiers on the enemy soldiers at its catapults:
    /// those already fighting one of them stay on it, and the nearest free ones are sent at the first.
    /// </summary>
    private void Screen(Simulation sim, List<int> threats, Action<Command> issue)
    {
        if (threats.Count == 0) return;
        var u = sim.Units;
        var free = new List<(long D, int Id)>();
        int tx = u.X[threats[0]].FloorToInt(), tz = u.Z[threats[0]].FloorToInt();
        foreach (int id in _wave)
        {
            if (_engines.Contains(id)) continue;
            int i = u.IndexOf(id);
            if (i < 0 || u.Rout[i] > 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (def.Role == UnitRole.Sapper) continue;
            int target = u.Target[i] != 0 ? u.IndexOf(u.Target[i]) : -1;
            if (target >= 0 && threats.Contains(target))
            {
                _shield.Add(id);
                continue;
            }
            free.Add((Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), tx, tz), id));
        }
        int want = ScreenSoldiers - _shield.Count;
        if (want <= 0 || free.Count == 0) return;
        free.Sort();
        var send = new int[Math.Min(want, free.Count)];
        for (int k = 0; k < send.Length; k++) send[k] = free[k].Id;
        _shield.AddRange(send);
        issue(new AttackCommand(Player, send, u.Id[threats[0]]));
    }
}
