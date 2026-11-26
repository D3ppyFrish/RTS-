using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

/// <summary>
/// Plank bridges (see <see cref="BridgeCommand"/>: a level deck of planks across water, a drop or a trench, 2 planks a
/// column). The smart levels (Easy and up) bridge the water between their town and the enemy's when the way by land is
/// cut off, or longer than the way over a bridge by at least <see cref="BridgeSaves"/> steps: they look along the banks
/// they can walk to from their town hall (within <see cref="BridgeSearch"/> steps) for a straight line, across x or
/// along z, over the water to ground from which they can walk to the enemy town hall they know of (or where they guess
/// one stands), short enough for one bridge and with room for a deck on every column, and take the one that makes the
/// whole way shortest. With no sawmill they put one up first; once they have the planks, up to
/// <see cref="BridgeWorkers"/> of their workers lay the bridge, and their waves walk over it like any ground. One
/// bridge at a time; a bridge of theirs knocked out is looked at again like any other. Hard bridges only while its town
/// is calm. The classic level never lays bridges. The numbers are first guesses for playtesting.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>Steps from its town hall within which it looks for the near bank of a bridge.</summary>
    public const int BridgeSearch = 96;

    /// <summary>Steps a bridge must save on the way by land to the enemy (when there is one) to be worth the planks.</summary>
    public const int BridgeSaves = 40;

    /// <summary>Workers it sends to lay a bridge.</summary>
    public const int BridgeWorkers = 3;

    /// <summary>No bridge before it has this many workers.</summary>
    public const int BridgeFromWorkers = 8;

    /// <summary>Ticks between its looks for a bridge (30 seconds), and after a look that found none worth laying (2 minutes).</summary>
    public const int BridgeLookTicks = 30 * Tps, BridgeIdleTicks = 120 * Tps;

    private long _bridgeLook;
    private (int AX, int AZ, int BX, int BZ)? _bridge;
    private int[]? _bridgeFromHall, _bridgeFromGoal;
    private readonly List<BridgeSpan> _bridgeSpans = new();

    /// <summary>The ends of the bridge it laid (or is laying) for the way to the enemy, if any.</summary>
    public (int AX, int AZ, int BX, int BZ)? BridgeEnds => _bridge;

    /// <summary>Bridges it has laid out.</summary>
    public int BridgesLaid { get; private set; }

    /// <summary>
    /// Whether it lays bridges at all (it does unless told not to): the tests of its other ways over the water, ice and
    /// ships, turn it off so that a bridge laid before the wave sets out does not take their place.
    /// </summary>
    public bool Bridging { get; set; } = true;

    /// <summary>The smart levels' bridge toward the enemy (see the top of this file).</summary>
    private void Bridges(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Smart || !Bridging || !hall.Active || sim.Tick < _bridgeLook) return;
        _bridgeLook = sim.Tick + BridgeLookTicks;
        if (BridgeDef(sim) is not { } def) return;

        if (_bridge is var (ax0, az0, bx0, bz0))
        {
            var (sites, whole) = BridgeState(sim, ax0, az0, bx0, bz0);
            if (sites.Count > 0)
            {
                // Still going up: send workers again to a column nobody builds while one can be built.
                SendBridgeWorkers(sim, sites, issue);
                return;
            }
            if (whole) return; // it stands: the waves walk over it
            Note(sim, $"finds its bridge from {ax0},{az0} to {bx0},{bz0} knocked out");
            _bridge = null;
        }

        if (Workers(sim) < BridgeFromWorkers) return;
        var (hx, hz) = Middle(hall);
        var goal = EnemyHall(sim, hx, hz) is { } eh ? (eh.AX, eh.AZ) : SearchGoal(sim, hx, hz, hx, hz);
        if (goal is not var (gx, gz)) return;
        if (PlanBridgeCrossing(sim, hall, gx, gz) is not var (ax, az, bx, bz, decks))
        {
            _bridgeLook = sim.Tick + BridgeIdleTicks;
            return;
        }

        // Planks: a sawmill first, then the planks for every deck column.
        bool sawmill = false;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == Player && b.Def.Kind == BuildingKind.Workshop && b.Def.Makes == Good.Planks) { sawmill = true; break; }
        if (!sawmill)
        {
            if (Making(sim, Good.Planks) is not { } mill || !Affordable(sim, mill.Cost)) return;
            if (Hard) Want(sim, mill, hall, HardTownRadius, false, issue);
            else PlaceNear(sim, mill, (hx, hz), Automation.AutomationSystem.PlanRadius, issue);
            Note(sim, "lays out a sawmill for the planks of a bridge");
            return;
        }
        var cost = new GoodAmount[def.Cost.Length];
        for (int k = 0; k < cost.Length; k++) cost[k] = new GoodAmount(def.Cost[k].Good, def.Cost[k].Amount * decks);
        if (!Affordable(sim, cost)) return;

        var workers = SpareWorkers(sim, BridgeWorkers);
        if (workers.Count == 0) return;
        issue(new BridgeCommand(Player, workers.ToArray(), def.Id, ax, az, bx, bz));
        if (Hard) Spend(cost);
        _bridge = (ax, az, bx, bz);
        BridgesLaid++;
        Note(sim, $"lays a bridge of {decks} planks' columns from {ax},{az} to {bx},{bz} on the way to the enemy");
    }

    private static BuildingDef? BridgeDef(Simulation sim)
    {
        foreach (var d in sim.Content.Buildings)
            if (d.IsBridge) return d;
        return null;
    }

    /// <summary>
    /// Its bridge columns along the line from (ax, az) to (bx, bz) still to be built, and whether the way across is
    /// whole: no column of the line that needs a deck (see <see cref="EconomySystem.PlanBridge"/>) is without one.
    /// </summary>
    private (List<Building> Sites, bool Whole) BridgeState(Simulation sim, int ax, int az, int bx, int bz)
    {
        var sites = new List<Building>();
        foreach (var (x, z) in EconomySystem.WallLine(ax, az, bx, bz))
            if (sim.Buildings.AtColumn(x, z) is { } b && b.Owner == Player && b.Def.IsBridge && b.State == BuildingState.Site) sites.Add(b);
        bool whole = sim.Economy.PlanBridge(ax, az, bx, bz, _bridgeSpans) >= 0;
        if (whole)
            foreach (var s in _bridgeSpans)
                if (s.Room) { whole = false; break; } // a column open to the water with no deck on it
        return (sites, whole);
    }

    /// <summary>While no worker of its own builds any of the bridge's sites, sends spare ones to the one that can be built now.</summary>
    private void SendBridgeWorkers(Simulation sim, List<Building> sites, Action<Command> issue)
    {
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || u.Job[i] != UnitJob.Build) continue;
            foreach (var b in sites)
                if (u.JobTarget[i] == b.Id) return;
        }
        foreach (var b in sites)
        {
            if (!sim.Economy.BridgeAccess(b, out _, out _, out _)) continue;
            var workers = SpareWorkers(sim, BridgeWorkers);
            if (workers.Count > 0) issue(new WorkCommand(Player, workers.ToArray(), b.Id));
            return;
        }
    }

    /// <summary>
    /// The best straight bridge for the way from the town hall to (gx, gz), with the number of deck columns it takes:
    /// from a bank it can walk to, over the water to ground from which it can walk to the goal (see the top of this
    /// file); null if there is none, or the way by land is short enough without one.
    /// </summary>
    private (int AX, int AZ, int BX, int BZ, int Decks)? PlanBridgeCrossing(Simulation sim, Building hall, int gx, int gz)
    {
        var g = sim.Walkable;
        var (sx, sz) = Approach(sim, hall);
        int start = g.NearestNode(sx, sz, hall.Y);
        if (start < 0) return null;
        var ends = new List<int>();
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int x = gx + dx, z = gz + dz;
            if ((uint)x >= (uint)g.SizeX || (uint)z >= (uint)g.SizeZ) continue;
            for (int s = 0; s < WalkGraph.MaxLevels; s++)
            {
                int n = ((z * g.SizeX + x) << WalkGraph.LevelBits) + s;
                if (g.Level(n) < 0) break;
                ends.Add(n);
            }
        }
        if (ends.Count == 0) return null;

        _bridgeFromHall = Steps(sim, new List<int> { start }, _bridgeFromHall);
        _bridgeFromGoal = Steps(sim, ends, _bridgeFromGoal);
        var fromHall = _bridgeFromHall;
        var fromGoal = _bridgeFromGoal;
        int land = int.MaxValue;
        foreach (int n in ends)
            if (fromHall[n] >= 0) land = Math.Min(land, fromHall[n]);

        // Shortest whole way first; between ways as short, the straightest.
        int best = land == int.MaxValue ? int.MaxValue : land - BridgeSaves;
        long bestStraight = long.MaxValue;
        (int, int, int, int, int)? plan = null;
        for (int a = 0; a < fromHall.Length; a++)
        {
            int da = fromHall[a];
            if (da < 0 || da > BridgeSearch || da > best) continue;
            int ax = g.ColumnX(a), az = g.ColumnZ(a), ay = g.Level(a);
            if (KeepsOff(sim, ax, az)) continue;
            for (int dir = 0; dir < 4; dir++)
            {
                int stx = dir == 0 ? 1 : dir == 1 ? -1 : 0, stz = dir == 2 ? 1 : dir == 3 ? -1 : 0;
                for (int k = 1; k <= EconomySystem.MaxBridge + 1; k++)
                {
                    int x = ax + stx * k, z = az + stz * k;
                    if ((uint)x >= (uint)g.SizeX || (uint)z >= (uint)g.SizeZ) break;
                    int b = g.NearestNode(x, z, ay);
                    if (b < 0 || Math.Abs(g.Level(b) - ay) > Walk.MaxClimb) continue; // water or a drop: on over it
                    if (k == 1) break; // still on the bank: the column next to the water looks from there
                    int db = fromGoal[b];
                    long straight = Dist2(ax, az, sx, sz) + Dist2(x, z, gx, gz);
                    if (db < 0 || da + k + db > best || (da + k + db == best && straight >= bestStraight)) break;
                    if (KeepsOff(sim, x, z) || sim.Economy.PlanBridge(ax, az, x, z, _bridgeSpans) < 0) break;
                    int decks = 0;
                    bool room = true;
                    foreach (var s in _bridgeSpans)
                    {
                        if (!s.Room) { room = false; break; }
                        decks++;
                    }
                    if (!room || decks == 0) break;
                    (best, bestStraight) = (da + k + db, straight);
                    plan = (ax, az, x, z, decks);
                    break;
                }
            }
        }
        return plan;
    }
}
