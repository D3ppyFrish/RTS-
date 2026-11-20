using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.Automation;

/// <summary>
/// Town governors (<see cref="GovernCommand"/>): a player can put a governor in charge of a town hall, with
/// a focus. Every <see cref="GovernTicks"/> the governor looks over the settlement, the ground within
/// <see cref="GovernRadius"/> columns of the hall, and gives the commands the player would:
///
/// 1. Idle workers there that are not automatic, and that the player has not held with an order of their
///    own, are made automatic, so they find their own jobs.
/// 2. With no site of the player's in the settlement still being built, it places one building, the first
///    that is wanted: a house when fewer than <see cref="HouseHeadroom"/> places are free in the player's
///    housing; a farm when food runs short and every farm is fully worked; then what its focus asks for.
///    Growth wants a storehouse out at the edge of the settlement once the player has
///    <see cref="StorehouseWorkers"/> workers, then a smelter once there is iron ore to smelt. Army wants a
///    barracks. Defence wants a barracks, then a watchtower at each of the four corners
///    <see cref="TowerRing"/> columns out from the hall.
/// 3. The hall trains a worker while the player has fewer than the focus's <see cref="WorkerTarget"/>.
///    With Army or Defence, each barracks in the settlement with nothing queued trains a soldier: Army the
///    fighting type the player has fewest of, Defence the ranged type they have fewest of (or else line
///    infantry). Soldiers wait while a wanted building is waiting for goods, so the town keeps up with its
///    army. Nothing is trained without room in the houses, or without a minute's food left for everyone.
/// 4. At the player's research hall, with nothing being researched, it starts the next tech its focus
///    needs (see <see cref="Research"/>): farming first, then smelting for Growth, the barracks' soldiers'
///    techs for Army, archery for Defence. Until then, Defence trains line infantry.
///
/// A captured hall loses its governor. A player who makes all their workers automatic gets houses and
/// farms put up near their hall anyway (see <see cref="Plan"/>); with a governor in that hall, the governor
/// does it instead.
/// </summary>
public sealed partial class AutomationSystem
{
    /// <summary>Ticks between a governor's looks at its settlement.</summary>
    public const int GovernTicks = 2 * Tps;
    /// <summary>How far (columns from the middle of the hall) a governor's settlement reaches.</summary>
    public const int GovernRadius = PlanRadius;
    /// <summary>A governor puts up a house when fewer places than this are free in the player's housing.</summary>
    public const int HouseHeadroom = 4;
    /// <summary>Workers the player has before a Growth governor puts up a storehouse.</summary>
    public const int StorehouseWorkers = 10;
    /// <summary>Columns from the hall's middle a Growth governor's storehouse stands at least.</summary>
    public const int StorehouseRing = 12;
    /// <summary>Iron ore in store before a Growth governor puts up a smelter.</summary>
    public const int SmelterOre = 20;
    /// <summary>Columns (along each axis) from the hall's middle to where a Defence governor puts each watchtower.</summary>
    public const int TowerRing = 9;
    /// <summary>How far (columns) from each corner spot a Defence governor looks for room for its watchtower.</summary>
    public const int TowerSlack = 4;

    /// <summary>The workers a governor's hall trains up to, by focus.</summary>
    public static int WorkerTarget(GovernorFocus focus) => focus switch
    {
        GovernorFocus.Growth => 30,
        GovernorFocus.Army => 15,
        GovernorFocus.Defence => 20,
        _ => 0,
    };

    private static readonly (int Dx, int Dz)[] TowerCorners = { (-1, -1), (1, -1), (-1, 1), (1, 1) };

    /// <summary>Puts a governor with that focus in charge of the player's own town hall, or takes it away.</summary>
    internal void Apply(GovernCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (b == null || b.Owner != c.PlayerId || b.Def.Kind != BuildingKind.TownHall || b.Planned) return;
        if (!Enum.IsDefined(c.Focus)) return;
        b.Governor = c.Focus;
    }

    /// <summary>True if the hall the player's houses and farms are planned around (see <see cref="Plan"/>) has a governor.</summary>
    private bool Governed(int p) => Economy.TownHall(p) is { Governor: not GovernorFocus.None };

