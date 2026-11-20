using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// The Hard level (issue #2): the best the computer plays. Like the other smart levels it goes only by what
// it has seen (see Look in AiPlayer.Smart.cs) and has only the goods its own workers bring in; what makes it
// hard is how it uses them. This file is its economy; its army is in AiPlayer.HardArmy.cs.
public sealed partial class AiPlayer
{
    /// <summary>Hard: workers wanted at the start, how many more each minute, and at most.</summary>
    public const int HardFirstWorkers = 12, HardWorkersPerMinute = 3, HardMostWorkers = 48;
    /// <summary>
    /// Hard: the share (percent) of its workers, masons aside, it keeps on its farms when it has as much food
    /// as wood to spend: a soldier costs about as much work in wood (with its room in a house) as in food,
    /// and wood comes in slower. The share goes up a point for every <see cref="SharePer"/> more wood than
    /// food it has to spend and down as much the other way, between the second and third numbers.
    /// </summary>
    public const int HardFarmPercent = 45, HardLeastFarmPercent = 25, HardMostFarmPercent = 70, SharePer = 8;
    /// <summary>Hard: ticks between looks at who works at what (see <see cref="Assign"/>), and workers moved at a time.</summary>
    public const int AssignTicks = 8 * Tps, AssignedAtOnce = 2;
    /// <summary>
    /// Hard: workers kept at its quarry while stone is short (two, and one more for every
    /// <see cref="MasonsPer"/> workers, at most <see cref="MostMasons"/>); short is less than
    /// <see cref="StoneWanted"/> on top of what it is saving for, and with <see cref="StonePlenty"/> on top one
    /// mason stays.
    /// </summary>
    public const int MasonsPer = 12, MostMasons = 5, StoneWanted = 60, StonePlenty = 200;
    /// <summary>Hard: no quarry before it has this many workers, unless it is saving up for something it has not the stone for.</summary>
    public const int QuarryWorkers = 16;
    /// <summary>
    /// Hard: its quarry is a pit this long and wide, dug this deep (the top three levels are soil, the rest
    /// mostly stone), at least <see cref="QuarryNear"/> and at most <see cref="QuarryFar"/> columns from the
    /// town hall's middle. The stair the diggers leave runs along the long side, so it has to be longer
    /// than the pit is deep.
    /// </summary>
    public const int QuarryLong = 14, QuarryWide = 6, QuarryDepth = 12, QuarryNear = 14, QuarryFar = 30;
    /// <summary>Hard: while it has no ground for a quarry it looks for some this often (ticks).</summary>
    public const int QuarryLookTicks = 10 * Tps;
    /// <summary>Hard: it settles for a lesser quarry (see <see cref="QuarrySecond"/>) only from this tick, when it has had a look round, or when it needs the stone.</summary>
    public const int QuarrySettleTicks = 2 * 60 * Tps;
    /// <summary>Hard: sites of one kind (houses, farms) it has going at once.</summary>
    public const int HardSitesAtOnce = 2;
    /// <summary>Hard: at most this many barracks, and storehouses.</summary>
    public const int HardMostBarracks = 6, HardMostStorehouses = 12;
    /// <summary>Hard: its first barracks once it has this many workers.</summary>
    public const int HardBarracksWorkers = 8;
    /// <summary>
    /// Hard: another barracks when every one it has is training, its people have room, and it has this much
    /// food and wood left over on top of the price: goods are piling up faster than it can train.
    /// </summary>
    public const int HardSpareFood = 60, HardSpareWood = 30;
    /// <summary>
    /// Hard: a second town hall (the match is lost with the last one, and it trains workers too) once it
    /// has this many workers and barracks, this many columns from the first on the side away from the enemy.
    /// </summary>
    public const int HardExpandWorkers = 30, HardExpandBarracks = 2, HardExpandDistance = 44;
    /// <summary>Hard: while enemies are at its buildings, and this long (ticks) after, it saves up for nothing but soldiers.</summary>
    public const int AlarmTicks = 20 * Tps;
    /// <summary>Hard: how far (columns) from a town hall it looks for room for houses and farms.</summary>
    public const int HardTownRadius = 34;

    /// <summary>Techs in the order Hard researches them (those its faction cannot have are skipped).</summary>
    public static readonly string[] HardTechOrder =
    {
        "farming", "tools", "crop_rotation", "horse_breeding", "roads", "carts", "archery", "masonry", "settler_guilds",
        "orc_beast_pens", "orc_war_drums", "sapping", "basic_magic", "smelting", "settler_drill",
    };

