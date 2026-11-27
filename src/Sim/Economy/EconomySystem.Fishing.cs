using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Fishing boats (a boat with a <see cref="UnitDef.Catch"/>; the harbor moodboard's boats off the beach). A fishing boat
/// on the <see cref="UnitJob.Fish"/> job goes round from its dock: it sails out to its fishing ground, open water
/// (every column within <see cref="OpenReach"/> of it water at the same level) within <see cref="FishReach"/> columns of
/// the dock by water, the nearest one by water no other fishing boat is fishing within <see cref="FishApart"/> columns of
/// (<see cref="JobPhase.ToWork"/>); fishes there for its <see cref="UnitDef.FishTicks"/> (<see cref="JobPhase.Working"/>);
/// and sails the catch home to the water the dock launches its boats onto (<see cref="JobPhase.ToStore"/>), where it
/// goes into its owner's storage nearest the dock. Then it goes out again. With no way out or home, or no storage, it
/// waits and tries again. It keeps going until given another order.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>How far (columns, by water) from its dock a fishing boat goes for its fishing ground.</summary>
    public const int FishReach = 40;
    /// <summary>A fishing ground is open water: every column within this many columns of it is water at the same level.</summary>
    public const int OpenReach = 2;
    /// <summary>Columns (either way) a fishing boat keeps its fishing ground from another's while there are others.</summary>
    public const int FishApart = 4;

    private readonly List<(int X, int Z)> _fishGrounds = new();

    /// <summary>The fishing boats among the command's units go fishing from the dock it names, or their nearest.</summary>
    internal void Apply(FishCommand c)
    {
        Building? dock = null;
        if (c.Dock != 0)
        {
            dock = Buildings.Get(c.Dock);
            if (dock == null || dock.Owner != c.PlayerId || !dock.Active) return;
        }
        foreach (int i in OwnedUnits(c))
        {
            var def = Content.Units[U.Def[i]];
            if (!def.IsFisher) continue;
            var home = dock != null ? (FishesFrom(dock, def) && Reaches(i, dock) ? dock : null) : NearestDock(i);
            if (home == null) continue;
            StartFishing(i, home);
        }
    }

    /// <summary>Fishing boat i starts fishing from the dock (bringing home first what it holds).</summary>
    internal void StartFishing(int i, Building dock)
    {
        int carry = U.CarryAmount[i];
        var good = U.Carry[i];
        StartJob(i, UnitJob.Fish, dock.Id);
        U.Carry[i] = good;
        U.CarryAmount[i] = carry;
        SetPhase(i, carry > 0 ? JobPhase.ToStore : JobPhase.ToWork);
    }

    /// <summary>True if the building is a dock that builds fishing boats of the kind (and so takes their catch).</summary>
    public static bool FishesFrom(Building b, UnitDef def) => b.Active && Array.IndexOf(b.Def.Trains, def.Id) >= 0;

    // True if fishing boat i can sail to the water the dock launches its boats onto.
    private bool Reaches(int i, Building dock) => HomeDistance(i, dock) >= 0;

    // How far (by water, 10 a column) fishing boat i is from the dock's launch water, or -1 if it cannot get there.
    private int HomeDistance(int i, Building dock)
    {
        if (!_sim.Boats.Launch(dock, out int lx, out int lz, out _)) return -1;
        var dist = _sim.Boats.WaysTo(lx, lz);
        int x = U.X[i].FloorToInt(), z = U.Z[i].FloorToInt();
        if (dist == null || !World.InBounds(x, 0, z)) return -1;
        return dist[z * World.SizeX + x];
    }

    /// <summary>The owner's dock nearest fishing boat i by water that builds its kind, or null if none is in reach.</summary>
    public Building? NearestDock(int i)
    {
        var def = Content.Units[U.Def[i]];
        Building? best = null;
        int bestD = int.MaxValue;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != U.Owner[i] || !FishesFrom(b, def)) continue;
            int d = HomeDistance(i, b);
            if (d < 0 || d >= bestD) continue;
            bestD = d;
            best = b;
        }
        return best;
    }

    private void StepFish(int i)
    {
        var u = U;
        var def = Content.Units[u.Def[i]];
        var dock = Buildings.Get(u.JobTarget[i]);
        if (dock == null || dock.Owner != u.Owner[i] || !FishesFrom(dock, def))
        {
            // Its dock is gone: on to another, or it stops with what it holds.
            if (NearestDock(i) is { } other) u.JobTarget[i] = (dock = other).Id;
            else
            {
                int carry = u.CarryAmount[i];
                EndJob(i);
                u.CarryAmount[i] = carry;
                u.Carry[i] = Good.Food;
                return;
            }
        }
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        switch (u.Phase[i])
        {
            case JobPhase.Wait:
                if (--u.JobTimer[i] <= 0) SetPhase(i, u.CarryAmount[i] > 0 ? JobPhase.ToStore : JobPhase.ToWork);
                return;
            case JobPhase.ToWork:
                if (u.JobY[i] == 1 && x == u.JobX[i] && z == u.JobZ[i])
                {
                    SetPhase(i, JobPhase.Working);
                    return;
                }
                if (u.Order[i] != UnitOrder.Idle) return;
                // Not sent yet (or it stopped short of its ground): pick a ground and sail there, or wait.
                if (u.JobRetry[i] == 0 && FishingGround(i, dock, out int gx, out int gz))
                {
                    u.JobX[i] = gx;
                    u.JobZ[i] = gz;
                    u.JobY[i] = 1;
                    u.JobRetry[i] = 1;
                    _sim.Boats.Steer(i, gx, gz);
                }
                else Wait(i);
                return;
            case JobPhase.Working:
                if (++u.JobTimer[i] < def.FishTicks) return;
                int amount = def.Catch + def.Catch * _sim.Cities.FishingBonusOf(u.Owner[i]) / 100;
                AddCarry(i, Good.Food, amount);
                SetPhase(i, JobPhase.ToStore);
                return;
            case JobPhase.ToStore:
                if (!_sim.Boats.Launch(dock, out int lx, out int lz, out _))
                {
                    Wait(i);
                    return;
                }
                if (Math.Abs(x - lx) <= 1 && Math.Abs(z - lz) <= 1)
                {
                    var store = NearestStorage(u.Owner[i], dock.X + dock.Def.SizeX / 2, dock.Z + dock.Def.SizeZ / 2);
                    if (store == null)
                    {
                        Wait(i);
                        return;
                    }
                    if (u.CarryAmount[i] > 0) store.Stock[(int)u.Carry[i]] += u.CarryAmount[i];
                    u.CarryAmount[i] = 0;
                    SetPhase(i, JobPhase.ToWork);
                    return;
                }
                if (u.Order[i] != UnitOrder.Idle) return;
                if (u.JobRetry[i] == 0)
                {
                    u.JobRetry[i] = 1;
                    _sim.Boats.Steer(i, lx, lz);
                }
                else Wait(i);
                return;
            default:
                SetPhase(i, JobPhase.ToWork);
                return;
        }
    }

    /// <summary>
    /// Fishing boat i's fishing ground from the dock: the open water within <see cref="FishReach"/> columns of the dock's
    /// launch water by water nearest it, then in a fixed order, keeping <see cref="FishApart"/> from the other fishing
    /// boats' grounds while there is such water. False if there is no open water in reach.
    /// </summary>
    public bool FishingGround(int i, Building dock, out int gx, out int gz)
    {
        gx = gz = -1;
        if (!_sim.Boats.Launch(dock, out int lx, out int lz, out _)) return false;
        var dist = _sim.Boats.WaysTo(lx, lz);
        if (dist == null) return false;
        var u = U;
        _fishGrounds.Clear();
        for (int j = 0; j < u.Count; j++)
            if (j != i && u.Job[j] == UnitJob.Fish && u.JobY[j] == 1) _fishGrounds.Add((u.JobX[j], u.JobZ[j]));
        // The boat itself must get there: it fishes only water joined to the dock's.
        int bx = u.X[i].FloorToInt(), bz = u.Z[i].FloorToInt();
        if (!World.InBounds(bx, 0, bz) || dist[bz * World.SizeX + bx] < 0) return false;
        int best = int.MaxValue, bestFree = int.MaxValue, fx = -1, fz = -1;
        for (int z = Math.Max(0, lz - FishReach); z <= Math.Min(World.SizeZ - 1, lz + FishReach); z++)
        for (int x = Math.Max(0, lx - FishReach); x <= Math.Min(World.SizeX - 1, lx + FishReach); x++)
        {
            int d = dist[z * World.SizeX + x];
            if (d < 0 || d > FishReach * 10 || (d >= best && d >= bestFree) || !OpenWater(x, z)) continue;
            if (d < best) (best, gx, gz) = (d, x, z);
            if (d < bestFree && !Claimed(x, z)) (bestFree, fx, fz) = (d, x, z);
        }
        if (fx >= 0) (gx, gz) = (fx, fz);
        return gx >= 0;
    }

    private bool Claimed(int x, int z)
    {
        foreach (var (cx, cz) in _fishGrounds)
            if (Math.Abs(cx - x) < FishApart && Math.Abs(cz - z) < FishApart) return true;
        return false;
    }

    /// <summary>True if column (x, z) is open water: every column within <see cref="OpenReach"/> of it water at its level.</summary>
    public bool OpenWater(int x, int z)
    {
        int s = _sim.Boats.Surface(x, z);
        if (s < 0) return false;
        for (int dz = -OpenReach; dz <= OpenReach; dz++)
        for (int dx = -OpenReach; dx <= OpenReach; dx++)
            if (_sim.Boats.Surface(x + dx, z + dz) != s) return false;
        return true;
    }
}
