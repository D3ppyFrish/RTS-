using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.Replay;

/// <summary>
/// Generates a match for headless testing: scripted players that react to the world as it is,
/// digging and building near their home area, marching their soldiers around the map and into fights
/// (attack-moves, attacks on units and buildings, sapper tunnels), and running an economy from their town
/// hall (gathering, building, farming, smelting, research, training, roads and raids), switching automation
/// on and off now and then (automatic workers, repeating training, stances, forms to evolve into),
/// evolving units that have the experience, and riding or recruiting the monsters they beat. The match has
/// a monster lair for each start, and the players' soldiers march on them now and then. It has the
/// city-states' towns too: the players send their caravans to trade there, give them gifts, and hire
/// mercenaries where they are the ally. The output is a plain command log, so the match can then be
/// replayed anywhere.
/// </summary>
public static class ScriptedMatch
{
    /// <param name="actionsPerSecond">Dig and build commands each player issues per second of game time.</param>
    /// <param name="startUnits">Units each player starts with; they get a new order about once a second.</param>
    public static CommandLog Generate(ulong seed, int sizeX, int sizeY, int sizeZ, long ticks,
        int players = 2, int actionsPerSecond = 10, int startUnits = 0)
    {
        if (players <= 0 || players >= Simulation.WildPlayer) throw new ArgumentOutOfRangeException(nameof(players));
        if (startUnits < 0) throw new ArgumentOutOfRangeException(nameof(startUnits));
        var log = new CommandLog
        {
            Seed = seed, SizeX = sizeX, SizeY = sizeY, SizeZ = sizeZ, Ticks = ticks, StartUnits = startUnits,
            StartBuildings = true, StartLairs = true, StartCities = true, ContentHash = ContentDb.Default.Hash,
            TreeScale = World.RegionGenerator.DefaultTreeScale,
        };
        var sim = log.CreateSimulation();
        var rng = new DeterministicRandom(seed ^ 0x5C819EDUL);
        var economyRng = new DeterministicRandom(seed ^ 0xEC0A0B1EUL);

        // Each player works around a home point spread along the map diagonal.
        var homes = new (int X, int Z)[players];
        for (int p = 0; p < players; p++)
            homes[p] = (sizeX * (p + 1) / (players + 1), sizeZ * (p + 1) / (players + 1));
        int radius = Math.Max(4, Math.Min(sizeX, sizeZ) / 8);

        // Starting soldiers by owner, in id order. Workers are left to their jobs, raiders to their raids, and
        // wild monsters to their lairs.
        var army = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < sim.Units.Count; i++)
        {
            if (!IsSoldier(sim, i) || sim.Units.Owner[i] == Simulation.WildPlayer || sim.Units.Town[i] != 0) continue;
            if (!army.TryGetValue(sim.Units.Owner[i], out var list)) army[sim.Units.Owner[i]] = list = new List<int>();
            list.Add(sim.Units.Id[i]);
        }
        foreach (var list in army.Values)
        {
            list.Sort();
            list.RemoveRange(list.Count - Raiders(list.Count), Raiders(list.Count));
        }