    /// <summary>What the player has, counted once each time Hard looks at its economy.</summary>
    private struct Census
    {
        public int Workers, Halls, HallSites, Houses, HouseSites, Farms, FarmSites, FarmSlots, Barracks, BarracksSites,
            IdleBarracks, Storehouses, StoreSites, Housing, Population, Queued, QueuedWorkers, FreeFarmSlots, ActiveFarmSlots;
    }

    // What its workers do when they have nothing to do (the player's default priority list), as last set; the
    // workers kept on stone; and when it next looks at who works at what.
    private ulong _defaultList;
    private readonly List<int> _masons = new();
    private long _assignAt;

    // Its workers' lists stop at food and wood: one with neither in reach stands idle, and is sent to trees
    // further off (see Assign) rather than wandering off to dig whatever is near.
    private static readonly ulong FoodFirst = Automation.WorkPriorities.Pack(Automation.WorkKind.Build, Automation.WorkKind.Farm,
        Automation.WorkKind.Food, Automation.WorkKind.Wood);
    private static readonly ulong WoodFirst = Automation.WorkPriorities.Pack(Automation.WorkKind.Build, Automation.WorkKind.Wood,
        Automation.WorkKind.Farm, Automation.WorkKind.Food);
    // Its quarry (the pit's corners and floor), if it has marked one out, and every pit it has dug (nothing
    // is built in them: a building's footing would level the stair the diggers climb out by).
    private (int X1, int Z1, int X2, int Z2, int Floor)? _quarry;
    private readonly List<(int X1, int Z1, int X2, int Z2)> _pits = new();

    /// <summary>The workers it keeps at its quarry, by id.</summary>
    public IReadOnlyList<int> Masons => _masons;

    /// <summary>The quarry it has marked out (the pit's corners and the level of its floor), if any.</summary>
    public (int X1, int Z1, int X2, int Z2, int Floor)? QuarryArea => _quarry;

    /// <summary>
    /// Hard keeps its army worth at least this share (percent) of what it takes the enemy's to be worth
    /// (what it has seen of it lately, and never less than what a player starts with): below that, soldiers
    /// come before more workers and everything else but houses; above it they get what the rest leaves over.
    /// </summary>
    public const int HardKeepUpPercent = 120;

    /// <summary>
    /// Hard: with fewer workers than this and one more for every minute played, workers come before soldiers
    /// even while its army is short, unless enemies are at its buildings: a town that small cannot keep an
    /// army up whatever it spends on one, and a raid that kills its workers would leave it that small for good.
    /// </summary>
    public const int HardLeastWorkers = 12;

    /// <summary>True if its army is worth less than <see cref="HardKeepUpPercent"/> of what it takes the enemy's to be.</summary>
    private bool ArmyShort(Simulation sim)
    {
        return PowerOf(sim, Able(sim)) * 100 < Reckon(sim) * HardKeepUpPercent;
    }

    /// <summary>The soldier a barracks of its can train now that costs the least (food and wood, stone double), or null.</summary>
    private UnitDef? CheapSoldier(Simulation sim)
    {
        var barracks = FirstBuilding(sim, BuildingKind.Barracks);
        if (barracks == null) return null;
        UnitDef? best = null;
        int bestCost = int.MaxValue;
        foreach (string id in barracks.Trains)
        {
            if (!sim.Content.TryGetUnit(id, out var d) || !sim.Economy.Unlocked(Player, d) || RoleWeight(d.Role) == 0 || d.Role == UnitRole.Sapper) continue;
            int cost = 0;
            foreach (var g in d.Cost) cost += g.Good == Good.Stone ? 2 * g.Amount : g.Amount;
            if (cost < bestCost) (bestCost, best) = (cost, d);
        }
        return best;
    }