    /// <summary>Each governed town hall, every <see cref="GovernTicks"/>.</summary>
    private void StepGovernors()
    {
        long tick = _sim.Tick;
        var all = Buildings.All;
        for (int k = 0; k < all.Count; k++)
        {
            var hall = all[k];
            if (hall.Governor == GovernorFocus.None || !hall.Active || (tick + hall.Id) % GovernTicks != 0) continue;
            Govern(hall);
        }
    }

    private void Govern(Building hall)
    {
        int p = hall.Owner;
        if (p < 0 || p >= Economy.PlayerCount) return;
        var focus = hall.Governor;
        int cx = hall.X + hall.Def.SizeX / 2, cz = hall.Z + hall.Def.SizeZ / 2;

        AutomateIdleWorkers(p, cx, cz);

        Refresh();
        if (p >= _stats.Count) return;
        var s = _stats[p];

        // One building at a time: none while a site of the player's in the settlement is still going up.
        bool saving = false;
        if (!SiteNear(p, cx, cz))
        {
            var (def, x, z) = Wanted(p, hall, focus, s, cx, cz);
            if (def != null)
            {
                if (Economy.CanAfford(p, def.Cost))
                {
                    Issue(new BuildCommand(p, Array.Empty<int>(), def.Id, x, z));
                    Refresh();
                }
                else saving = true;
            }
        }

        // A worker at the hall, while the player has fewer than the focus wants.
        if (hall.Queue.Count == 0 && s.Workers + WorkersQueued(p) < WorkerTarget(focus) && HasRoom(s))
        {
            var worker = TrainableAt(hall, p, d => d.Role == UnitRole.Worker);
            if (worker != null && CanPay(s, worker.Cost))
            {
                Issue(new TrainCommand(p, hall.Id, worker.Id));
                Refresh();
            }
        }

        if (saving) return;
        if (hall == Economy.TownHall(p)) Research(p, focus, s);

        if (focus is not (GovernorFocus.Army or GovernorFocus.Defence)) return;
        long r2 = (long)GovernRadius * GovernRadius;
        var all = Buildings.All;
        for (int k = 0; k < all.Count; k++)
        {
            var b = all[k];
            if (b.Owner != p || !b.Active || b.Def.Kind != BuildingKind.Barracks || b.Queue.Count > 0) continue;
            if (b.CentreDistance2(cx, cz) > 4 * r2) continue; // CentreDistance2 is in half-columns
            if (!HasRoom(s)) return;
            var soldier = NextSoldier(b, p, focus);
            if (soldier == null || !CanPay(s, soldier.Cost)) continue;
            Issue(new TrainCommand(p, b.Id, soldier.Id));
            Refresh();
        }
    }

    /// <summary>
    /// With nothing being researched, starts the first tech the focus needs that the player can pay for (and
    /// still keep a minute's food): what farms need, then for Growth what smelters need, for Army what each
    /// fighting unit at the barracks needs, for Defence what its ranged units need. A tech that needs
    /// another first starts with that one. Which techs these are comes from the data.
    /// </summary>
    private void Research(int p, GovernorFocus focus, Stats s)
    {
        if (Economy.Researching(p) != null) return;
        foreach (var want in TechWants(p, focus))
        {
            var tech = FirstMissing(p, want);
            if (tech == null || Economy.CanResearch(p, tech, checkCost: false) != ResearchResult.Ok) continue;
            if (!CanPay(s, tech.Cost)) return; // saves up for it rather than skipping ahead
            Issue(new ResearchCommand(p, tech.Id));
            Refresh();
            return;
        }
    }

    /// <summary>The techs (in order) that unlock what the focus builds and trains.</summary>
    private IEnumerable<TechDef> TechWants(int p, GovernorFocus focus)
    {
        var farm = Wants(p, BuildingKind.Farm);
        if (farm != null)
            foreach (var t in farm.Requires) yield return t;
        if (focus == GovernorFocus.Growth)
        {
            var smelter = Wants(p, BuildingKind.Smelter);
            if (smelter != null)
                foreach (var t in smelter.Requires) yield return t;
            yield break;
        }
        var barracks = Wants(p, BuildingKind.Barracks);
        if (barracks == null) yield break;
        foreach (var id in barracks.Trains)
        {
            if (!_sim.Content.TryGetUnit(id, out var d) || !Fights(d.Role)) continue;
            if (d.Faction != null && d.Faction != Economy.Faction(p)) continue;
            if (focus == GovernorFocus.Defence && d.Role != UnitRole.Ranged) continue;
            foreach (var t in d.Requires) yield return t;
        }
    }