        int carry = 0;
        while (sim.Tick < ticks)
        {
            carry += actionsPerSecond;
            int perPlayer = carry / Simulation.TicksPerSecond;
            carry %= Simulation.TicksPerSecond;
            for (int p = 0; p < players; p++)
            for (int i = 0; i < perPlayer; i++)
            {
                int x = Math.Clamp(homes[p].X + rng.Range(-radius, radius + 1), 0, sizeX - 1);
                int z = Math.Clamp(homes[p].Z + rng.Range(-radius, radius + 1), 0, sizeZ - 1);
                int y = sim.World.SurfaceHeight(x, z);
                Command cmd;
                // Mostly dig (mining), sometimes build walls of stone or wood on top.
                if (y < 0 || rng.Range(0, 100) < 30)
                {
                    if (y + 1 >= sizeY) continue;
                    var block = rng.Range(0, 2) == 0 ? BlockType.Stone : BlockType.Wood;
                    cmd = new PlaceCommand(p, x, y + 1, z, block);
                }
                else
                {
                    cmd = new DigCommand(p, x, y, z);
                }
                Issue(sim, log, cmd);
            }

            // Once a second each player gives a slice of its army a new order.
            if (sim.Tick % Simulation.TicksPerSecond == 0)
            {
                foreach (var (owner, ids) in army)
                    OrderUnits(sim, log, rng, owner, ids, homes[owner % players]);
            }
            // Every two seconds each player makes one economic move.
            if (sim.Tick % EconomyInterval == 0)
            {
                for (int p = 0; p < players; p++)
                    RunEconomy(sim, log, economyRng, p, homes[p]);
            }
            sim.Step();
        }
        return log;
    }

    /// <summary>
    /// Sends a random run of the player's units somewhere: near their start, into the dig site at their
    /// home (holes and walls to climb), to the map centre, to the monster lair nearest their start, or
    /// toward another start, fighting on the way to the last three. Sometimes stops them, sets them on the
    /// nearest enemy or an enemy building, or has their sappers tunnel two levels down toward their home.
    /// </summary>
    private static void OrderUnits(Simulation sim, CommandLog log, DeterministicRandom rng, int owner, List<int> ids,
        (int X, int Z) home)
    {
        if (ids.Count == 0) return;
        int first = rng.Range(0, ids.Count);
        int count = Math.Min(ids.Count - first, rng.Range(1, 41));
        var units = ids.GetRange(first, count).ToArray();
        int roll = rng.Range(0, 100);
        if (roll < 10)
        {
            Issue(sim, log, new StopCommand(owner, units));
            return;
        }
        if (roll < 16)
        {
            int enemy = NearestEnemy(sim, owner, units);
            if (enemy > 0) Issue(sim, log, new AttackCommand(owner, units, enemy));
            return;
        }
        if (roll < 20)
        {
            var targets = new List<Building>();
            foreach (var b in sim.Buildings.All)
                if (b.Owner != owner) targets.Add(b);
            if (targets.Count > 0) Issue(sim, log, new AttackBuildingCommand(owner, units, targets[rng.Range(0, targets.Count)].Id));
            return;
        }
        int spread = 16;
        if (roll < 24)
        {
            int tx = Math.Clamp(home.X + rng.Range(-spread, spread + 1), 0, sim.World.SizeX - 1);
            int tz = Math.Clamp(home.Z + rng.Range(-spread, spread + 1), 0, sim.World.SizeZ - 1);
            int ty = sim.World.SurfaceHeight(tx, tz) - 1; // two levels under where units stand
            if (ty > 0) Issue(sim, log, new TunnelCommand(owner, units, tx, ty, tz));
            return;
        }
        var starts = sim.Map.Starts;
        var start = starts.Count > 0 ? (starts[owner % starts.Count].X, starts[owner % starts.Count].Z) : home;
        (int X, int Z) target = roll switch
        {
            < 40 => start,
            < 70 => home,
            < 80 => (sim.World.SizeX / 2, sim.World.SizeZ / 2),
            < 85 => NearestLair(sim, start) ?? home,
            _ => starts.Count > 0 ? (starts[(owner + 1) % starts.Count].X, starts[(owner + 1) % starts.Count].Z) : home,
        };
        int x = Math.Clamp(target.X + rng.Range(-spread, spread + 1), 0, sim.World.SizeX - 1);
        int z = Math.Clamp(target.Z + rng.Range(-spread, spread + 1), 0, sim.World.SizeZ - 1);
        Issue(sim, log, roll < 70 ? new MoveCommand(owner, units, x, z) : new AttackMoveCommand(owner, units, x, z));
    }

    /// <summary>
    /// The type of the player's lowest-id unit that can evolve, or null if none can: all of its kind that can
    /// evolve then take one form (see <see cref="Evolve"/>).
    /// </summary>
    private static UnitDef? ReadyToEvolve(Simulation sim, int owner)
    {
        var u = sim.Units;
        UnitDef? from = null;
        int first = int.MaxValue;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != owner || u.Id[i] >= first || !sim.Evolution.CanEvolve(i)) continue;
            first = u.Id[i];
            from = sim.Content.Units[u.Def[i]];
        }
        return from;
    }

    /// <summary>The player's units of that type that can evolve take a random one of its forms.</summary>
    private static void Evolve(Simulation sim, CommandLog log, DeterministicRandom rng, int owner, UnitDef from)
    {
        var u = sim.Units;
        var ready = new List<int>();
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == owner && u.Def[i] == from.Index && sim.Evolution.CanEvolve(i)) ready.Add(u.Id[i]);
        ready.Sort();
        Issue(sim, log, new EvolveCommand(owner, ready.ToArray(), from.Forms[rng.Range(0, from.Forms.Count)].Id));
    }

    /// <summary>The den of the lair nearest to column (x, z) that still has its pack (ties to the lower id), or null.</summary>
    private static (int X, int Z)? NearestLair(Simulation sim, (int X, int Z) from)
    {
        (int X, int Z)? best = null;
        long bestD = long.MaxValue;
        foreach (var lair in sim.Monsters.Lairs)
        {
            if (lair.Cleared) continue;
            long d = (long)(lair.X - from.X) * (lair.X - from.X) + (long)(lair.Z - from.Z) * (lair.Z - from.Z);
            if (d >= bestD) continue;
            bestD = d;
            best = (lair.X, lair.Z);
        }
        return best;
    }

    /// <summary>The ids of the player's beaten monsters, in order.</summary>
    private static List<int> Beaten(Simulation sim, int owner)
    {
        var u = sim.Units;
        var ids = new List<int>();
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == owner && u.Subdued[i]) ids.Add(u.Id[i]);
        ids.Sort();
        return ids;
    }

    /// <summary>The id of the enemy unit nearest the first of the units that is still alive (ties to the lowest id), or 0.</summary>
    private static int NearestEnemy(Simulation sim, int owner, int[] units)
    {
        var u = sim.Units;
        int from = -1;
        foreach (int id in units)
            if ((from = u.IndexOf(id)) >= 0) break;
        if (from < 0) return 0;
        int best = 0;
        Fix64 bestD = Fix64.MaxValue;
        for (int j = 0; j < u.Count; j++)
        {
            if (u.Owner[j] == owner) continue;
            Fix64 dx = u.X[j] - u.X[from], dz = u.Z[j] - u.Z[from];
            Fix64 d = dx * dx + dz * dz;
            if (d < bestD || (d == bestD && u.Id[j] < best))
            {
                bestD = d;
                best = u.Id[j];
            }
        }
        return best;
    }

    private const int EconomyInterval = 2 * Simulation.TicksPerSecond;
    private static readonly BlockType[] Resources = { BlockType.Wood, BlockType.Stone, BlockType.IronOre, BlockType.BerryBush };
    private static readonly string[] Builds = { "house", "storehouse", "farm", "smelter", "barracks" };

    private static bool IsWorker(Simulation sim, int i) => sim.Content.Units[sim.Units.Def[i]].Role == UnitRole.Worker;

    private static bool IsSoldier(Simulation sim, int i) => sim.Content.Units[sim.Units.Def[i]].Role is not (UnitRole.Worker or UnitRole.Trader or UnitRole.Hauler);

    /// <summary>How many of a player's soldiers (the highest ids) are kept back for raids.</summary>
    private static int Raiders(int soldiers) => Math.Min(8, soldiers / 2);

    /// <summary>
    /// One economic move for the player: send a few workers to gather the nearest wood, stone, ore or
    /// berries around their town hall, put up a building nearby, work a farm or smelter, train a unit,
    /// research a tech, lay a road out from the hall, send soldiers to destroy a city-state's totem the player
    /// does not hold, send soldiers to capture or burn an enemy storehouse, or change a setting of automation. Half the time, units with the experience
    /// to evolve do that instead, and half the time the monsters the player's units beat are ridden or
    /// recruited (all of them, one way). Caravans that are not trading are sent to a town first, and half the
    /// time the holder of a city-state's town hires one of its mercenaries. Buildings, units and roads that need a tech the
    /// player lacks are skipped.
    /// </summary>
    private static void RunEconomy(Simulation sim, CommandLog log, DeterministicRandom rng, int owner, (int X, int Z) home)
    {
        var u = sim.Units;
        var workers = new List<int>();
        var soldiers = new List<int>();
        var idleCaravans = new List<int>();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != owner || u.Subdued[i] || u.Town[i] != 0) continue;
            if (IsWorker(sim, i)) workers.Add(u.Id[i]);
            else if (IsSoldier(sim, i)) soldiers.Add(u.Id[i]);
            else if (u.Job[i] != UnitJob.Trade && u.Rout[i] == 0) idleCaravans.Add(u.Id[i]);
        }
        workers.Sort();
        soldiers.Sort();
        idleCaravans.Sort();
        Building? hall = null;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == owner && b.Def.Kind == BuildingKind.TownHall) { hall = b; break; }
        int cx = hall != null ? hall.X + hall.Def.SizeX / 2 : home.X;
        int cz = hall != null ? hall.Z + hall.Def.SizeZ / 2 : home.Z;
        // Units with the experience to evolve do, half the time.
        if (ReadyToEvolve(sim, owner) is { } from && rng.Range(0, 2) == 0)
        {
            Evolve(sim, log, rng, owner, from);
            return;
        }
        // So are beaten monsters ridden or recruited.
        if (Beaten(sim, owner) is { Count: > 0 } beaten && rng.Range(0, 2) == 0)
        {
            Issue(sim, log, new TameCommand(owner, beaten.ToArray(), rng.Range(0, 2) == 0 ? TameChoice.Ride : TameChoice.Recruit));
            return;
        }
        var towns = sim.Cities.Towns;
        // Caravans with nothing to do go and trade.
        if (idleCaravans.Count > 0 && towns.Count > 0)
        {
            Issue(sim, log, new TradeCommand(owner, idleCaravans.ToArray(), towns[rng.Range(0, towns.Count)].Id));
            return;
        }
        // An ally with room for one more unit hires a mercenary it can pay for, half the time.
        foreach (var town in towns)
        {
            if (town.Holder != owner || town.Ready == 0 || !sim.Cities.CanHire(owner)) continue;
            var mercenaries = new List<UnitDef>();
            foreach (var m in town.Def.Mercenaries)
                if (sim.Economy.CanAfford(owner, m.Cost)) mercenaries.Add(m);
            if (mercenaries.Count == 0 || rng.Range(0, 2) > 0) continue;
            Issue(sim, log, new HireCommand(owner, town.Id, mercenaries[rng.Range(0, mercenaries.Count)].Id));
            return;
        }
        int roll = rng.Range(0, 100);
        if (roll >= 92)
        {
            SetAutomation(sim, log, rng, owner, workers, soldiers);
            return;
        }
        if (roll < 36)
        {
            var kind = Resources[rng.Range(0, Resources.Length)];
            if (workers.Count > 0 && FindNear(sim.World, kind, cx, hall?.Y ?? sim.World.SurfaceHeight(cx, cz) + 1, cz, out var r))
                Issue(sim, log, new GatherCommand(owner, Pick(rng, workers, 4), r.X, r.Y, r.Z));
        }
        else if (roll < 50)
        {
            var def = sim.Content.Building(Builds[rng.Range(0, Builds.Length)]);
            if (!sim.Economy.Unlocked(owner, def)) return;
            for (int t = 0; t < 8; t++)
            {
                int x = cx + rng.Range(-20, 21) - def.SizeX / 2, z = cz + rng.Range(-20, 21) - def.SizeZ / 2;
                if (sim.Economy.CanPlace(owner, def, x, z, out _) != PlaceResult.Ok) continue;
                Issue(sim, log, new BuildCommand(owner, Pick(rng, workers, 3), def.Id, x, z));
                break;
            }
        }
        else if (roll < 60)
        {
            var places = new List<Building>();
            foreach (var b in sim.Buildings.All)
                if (b.Owner == owner && (b.State == BuildingState.Site || b.Def.WorkerSlots > 0)) places.Add(b);
            if (places.Count > 0 && workers.Count > 0)
                Issue(sim, log, new WorkCommand(owner, Pick(rng, workers, 2), places[rng.Range(0, places.Count)].Id));
        }
        else if (roll < 70)
        {
            // Two times in three at a building that trains soldiers, if the player has one.
            var trainers = new List<Building>();
            var barracks = new List<Building>();
            foreach (var b in sim.Buildings.All)
            {
                if (b.Owner != owner || !b.Active || b.Def.Trains.Length == 0) continue;
                trainers.Add(b);
                if (Array.Exists(b.Def.Trains, id => sim.Content.Unit(id).Role is not (UnitRole.Worker or UnitRole.Trader or UnitRole.Hauler))) barracks.Add(b);
            }
            if (trainers.Count == 0) return;
            if (barracks.Count > 0 && rng.Range(0, 3) > 0) trainers = barracks;
            var at = trainers[rng.Range(0, trainers.Count)];
            var units = new List<string>();
            foreach (var id in at.Def.Trains)
                if (sim.Economy.Unlocked(owner, sim.Content.Unit(id))) units.Add(id);
            if (units.Count > 0) Issue(sim, log, new TrainCommand(owner, at.Id, units[rng.Range(0, units.Count)]));
        }
        else if (roll < 78)
        {
            var techs = new List<TechDef>();
            foreach (var t in sim.Content.Techs)
                if (sim.Economy.CanResearch(owner, t) == ResearchResult.Ok) techs.Add(t);
            if (techs.Count > 0) Issue(sim, log, new ResearchCommand(owner, techs[rng.Range(0, techs.Count)].Id));
        }
        else if (roll < 80)
        {
            if (!sim.Economy.CanLayRoads(owner)) return;
            int z1 = hall != null ? hall.MaxZ + 2 : cz;
            Issue(sim, log, new RoadCommand(owner, cx, z1, cx + rng.Range(-16, 17), z1 + rng.Range(-16, 17)));
        }
        else if (roll < 84)
        {
            // The raiders go for a city-state's totem the player does not hold.
            if (towns.Count == 0 || soldiers.Count == 0) return;
            var town = towns[rng.Range(0, towns.Count)];
            if (town.Holder == owner || sim.Cities.Totem(town) is not { } totem) return;
            var raiders = soldiers.GetRange(soldiers.Count - Raiders(soldiers.Count), Raiders(soldiers.Count));
            if (raiders.Count > 0) Issue(sim, log, new AttackBuildingCommand(owner, raiders.ToArray(), totem.Id));
        }
        else
        {
            var targets = new List<Building>();
            foreach (var b in sim.Buildings.All)
                if (b.Owner != owner && b.Def.Raidable) targets.Add(b);
            if (targets.Count == 0 || soldiers.Count == 0) return;
            var target = targets[rng.Range(0, targets.Count)];
            var raiders = soldiers.GetRange(soldiers.Count - Raiders(soldiers.Count), Raiders(soldiers.Count));
            if (raiders.Count == 0) return;
            raiders = new List<int>(Pick(rng, raiders, raiders.Count));
            Issue(sim, log, rng.Range(0, 2) == 0
                ? new CaptureCommand(owner, raiders.ToArray(), target.Id)
                : new BurnCommand(owner, raiders.ToArray(), target.Id));
        }
    }

    /// <summary>
    /// Switches a few workers to automatic or back, all the player's workers, repeat at a building that
    /// trains units, the stance of a few soldiers, or the form a unit type evolves into on its own.
    /// </summary>
    private static void SetAutomation(Simulation sim, CommandLog log, DeterministicRandom rng, int owner,
        List<int> workers, List<int> soldiers)
    {
        switch (rng.Range(0, 5))
        {
            case 0:
                if (workers.Count > 0) Issue(sim, log, new AutomateCommand(owner, Pick(rng, workers, 4), rng.Range(0, 3) > 0));
                break;
            case 1:
                Issue(sim, log, new AutomateAllCommand(owner, rng.Range(0, 3) > 0));
                break;
            case 2:
                var trainers = new List<Building>();
                foreach (var b in sim.Buildings.All)
                    if (b.Owner == owner && b.Def.Trains.Length > 0) trainers.Add(b);
                if (trainers.Count > 0) Issue(sim, log, new RepeatCommand(owner, trainers[rng.Range(0, trainers.Count)].Id, rng.Range(0, 2) == 0));
                break;
            case 3:
                if (soldiers.Count > 0) Issue(sim, log, new StanceCommand(owner, Pick(rng, soldiers, 10), (UnitStance)rng.Range(0, 3)));
                break;
            default:
                var evolving = new List<UnitDef>();
                foreach (var d in sim.Content.Units)
                    if (d.Forms.Count > 0) evolving.Add(d);
                if (evolving.Count == 0) break;
                var from = evolving[rng.Range(0, evolving.Count)];
                string? form = rng.Range(0, 3) > 0 ? from.Forms[rng.Range(0, from.Forms.Count)].Id : null;
                Issue(sim, log, new AutoEvolveCommand(owner, from.Id, form));
                break;
        }
    }

    /// <summary>A random run of up to <paramref name="max"/> of the ids, in order.</summary>
    private static int[] Pick(DeterministicRandom rng, List<int> ids, int max)
    {
        if (ids.Count == 0) return Array.Empty<int>();
        int first = rng.Range(0, ids.Count);
        return ids.GetRange(first, Math.Min(ids.Count - first, rng.Range(1, max + 1))).ToArray();
    }

    /// <summary>
    /// The block of that kind nearest to column (x, z), within 32 columns and a few levels of y, that a
    /// player could see and click: one with open air (or water) beside it.
    /// </summary>
    private static bool FindNear(VoxelWorld w, BlockType kind, int x, int y, int z, out (int X, int Y, int Z) found)
    {
        found = (-1, -1, -1);
        long best = long.MaxValue;
        for (int bz = z - 32; bz <= z + 32; bz++)
        for (int bx = x - 32; bx <= x + 32; bx++)
        for (int by = y - 4; by <= y + 8; by++)
        {
            if (!Harvest.Matches(kind, w.Get(bx, by, bz)) || w.Get(bx, by, bz) == BlockType.TreeRubble || !Exposed(w, bx, by, bz)) continue;
            long d = (long)(bx - x) * (bx - x) + (long)(bz - z) * (bz - z);
            if (d >= best) continue;
            best = d;
            found = (bx, by, bz);
        }
        return best != long.MaxValue;
    }

    private static bool Exposed(VoxelWorld w, int x, int y, int z) =>
        !BlockInfo.IsSolid(w.Get(x + 1, y, z)) || !BlockInfo.IsSolid(w.Get(x - 1, y, z)) || !BlockInfo.IsSolid(w.Get(x, y + 1, z))
        || !BlockInfo.IsSolid(w.Get(x, y, z + 1)) || !BlockInfo.IsSolid(w.Get(x, y, z - 1));

    private static void Issue(Simulation sim, CommandLog log, Command cmd)
    {
        sim.Schedule(sim.Tick, cmd);
        log.Add(sim.Tick, cmd);
    }
}