    private Census Count(Simulation sim)
    {
        var c = new Census();
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && sim.Content.Units[u.Def[i]].Role == UnitRole.Worker) c.Workers++;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Planned) continue;
            bool site = !b.Active;
            foreach (int q in b.Queue)
            {
                c.Queued++;
                if (sim.Content.Units[q].Role == UnitRole.Worker) c.QueuedWorkers++;
            }
            switch (b.Def.Kind)
            {
                case BuildingKind.TownHall:
                    if (site) c.HallSites++; else c.Halls++;
                    break;
                case BuildingKind.House:
                    if (site) c.HouseSites++; else c.Houses++;
                    break;
                case BuildingKind.Farm:
                    if (site) c.FarmSites++;
                    else
                    {
                        c.Farms++;
                        c.ActiveFarmSlots += b.Def.WorkerSlots;
                    }
                    c.FarmSlots += b.Def.WorkerSlots;
                    break;
                case BuildingKind.Barracks:
                    if (site) c.BarracksSites++;
                    else
                    {
                        c.Barracks++;
                        if (b.Queue.Count == 0) c.IdleBarracks++;
                    }
                    break;
                case BuildingKind.Storehouse:
                    if (site) c.StoreSites++; else c.Storehouses++;
                    break;
            }
        }
        // Farm places nobody works: every active farm's places, less the farmers.
        int farmers = 0;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && u.Job[i] == UnitJob.Farm) farmers++;
        c.FreeFarmSlots = Math.Max(0, c.ActiveFarmSlots - farmers);
        c.Housing = sim.Economy.Housing(Player);
        c.Population = sim.Economy.Population(Player);
        return c;
    }

    /// <summary>
    /// Hard's economy. Each look it goes down what it wants in order of need, and pays for what it can; what
    /// it cannot pay for yet it saves up for (<see cref="_keep"/>), so that nothing further down spends it.
    /// In order: houses before its people run out of room; soldiers, while its army is short (see
    /// <see cref="ArmyShort"/>); workers, all the time, at every town hall; a first barracks; a storehouse
    /// where work is far from storage; research; farms for its share of farmers (<see cref="FarmShare"/>);
    /// more barracks while goods pile up; its wall, for a faction it plays balanced (<see cref="HardWall"/>);
    /// a second town hall (<see cref="HardExpand"/>); and soldiers with what is left. While enemies are at
    /// its buildings it saves up only for houses and soldiers.
    /// </summary>
    private void HardEconomy(Simulation sim, Building hall, Action<Command> issue)
    {
        var econ = sim.Economy;
        Array.Clear(_keep);
        Feed(sim, issue);
        AutomateNew(sim, issue);
        var c = Count(sim);
        int minutes = (int)(sim.Tick / (60 * Tps));
        bool calm = sim.Tick - _alarm > AlarmTicks;
        // The line of its wall, if it is to have one, and the ground for its quarry are picked before the
        // town grows over them, and kept clear.
        PlanWall(sim, hall);
        if (_quarry == null && sim.Tick % QuarryLookTicks < EconomyTicks) MarkQuarry(sim, hall, settle: sim.Tick >= QuarrySettleTicks);

        // Houses: room for what is being trained and a little more, more of it the more buildings train.
        var house = FirstBuilding(sim, BuildingKind.House);
        if (house != null)
        {
            int perHouse = Math.Max(1, house.Housing);
            int room = c.Housing + c.HouseSites * perHouse - c.Population - c.Queued;
            int margin = 3 + 2 * (c.Halls + c.Barracks);
            if (room < margin && c.HouseSites < HardSitesAtOnce) Want(sim, house, hall, HardTownRadius, true, issue);
        }

        // Soldiers before more workers (and all that follows) while its army is worth less than it takes
        // the enemy's to be; otherwise they get what is left over, at the end.
        // Not while its town is too small to keep an army, unless the enemy is at its buildings: then workers
        // come first, as they would have before the raid that cost it them.
        var barracks = FirstBuilding(sim, BuildingKind.Barracks);
        bool thin = calm && c.Workers + c.QueuedWorkers < Math.Min(HardMostWorkers, HardLeastWorkers + minutes);
        bool armyFirst = c.Barracks >= 1 && ArmyShort(sim) && !thin;
        if (armyFirst && !SmartTrain(sim, issue, anyAffordable: true) && c.Housing > c.Population + c.Queued
            && c.IdleBarracks > 0 && CheapSoldier(sim) is { } soldier)
            Save(soldier.Cost);

        // Workers, at every town hall, one in each queue.
        int wanted = Math.Min(HardMostWorkers, HardFirstWorkers + HardWorkersPerMinute * minutes);
        int coming = c.Workers + c.QueuedWorkers;
        foreach (var b in sim.Buildings.All)
        {
            if (coming >= wanted) break;
            if (b.Owner != Player || !b.Active || b.Def.Kind != BuildingKind.TownHall || b.Queue.Count > 0) continue;
            if (FirstTrained(sim, b, UnitRole.Worker) is not { } worker) continue;
            if (Affordable(sim, worker.Cost))
            {
                issue(new TrainCommand(Player, b.Id, worker.Id));
                Spend(worker.Cost);
                coming++;
            }
            else Save(worker.Cost);
        }

        // The first barracks, as soon as its first fields are sown.
        if (barracks != null && c.Barracks + c.BarracksSites == 0 && c.Workers >= HardBarracksWorkers && c.FarmSlots >= 4)
            Want(sim, barracks, hall, Automation.AutomationSystem.PlanRadius, true, issue);

        // A storehouse where workers gather far from storage: shorter trips are worth more than anything
        // else wood buys.
        var store = FirstBuilding(sim, BuildingKind.Storehouse);
        if (store != null && c.StoreSites == 0 && c.Storehouses < HardMostStorehouses && FarWork(sim) is var (fx, fz))
        {
            if (!Affordable(sim, store.Cost))
            {
                if (calm) Save(store.Cost);
            }
            else if (Room(sim, store, (fx, fz), StoreRadius, out int sx, out int sz))
            {
                issue(new BuildCommand(Player, Array.Empty<int>(), store.Id, sx, sz));
                Spend(store.Cost);
            }
        }

        // Research, one tech at a time, in order. The first two (fields, and better tools) come before
        // anything below; for the rest it saves up once it has a barracks.
        if (econ.Researching(Player) == null)
        {
            int k = 0;
            foreach (string id in HardTechOrder)
            {
                k++;
                if (!sim.Content.TryGetTech(id, out var tech)) continue;
                var r = econ.CanResearch(Player, tech, checkCost: false);
                if (r is ResearchResult.Researched or ResearchResult.OtherFaction or ResearchResult.NeedsTechs) continue;
                if (r != ResearchResult.Ok) break;
                if (Affordable(sim, tech.Cost))
                {
                    issue(new ResearchCommand(Player, id));
                    Spend(tech.Cost);
                    Note(sim, $"researches {tech.Name}");
                }
                else if (calm && (k <= 2 || c.Barracks >= 1)) SaveUp(sim, tech.Cost);
                break;
            }
        }

        // Farms: places for its share of farmers and a couple more, two sites at a time.
        var farm = FirstBuilding(sim, BuildingKind.Farm);
        if (farm != null && c.FarmSites < HardSitesAtOnce
            && c.FarmSlots < Math.Max(4, (c.Workers + c.QueuedWorkers) * Math.Max(HardFarmPercent, FarmShare(sim)) / 100 + 2))
            Want(sim, farm, hall, HardTownRadius, calm, issue);

        // More barracks while every one is busy, its people have room and goods are left over.
        if (barracks != null && c.Barracks >= 1 && c.BarracksSites == 0 && c.IdleBarracks == 0 && c.Barracks < HardMostBarracks
            && c.Housing > c.Population + c.Queued && Spare(sim, barracks.Cost, HardSpareFood, HardSpareWood))
            Want(sim, barracks, hall, HardTownRadius, false, issue);

        // Its wall (a faction it plays balanced), before it settles new ground.
        if (calm) HardWall(sim, hall, c, issue);
        if (calm) HardExpand(sim, hall, c, issue);
        if (!armyFirst) SmartTrain(sim, issue, anyAffordable: false);
        if (sim.Tick >= _assignAt)
        {
            _assignAt = sim.Tick + AssignTicks;
            Assign(sim, hall, c, issue);
        }
    }

    /// <summary>Hard: work this far (columns) from the nearest storage is far, and a storehouse goes up for this many workers at it.</summary>
    public const int HardFarWork = 14, HardFarWorkers = 3;

    /// <summary>
    /// Where a storehouse would save the most walking: the block one of its workers is gathering,
    /// <see cref="HardFarWork"/> columns or more from the nearest storage (finished or not), that has the
    /// most others gathering as far from storage within eight columns of it; null if fewer than
    /// <see cref="HardFarWorkers"/> work there.
    /// </summary>
    private (int X, int Z)? FarWork(Simulation sim)
    {
        var u = sim.Units;
        var stores = new List<Building>();
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && !b.Planned && b.Def.Storage) stores.Add(b);
        var far = new List<(int X, int Z)>();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || u.Job[i] != UnitJob.Gather) continue;
            int x = u.JobX[i], z = u.JobZ[i];
            bool near = false;
            foreach (var b in stores) near |= b.ColumnDistance(x, z) < HardFarWork;
            if (!near) far.Add((x, z));
        }
        (int X, int Z)? best = null;
        int bestN = HardFarWorkers - 1;
        foreach (var (x, z) in far)
        {
            int n = 0;
            foreach (var (ox, oz) in far)
                if (Dist2(x, z, ox, oz) <= 64) n++;
            if (n > bestN) (bestN, best) = (n, (x, z));
        }
        return best;
    }

    /// <summary>
    /// The share (percent) of its workers, masons aside, it wants on farms now: <see cref="HardFarmPercent"/>,
    /// more the more wood than food it has to spend (over what it keeps in hand), less the other way.
    /// </summary>
    private int FarmShare(Simulation sim)
    {
        var stock = sim.Economy.Stockpile(Player);
        int food = stock[(int)Good.Food] - _keep[(int)Good.Food], wood = stock[(int)Good.Wood] - _keep[(int)Good.Wood];
        return Math.Clamp(HardFarmPercent + (wood - food) / SharePer, HardLeastFarmPercent, HardMostFarmPercent);
    }

    /// <summary>
    /// Hard lays out every building itself, so it does not make all its workers automatic for good (that
    /// also has automation put up houses and farms where it sees fit, in a quarry pit say): it makes each
    /// new worker automatic as it comes out.
    /// </summary>
    private void AutomateNew(Simulation sim, Action<Command> issue)
    {
        var u = sim.Units;
        _scratch.Clear();
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && !u.Auto[i] && sim.Content.Units[u.Def[i]].Role == UnitRole.Worker) _scratch.Add(u.Id[i]);
        if (_scratch.Count > 0) issue(new AutomateCommand(Player, _scratch.ToArray(), true));
    }

    /// <summary>
    /// Who works at what. It wants a share of its workers on the farms (see <see cref="FarmShare"/>) and the
    /// rest cutting wood; its workers are automatic and go down the player's priority list when they have
    /// nothing to do, so it sets that list to farms first while it has fewer farmers than it wants, and to
    /// wood first otherwise, and moves a couple of workers over at a time when the split is off (farmers to
    /// the trees, wood cutters to farms with room). While it is saving up stone it keeps a few workers on
    /// stone, and lets them go when there is plenty. Workers with nothing to do go to the nearest trees it
    /// knows of. A tree is one worker's work, so a tree with few others round it leaves some of them
    /// standing: when any it sent are idle again the next time it looks, those trees are taken, and it sends
    /// the idle on to the next nearest (the cutters there stay).
    /// </summary>
    private void Assign(Simulation sim, Building hall, Census c, Action<Command> issue)
    {
        var u = sim.Units;
        var stock = sim.Economy.Stockpile(Player);
        int stone = stock[(int)Good.Stone];

        _masons.RemoveAll(id => !u.Exists(id));
        var farmers = new List<int>();
        var cutters = new List<int>();
        var idle = new List<int>();
        int farming = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || sim.Content.Units[u.Def[i]].Role != UnitRole.Worker || _masons.Contains(u.Id[i])) continue;
            if (u.Job[i] == UnitJob.Farm) farming++;
            if (u.Job[i] == UnitJob.None && u.Order[i] == UnitOrder.Idle && !u.Held[i]) idle.Add(u.Id[i]);
            else if (u.CarryAmount[i] > 0) continue; // it finishes its trip first
            else if (u.Job[i] == UnitJob.Farm) farmers.Add(u.Id[i]);
            else if (u.Job[i] == UnitJob.Gather && u.JobBlock[i] == BlockType.Wood) cutters.Add(u.Id[i]);
        }
        // The trees it last sent the idle to had no room for some of them: not there again.
        bool left = false;
        foreach (int id in _woodSent) left |= idle.Contains(id);
        _woodSent.Clear();
        if (left && _forest is var (bx, _, bz))
        {
            if (_badForests.Count >= 16) _badForests.RemoveAt(0);
            _badForests.Add((bx, bz));
            _forest = null;
        }

        int want = Math.Min(c.ActiveFarmSlots, Math.Max(2, (c.Workers - _masons.Count) * FarmShare(sim) / 100));
        ulong list = farming < want ? FoodFirst : WoodFirst;
        if (list != _defaultList)
        {
            _defaultList = list;
            issue(new DefaultPriorityCommand(Player, list));
        }

        // Stone: a few workers at the quarry while stone is short, one when there is plenty.
        int over = stone - _keep[(int)Good.Stone];
        int masons = over < StoneWanted ? Math.Min(MostMasons, 2 + c.Workers / MasonsPer) : over >= StonePlenty ? 1 : Math.Max(1, _masons.Count);
        if (c.Workers < QuarryWorkers && over >= 0) masons = 0; // food and wood first
        // Its wall is not worth more diggers than that: it goes up as the stone comes in.
        if (_ringStone > 0 && _keep[(int)Good.Stone] <= _ringKept) masons = Math.Min(masons, KeepMasons);
        if (masons > _masons.Count)
        {
            var from = idle.Count > 0 ? idle : farming > want ? farmers : cutters;
            int n = Math.Min(Math.Min(masons - _masons.Count, AssignedAtOnce), from.Count);
            if (n > 0)
            {
                _masons.AddRange(from.GetRange(0, n));
                from.RemoveRange(0, n);
            }
        }
        else if (masons < _masons.Count)
        {
            var ids = _masons.GetRange(masons, _masons.Count - masons).ToArray();
            _masons.RemoveRange(masons, _masons.Count - masons);
            Free(ids, issue);
        }
        if (_masons.Count > 0) Quarry(sim, hall, issue);

        // Farms against wood: a couple of workers over at a time when the split is off by more than one.
        if (farming > want + 1)
            idle.AddRange(farmers.GetRange(0, Math.Min(Math.Min(AssignedAtOnce, farming - want), farmers.Count)));
        else if (farming < want - 1 && c.FreeFarmSlots > 0 && cutters.Count > 0)
            Free(cutters.GetRange(0, Math.Min(Math.Min(AssignedAtOnce, c.FreeFarmSlots), Math.Min(want - farming, cutters.Count))).ToArray(), issue);

        // Workers with nothing to do (and farmers moved to wood): to the nearest trees it knows of.
        if (idle.Count > 0 && Forest(sim, hall, out int x, out int y, out int z))
        {
            issue(new GatherCommand(Player, idle.ToArray(), x, y, z));
            _woodSent.AddRange(idle);
        }
    }

    /// <summary>
    /// Sends the masons that are not digging to the quarry. Stone lies under three levels of soil almost
    /// everywhere, so the quarry is a pit dug with a dig area near the town hall (see
    /// <see cref="QuarrySite"/>); when one is dug out it marks out another.
    /// </summary>
    private void Quarry(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        _scratch.Clear();
        foreach (int id in _masons)
        {
            int i = u.IndexOf(id);
            if (u.Job[i] != UnitJob.Dig && u.CarryAmount[i] == 0) _scratch.Add(id);
        }
        if (_scratch.Count == 0) return;
        if (_quarry is var (x1, z1, x2, z2, floor) && DugOut(sim.World, x1, z1, x2, z2, floor)) _quarry = null;
        if (_quarry == null) MarkQuarry(sim, hall, settle: true);
        if (_quarry is var (nx1, nz1, nx2, nz2, nfloor))
            issue(new DigAreaCommand(Player, _scratch.ToArray(), nx1, nz1, nx2, nz2, nfloor));
    }

    /// <summary>Picks the ground for its (next) quarry, if there is some (see <see cref="QuarrySite"/>): nothing is built there from now on.</summary>
    private void MarkQuarry(Simulation sim, Building hall, bool settle)
    {
        _quarry = QuarrySite(sim, hall, settle);
        if (_quarry is not var (x1, z1, x2, z2, _)) return;
        _pits.Add((x1, z1, x2, z2));
        Note(sim, $"marks out a quarry at {x1},{z1} to {x2},{z2}");
    }

    /// <summary>True if nothing is left to dig in the pit: every column, off the stair row and column, is down to the floor.</summary>
    private static bool DugOut(VoxelWorld w, int x1, int z1, int x2, int z2, int floor)
    {
        for (int z = z1 + 1; z <= z2; z++)
        for (int x = x1 + 1; x <= x2; x++)
            if (w.Get(x, floor, z) != BlockType.Air) return false;
        return true;
    }

    /// <summary>
    /// Where to dig a quarry: the nearest patch to the town hall, <see cref="QuarryNear"/> to
    /// <see cref="QuarryFar"/> columns from its middle and not in front of its door, of
    /// <see cref="QuarryLong"/> by <see cref="QuarryWide"/> columns (either way round) of open, dry, nearly
    /// level ground it has seen, with no building, tree, bush or water on it or within two columns of it.
    /// Where the ground round its town is too rough or too wooded for that it settles for less: further
    /// off and less level, then a smaller, shallower pit (see <see cref="QuarrySecond"/>). Null if there
    /// is none. Without <paramref name="settle"/>, only ground for a full quarry will do.
    /// </summary>
    private (int X1, int Z1, int X2, int Z2, int Floor)? QuarrySite(Simulation sim, Building hall, bool settle)
    {
        var (hx, hz) = Middle(hall);
        var tries = new List<(int, int, int, int, int, int)> { (QuarryLong, QuarryWide, QuarryDepth, QuarryNear, QuarryFar, 1) };
        if (settle)
        {
            tries.Add((QuarryLong, QuarryWide, QuarryDepth, QuarryNear, QuarrySecond.Far, QuarrySecond.Rough));
            tries.Add((QuarrySecond.Long, QuarrySecond.Wide, QuarrySecond.Depth, QuarrySecond.Near, QuarrySecond.Far, QuarrySecond.Rough));
        }
        foreach (var (lng, wide, depth, near, far, rough) in tries)
        {
            (int, int, int, int, int)? best = null;
            long bestD = long.MaxValue;
            for (int turn = 0; turn < 2; turn++)
            {
                int sx = turn == 0 ? lng : wide, sz = turn == 0 ? wide : lng;
                for (int cz = hz - far; cz <= hz + far; cz += 2)
                for (int cx = hx - far; cx <= hx + far; cx += 2)
                {
                    long d = Dist2(cx, cz, hx, hz);
                    if (d >= bestD || d < (long)near * near || d > (long)far * far) continue;
                    if (cz > hall.MaxZ && Math.Abs(cx - hx) <= 8) continue; // the way out of the door
                    int x1 = cx - sx / 2, z1 = cz - sz / 2, x2 = x1 + sx - 1, z2 = z1 + sz - 1;
                    if (QuarryFloor(sim, x1, z1, x2, z2, depth, rough) is not { } floor) continue;
                    bestD = d;
                    best = (x1, z1, x2, z2, floor);
                }
            }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>
    /// Hard: the quarry it settles for where there is no ground for a full one: out to this far from the
    /// town hall, on ground this uneven (levels), and failing that a pit this long, wide and deep, from
    /// this near.
    /// </summary>
    public static class QuarrySecond
    {
        public const int Far = 48, Rough = 2, Long = 10, Wide = 5, Depth = 8, Near = 11;
    }

    /// <summary>
    /// The floor of a quarry on the patch (<paramref name="depth"/> below its highest ground), or null if
    /// the patch will not do: not all seen, built on, not open soil, or more uneven than <paramref name="rough"/> levels.
    /// </summary>
    private int? QuarryFloor(Simulation sim, int x1, int z1, int x2, int z2, int depth, int rough)
    {
        var w = sim.World;
        if (x1 < 3 || z1 < 3 || x2 >= w.SizeX - 3 || z2 >= w.SizeZ - 3) return null;
        if (!ClearOfWall(x1, z1, x2, z2, 3)) return null; // off the line of its wall, and out of the way through a gate
        int low = int.MaxValue, high = int.MinValue;
        for (int z = z1 - 2; z <= z2 + 2; z++)
        for (int x = x1 - 2; x <= x2 + 2; x++)
        {
            if (sim.Vision.At(Player, x, z) == Sight.Unseen || sim.Buildings.AtColumn(x, z) != null) return null;
            int top = w.SurfaceHeight(x, z);
            if (top < 1 || w.Get(x, top, z) is not (BlockType.Grass or BlockType.Soil or BlockType.Sand)) return null;
            if (w.Get(x, top + 1, z) != BlockType.Air) return null; // a bush, a field, a road
            if (World.HazardSystem.IsHazard(w, x, top + 1, z)) return null; // the lava's heat
            low = Math.Min(low, top);
            high = Math.Max(high, top);
        }
        if (high - low > rough) return null;
        int floor = high + 1 - depth;
        return floor >= 2 ? floor : null;
    }

    /// <summary>Has the workers drop what they are doing and look for work again, by their priority lists.</summary>
    private void Free(int[] ids, Action<Command> issue)
    {
        issue(new StopCommand(Player, ids));
        issue(new AutomateCommand(Player, ids, true));
    }

    /// <summary>
    /// Lays out a site for the building near the town hall if it can pay for it; otherwise, with
    /// <paramref name="save"/>, saves up for it.
    /// </summary>
    private void Want(Simulation sim, BuildingDef def, Building hall, int radius, bool save, Action<Command> issue)
    {
        if (!Affordable(sim, def.Cost))
        {
            if (save) SaveUp(sim, def.Cost);
            return;
        }
        if (Room(sim, def, Middle(hall), radius, out int x, out int z))
        {
            issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, x, z));
            Spend(def.Cost);
            if (def.Kind is BuildingKind.Barracks) Note(sim, $"lays out a barracks at {x},{z}");
        }
    }

    /// <summary>
    /// Room for the building near the point (see <see cref="Automation.AutomationSystem.FindRoom"/>), clear of
    /// its quarry pits, of the line of its wall (see <see cref="ClearOfWall(BuildingDef, int, int)"/>), of hazard
    /// ground (see <see cref="SafeGround"/>) and of where its soldiers gather (see <see cref="RallyClear"/>).
    /// </summary>
    private bool Room(Simulation sim, BuildingDef def, (int X, int Z) at, int radius, out int x, out int z)
    {
        var rally = GatheringGround(sim);
        return sim.Automation.FindRoom(Player, def, at.X, at.Z, radius, out x, out z, (bx, bz) => ClearOfPits(def, bx, bz)
            && ClearOfWall(def, bx, bz)
            && SafeGround(sim, def, bx, bz)
            && (rally is not var (rx, rz) || bx > rx + RallyClear || bx + def.SizeX - 1 < rx - RallyClear
                || bz > rz + RallyClear || bz + def.SizeZ - 1 < rz - RallyClear));
    }

    /// <summary>True if a building with its corner at (x, z) keeps two columns clear of every quarry pit.</summary>
    private bool ClearOfPits(BuildingDef def, int x, int z)
    {
        foreach (var (x1, z1, x2, z2) in _pits)
            if (x <= x2 + 2 && x + def.SizeX - 1 >= x1 - 2 && z <= z2 + 2 && z + def.SizeZ - 1 >= z1 - 2) return false;
        return true;
    }

    /// <summary>Keeps the cost in hand: nothing wanted less may spend it.</summary>
    private void Save(GoodAmount[] cost)
    {
        foreach (var g in cost) _keep[(int)g.Good] += g.Amount;
    }

    /// <summary>
    /// Saves up for the cost (see <see cref="Save"/>); but while it has not the stone, it keeps only the
    /// stone in hand. Stone comes in slowly (a few workers dig it, and a wall takes a lot of it), and the
    /// food and wood kept idle until it came would be soldiers not trained; they are soon gathered again
    /// once the stone is there.
    /// </summary>
    private void SaveUp(Simulation sim, GoodAmount[] cost)
    {
        int stone = Price(cost, Good.Stone);
        if (stone > 0 && sim.Economy.Stockpile(Player)[(int)Good.Stone] < stone + _keep[(int)Good.Stone]) _keep[(int)Good.Stone] += stone;
        else Save(cost);
    }

    /// <summary>
    /// Counts the cost of something ordered this look as kept, so that what is ordered after it in the same
    /// look does not count on the same goods (the order is only paid for when the tick runs).
    /// </summary>
    private void Spend(GoodAmount[] cost) => Save(cost);

    /// <summary>True if it can pay the cost and still have this much food and wood on top of what it keeps in hand.</summary>
    private bool Spare(Simulation sim, GoodAmount[] cost, int food, int wood)
    {
        var stock = sim.Economy.Stockpile(Player);
        foreach (var g in cost)
            if (stock[(int)g.Good] < g.Amount + _keep[(int)g.Good]) return false;
        return stock[(int)Good.Food] >= food + _keep[(int)Good.Food] + Price(cost, Good.Food)
            && stock[(int)Good.Wood] >= wood + _keep[(int)Good.Wood] + Price(cost, Good.Wood);
    }

    private static int Price(GoodAmount[] cost, Good good)
    {
        foreach (var g in cost)
            if (g.Good == good) return g.Amount;
        return 0;
    }

    /// <summary>
    /// A second town hall once its town is grown (<see cref="HardExpandWorkers"/> workers,
    /// <see cref="HardExpandBarracks"/> barracks): the match is lost with the last town hall, so one is not
    /// enough, and a second trains workers and takes goods in too. It goes <see cref="HardExpandDistance"/>
    /// columns from the first, on the side away from where the enemy is (as far as it knows or guesses),
    /// on ground it has seen. It saves up for it.
    /// </summary>
    private void HardExpand(Simulation sim, Building hall, Census c, Action<Command> issue)
    {
        if (c.Halls + c.HallSites >= 2 || c.Workers < HardExpandWorkers || c.Barracks < HardExpandBarracks) return;
        var def = FirstBuilding(sim, BuildingKind.TownHall);
        if (def == null) return;
        if (!Affordable(sim, def.Cost))
        {
            SaveUp(sim, def.Cost);
            return;
        }
        var (hx, hz) = Middle(hall);
        var guesses = HallGuesses(sim, hx, hz);
        var enemy = KnownHall(hx, hz) is { } k ? (k.X, k.Z) : guesses.Count > 0 ? guesses[0] : (sim.World.SizeX / 2, sim.World.SizeZ / 2);
        // Eight ways out from the town hall, the one furthest from the enemy first.
        var ways = new List<(long D, int X, int Z)>();
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0) continue;
            int step = dx != 0 && dz != 0 ? HardExpandDistance * 7 / 10 : HardExpandDistance;
            int x = hx + dx * step, z = hz + dz * step;
            if (x < 12 || z < 12 || x >= sim.World.SizeX - 12 || z >= sim.World.SizeZ - 12) continue;
            if (sim.Vision.At(Player, x, z) == Sight.Unseen) continue;
            ways.Add((-Dist2(x, z, enemy.Item1, enemy.Item2), x, z));
        }
        ways.Sort();
        foreach (var (_, x, z) in ways)
        {
            if (!Room(sim, def, (x, z), 10, out int bx, out int bz)) continue;
            issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, bx, bz));
            Spend(def.Cost);
            Note(sim, $"lays out a second town hall at {bx},{bz}");
            return;
        }
    }
}