    /// <summary>The first building of that kind (in data order) the player's faction could build, techs aside; null if none.</summary>
    private BuildingDef? Wants(int p, BuildingKind kind)
    {
        foreach (var d in _sim.Content.Buildings)
            if (d.Kind == kind && !d.FromDen && d.UpgradeOf == null && d.FacingOf == null && (d.Faction == null || d.Faction == Economy.Faction(p)))
                return d;
        return null;
    }

    /// <summary>The tech to research first on the way to <paramref name="want"/>: it, or one it needs; null if the player has it.</summary>
    private TechDef? FirstMissing(int p, TechDef want)
    {
        for (int depth = 0; depth < 16; depth++)
        {
            if (Economy.HasTech(p, want)) return null;
            var before = Economy.MissingTech(p, want.Requires);
            if (before == null) return want;
            want = before;
        }
        return null;
    }

    /// <summary>Makes the player's idle, manual workers in the settlement automatic (those they held are left alone).</summary>
    private void AutomateIdleWorkers(int p, int cx, int cz)
    {
        var u = U;
        List<int>? ids = null;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != p || u.Auto[i] || u.Held[i] || !IsWorker(i) || !Idle(i)) continue;
            if (!InSettlement(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz)) continue;
            (ids ??= new List<int>()).Add(u.Id[i]);
        }
        if (ids != null) Issue(new AutomateCommand(p, ids.ToArray(), true));
    }

    private static bool InSettlement(int x, int z, int cx, int cz)
    {
        long dx = x - cx, dz = z - cz;
        return dx * dx + dz * dz <= (long)GovernRadius * GovernRadius;
    }

    /// <summary>True if a site of the player's (not a plan) stands in the settlement.</summary>
    private bool SiteNear(int p, int cx, int cz)
    {
        long r2 = 4L * GovernRadius * GovernRadius;
        foreach (var b in Buildings.All)
            if (b.Owner == p && b.State == BuildingState.Site && b.CentreDistance2(cx, cz) <= r2) return true;
        return false;
    }

    /// <summary>True if the player has a building of that kind (built or being built, not planned) within <paramref name="radius"/> columns of (cx, cz).</summary>
    private bool HasNear(int p, BuildingKind kind, int cx, int cz, int radius)
    {
        long r2 = 4L * radius * radius;
        foreach (var b in Buildings.All)
            if (b.Owner == p && b.Def.Kind == kind && !b.Planned && b.CentreDistance2(cx, cz) <= r2) return true;
        return false;
    }

    /// <summary>
    /// The building the settlement wants next and where it goes, or (null, ..) if it wants none or there is
    /// no room for it.
    /// </summary>
    private (BuildingDef? Def, int X, int Z) Wanted(int p, Building hall, GovernorFocus focus, Stats s, int cx, int cz)
    {
        int x, z;
        BuildingDef? def;
        if (s.Housing - (s.Population + s.Waiting) < HouseHeadroom && (def = Unlocked(p, BuildingKind.House)) != null
            && Room(p, def, hall, out x, out z))
            return (def, x, z);
        bool foodShort = s.Stock[(int)Good.Food] < FoodShort;
        if (foodShort && s.FreeFarmSlots == 0 && s.FarmSlots < Math.Max(2, s.Workers / 3)
            && (def = Unlocked(p, BuildingKind.Farm)) != null && Room(p, def, hall, out x, out z))
            return (def, x, z);

        switch (focus)
        {
            case GovernorFocus.Growth:
                if (s.Workers >= StorehouseWorkers && !HasNear(p, BuildingKind.Storehouse, cx, cz, GovernRadius)
                    && (def = Unlocked(p, BuildingKind.Storehouse)) != null
                    && FindRoom(p, def, cx, cz, GovernRadius, out x, out z, (bx, bz) => Far(def, bx, bz, cx, cz, StorehouseRing)))
                    return (def, x, z);
                if (!s.Smelter && s.Stock[(int)Good.IronOre] >= SmelterOre && !HasNear(p, BuildingKind.Smelter, cx, cz, GovernRadius)
                    && (def = Unlocked(p, BuildingKind.Smelter)) != null && Room(p, def, hall, out x, out z))
                    return (def, x, z);
                break;
            case GovernorFocus.Army or GovernorFocus.Defence:
                if (!HasNear(p, BuildingKind.Barracks, cx, cz, GovernRadius) && (def = Unlocked(p, BuildingKind.Barracks)) != null
                    && Room(p, def, hall, out x, out z))
                    return (def, x, z);
                if (focus == GovernorFocus.Defence && (def = Unlocked(p, BuildingKind.Tower)) != null)
                    foreach (var (dx, dz) in TowerCorners)
                    {
                        int tx = cx + dx * TowerRing, tz = cz + dz * TowerRing;
                        if (HasNear(p, BuildingKind.Tower, tx, tz, TowerSlack + 1)) continue;
                        if (FindRoom(p, def, tx, tz, TowerSlack, out x, out z)) return (def, x, z);
                    }
                break;
        }
        return (null, 0, 0);
    }

    /// <summary>True if a building with its corner at (x, z) has its middle at least <paramref name="ring"/> columns from (cx, cz).</summary>
    private static bool Far(BuildingDef def, int x, int z, int cx, int cz, int ring)
    {
        long dx = x + def.SizeX / 2 - cx, dz = z + def.SizeZ / 2 - cz;
        return dx * dx + dz * dz >= (long)ring * ring;
    }

    /// <summary>True if there is room in the player's housing for one more unit beyond those waiting.</summary>
    private static bool HasRoom(Stats s) => s.Population + s.Waiting < s.Housing;

    /// <summary>True if the player can pay (as repeating training checks).</summary>
    private static bool CanPay(Stats s, GoodAmount[] cost)
    {
        foreach (var c in cost)
            if (s.Stock[(int)c.Good] < c.Amount) return false;
        return true;
    }

    /// <summary>Workers queued or owed at the player's buildings.</summary>
    private int WorkersQueued(int p)
    {
        int n = 0;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != p) continue;
            foreach (int q in b.Queue)
                if (_sim.Content.Units[q].Role == UnitRole.Worker) n++;
        }
        return n;
    }

    /// <summary>The first unit (in the building's list) that it trains, the player may train, and that matches.</summary>
    private UnitDef? TrainableAt(Building b, int p, Func<UnitDef, bool> match)
    {
        foreach (var id in b.Def.Trains)
            if (_sim.Content.TryGetUnit(id, out var d) && match(d) && Economy.Unlocked(p, d)) return d;
        return null;
    }

    private static bool Fights(UnitRole r) => r is UnitRole.LineInfantry or UnitRole.HeavyInfantry or UnitRole.Ranged or UnitRole.Cavalry;

    /// <summary>
    /// The soldier a governor trains next at barracks b: of the types it trains that the player may train
    /// (ranged only for Defence, if there are any), the one the player has fewest of; the first in the list on a tie.
    /// </summary>
    private UnitDef? NextSoldier(Building b, int p, GovernorFocus focus)
    {
        var options = new List<UnitDef>();
        foreach (var id in b.Def.Trains)
            if (_sim.Content.TryGetUnit(id, out var d) && Fights(d.Role) && Economy.Unlocked(p, d)) options.Add(d);
        if (focus == GovernorFocus.Defence)
        {
            var ranged = options.FindAll(d => d.Role == UnitRole.Ranged);
            options = ranged.Count > 0 ? ranged : options.FindAll(d => d.Role == UnitRole.LineInfantry);
        }
        if (options.Count == 0) return null;
        var counts = new int[options.Count];
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != p) continue;
            for (int k = 0; k < options.Count; k++)
                if (u.Def[i] == options[k].Index) counts[k]++;
        }
        foreach (var q in Buildings.All)
        {
            if (q.Owner != p) continue;
            foreach (int d in q.Queue)
                for (int k = 0; k < options.Count; k++)
                    if (d == options[k].Index) counts[k]++;
        }
        int best = 0;
        for (int k = 1; k < options.Count; k++)
            if (counts[k] < counts[best]) best = k;
        return options[best];
    }
}
