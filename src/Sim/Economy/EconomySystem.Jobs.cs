using System;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Unit jobs. Each job is a small cycle of phases (see <see cref="JobPhase"/>): walk to the work, work,
/// walk to storage, unload, and back. Walking uses ordinary move orders; a unit counts as arrived as soon
/// as it is in reach of its work, wherever the walk would have ended.
/// </summary>
public sealed partial class EconomySystem
{
    private enum Travel
    {
        Arrived,
        Walking,
        /// <summary>Not there and not walking: the caller sends the unit (again) to a spot by the work.</summary>
        NeedSpot,
        /// <summary>The last walks all ended short of the work.</summary>
        Failed,
    }

    private void StepJob(int i)
    {
        switch (U.Job[i])
        {
            case UnitJob.Gather: StepGather(i); break;
            case UnitJob.Build: StepBuild(i); break;
            case UnitJob.Farm: StepFarm(i); break;
            case UnitJob.Haul: StepHaul(i); break;
            case UnitJob.Deliver: StepDeliver(i); break;
            case UnitJob.Capture:
            case UnitJob.Burn: StepRaider(i); break;
            case UnitJob.Trade: StepTrade(i); break;
            case UnitJob.Dig: StepDig(i); break;
            case UnitJob.Clean: StepClean(i); break;
            case UnitJob.Post: StepPost(i); break;
            case UnitJob.Supply: StepSupply(i); break;
        }
    }

    // --- Gathering ---------------------------------------------------------------------------------

    private void StepGather(int i)
    {
        var u = U;
        switch (u.Phase[i])
        {
            case JobPhase.ToStore:
                if (StepToStore(i)) AfterUnload(i);
                return;
            case JobPhase.Wait:
                if (--u.JobTimer[i] <= 0) Resume(i);
                return;
        }

        int x = u.JobX[i], y = u.JobY[i], z = u.JobZ[i];
        var kind = u.JobBlock[i];
        var block = World.Get(x, y, z);
        if (!Harvest.Matches(kind, block))
        {
            if (_sim.Physics.TreeFallingFrom(x, y, z)) return; // the tree it felled is coming down

            // Someone else took it: on to the next one, or home with what it has.
            if (Harvest.Repeats(kind) && NextTarget(i)) SetPhase(i, JobPhase.ToWork);
            else if (u.CarryAmount[i] > 0) GoToStorage(i);
            else EndJob(i);
            return;
        }
        var (baseY, topY) = Harvest.Span(World, x, y, z);
        bool there = Harvest.InReach(u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt(), x, baseY, topY, z);
        // Stone, ore and coal are only dug from where the worker can still walk out once the block is gone: in
        // reach on top of it, or down in a hole beside it, it walks on to a spot that leaves it a way out.
        if (there && u.Phase[i] == JobPhase.ToWork && Harvest.Quarried(kind)
            && !Harvest.LeavesWayOut(World, x, baseY, z, u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt()))
            there = false;

        if (u.Phase[i] == JobPhase.ToWork)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    u.JobMisses[i] = 0;
                    SetPhase(i, JobPhase.Working);
                    return;
                case Travel.NeedSpot:
                    // Prefer standing beside a quarried block over standing on it (and dropping into the hole). A
                    // quarried block is only worked from a spot the worker can walk out of again afterwards.
                    bool quarry = Harvest.Quarried(kind);
                    if (Harvest.WorkSpot(World, x, baseY, topY, z, u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt(),
                            out int sx, out int sy, out int sz, allowOnTop: !quarry, wayOut: quarry)
                        || (!quarry && Harvest.WorkSpot(World, x, baseY, topY, z, u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt(),
                            out sx, out sy, out sz)))
                    {
                        _sim.SendUnit(i, sx, sz, sy);
                        return;
                    }
                    break; // nowhere to stand: as if the walk failed
                case Travel.Walking:
                    return;
            }
            // Cannot get there: try another block of the kind, or give up (after MaxMisses blocks in a row).
            if (++u.JobMisses[i] < MaxMisses && Harvest.Repeats(kind) && NextTarget(i)) SetPhase(i, JobPhase.ToWork);
            else if (u.CarryAmount[i] > 0) GoToStorage(i);
            else EndJob(i);
            return;
        }

        // Working.
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (++u.JobTimer[i] < ByWork(i, WorkTicks(u.Owner[i], block) * (topY - baseY + 1))) return;
        u.JobTimer[i] = 0;
        if (BlockInfo.IsTrunk(block))
        {
            // Felled: it topples away from the worker, who waits for it to land and then clears its rubble.
            _sim.Physics.FellTree(x, baseY, topY, z, u.X[i].FloorToInt(), u.Z[i].FloorToInt());
            return;
        }
        if (Harvest.Quarried(kind) && ShutsIn(i, x, y, z))
        {
            // The ground changed while it worked (others dug nearby): taking this block now would leave it, or
            // somebody else close by, in a hole with no way out. It leaves the block and moves on.
            if (NextTarget(i)) SetPhase(i, JobPhase.ToWork);
            else if (u.CarryAmount[i] > 0) GoToStorage(i);
            else EndJob(i);
            return;
        }
        World.Set(x, y, z, BlockType.Air);
        if (Harvest.Yields(block, out var good)) AddCarry(i, good, Harvest.Amount(block));
        if (!Harvest.Repeats(kind)) EndJob(i);
        else if (u.CarryAmount[i] >= Capacity(u.Owner[i]) || Harvest.Bulk(block) || !NextTarget(i)) GoToStorage(i);
        else SetPhase(i, JobPhase.ToWork);
    }

    /// <summary>
    /// True if digging block (x, y, z) now would shut someone in: the worker at it (see
    /// <see cref="Harvest.LeavesWayOut"/>), or any other unit standing close by that can walk out to open ground
    /// now and could not with the block gone. Checked as the block is taken, since workers digging side by side
    /// each see the quarry as it was when they picked their blocks.
    /// </summary>
    private bool ShutsIn(int i, int x, int y, int z)
    {
        var u = U;
        if (!Harvest.LeavesWayOut(World, x, y, z, u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt())) return true;
        for (int j = 0; j < u.Count; j++)
        {
            if (j == i) continue;
            int jx = u.X[j].FloorToInt(), jz = u.Z[j].FloorToInt();
            if (Math.Max(Math.Abs(jx - x), Math.Abs(jz - z)) > ShutInReach || Math.Abs(u.Y[j] - y) > ShutInReach) continue;
            if (Harvest.ShutsIn(World, x, y, z, jx, u.Y[j], jz)) return true;
        }
        return false;
    }

    /// <summary>How near (columns, and levels) to a dug block a unit must stand for <see cref="ShutsIn"/> to look out for it.</summary>
    public const int ShutInReach = 4;

    /// <summary>Moves the gatherer on to the nearest unclaimed block of its kind near the last one. False if none.</summary>
    private bool NextTarget(int i)
    {
        EnsureClaims();
        if (!Harvest.FindNext(World, U.JobBlock[i], U.JobX[i], U.JobY[i], U.JobZ[i], _claimed,
                out int x, out int y, out int z))
            return false;
        Claim(i, x, y, z);
        return true;
    }

    private void Claim(int i, int x, int y, int z)
    {
        U.JobX[i] = x;
        U.JobY[i] = y;
        U.JobZ[i] = z;
        _claimed.Add(Harvest.Key(World, x, y, z));
    }

    /// <summary>Rebuilds the set of claimed gather targets, once per tick and only when something needs it.</summary>
    private void EnsureClaims()
    {
        if (_claimedTick == _sim.Tick) return;
        _claimedTick = _sim.Tick;
        _claimed.Clear();
        for (int i = 0; i < U.Count; i++)
            if (U.Job[i] is UnitJob.Gather or UnitJob.Dig) _claimed.Add(Harvest.Key(World, U.JobX[i], U.JobY[i], U.JobZ[i]));
    }

    /// <summary>True if the worker should drop off what it carries before gathering this kind of block.</summary>
    private bool MustUnloadFirst(int i, BlockType kind)
    {
        if (U.CarryAmount[i] == 0 || !Harvest.Yields(kind, out var good)) return false;
        return Harvest.Bulk(kind) || good != U.Carry[i] || U.CarryAmount[i] >= Capacity(U.Owner[i]);
    }

    private void AddCarry(int i, Good good, int amount)
    {
        if (U.CarryAmount[i] == 0) U.Carry[i] = good;
        U.CarryAmount[i] += amount;
        Player(U.Owner[i]).Gathered[(int)good] += amount;
    }

    // --- Building, farming, hauling, raiding -------------------------------------------------------

    private void StepBuild(int i)
    {
        var b = Buildings.Get(U.JobTarget[i]);
        if (b != null && b.Owner == U.Owner[i] && (b.Def.IsWall || b.Def.IsBridge) && !b.WantsBuilders)
        {
            // That stretch of wall (or bridge) is up, or upgraded: on to the next one along.
            var next = b.Def.IsBridge ? NextBridgeSite(U.Owner[i], b.X, b.Z) : NextWallSite(U.Owner[i], b.X, b.Z);
            if (next != null)
            {
                StartJob(i, UnitJob.Build, next.Id);
                b = next;
            }
        }
        if (b == null || b.Owner != U.Owner[i] || !b.WantsBuilders)
        {
            EndJob(i);
            return;
        }
        if (WalkToBuilding(i, b, out bool there)) return;
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        // A tick of work for each 100 of the unit's work speed (JobTimer keeps the rest): on the site, or on
        // the upgrade of a building that stands.
        var u = U;
        u.JobTimer[i] += Content.Units[u.Def[i]].WorkPercent;
        for (; u.JobTimer[i] >= 100; u.JobTimer[i] -= 100)
        {
            if (b.Upgrade != null) b.UpgradeWork = Math.Min(b.UpgradeWork + 1, b.Upgrade.BuildTicks);
            else if (b.Work < b.Def.BuildTicks) b.Work++;
        }
        if ((_sim.Tick + u.Id[i]) % Tps == 0) _sim.Evolution.Built(i);
    }

    private void StepFarm(int i)
    {
        var u = U;
        var farm = Buildings.Get(u.JobTarget[i]);
        if (farm == null || farm.Owner != u.Owner[i] || !farm.Active)
        {
            EndJob(i);
            return;
        }
        switch (u.Phase[i])
        {
            case JobPhase.ToStore:
                if (StepToStore(i)) AfterUnload(i);
                return;
            case JobPhase.Wait:
                if (--u.JobTimer[i] <= 0) Resume(i);
                return;
        }
        if (WalkToBuilding(i, farm, out bool there)) return;
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (++u.JobTimer[i] < ByWork(i, FarmTicks)) return;
        AddCarry(i, Good.Food, FarmYieldOf(u.Owner[i]));
        GoToStorage(i);
    }

    /// <summary>
    /// A smelter's hauler: brings its iron to storage, and keeps it supplied with ore and coal from storage
    /// (enough for two ingots), one load at a time.
    /// </summary>
    private void StepHaul(int i)
    {
        var u = U;
        var smelter = Buildings.Get(u.JobTarget[i]);
        if (smelter == null || smelter.Owner != u.Owner[i] || !smelter.Active)
        {
            EndJob(i);
            return;
        }
        switch (u.Phase[i])
        {
            case JobPhase.ToStore:
                if (StepToStore(i)) DecideHaul(i, smelter);
                return;
            case JobPhase.Wait:
                if (--u.JobTimer[i] <= 0) DecideHaul(i, smelter);
                return;
            case JobPhase.Fetch:
                StepFetch(i, smelter);
                return;
        }
        if (WalkToBuilding(i, smelter, out bool there)) return;
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        // At the smelter: drop off ore or coal, or pick up iron.
        if (u.CarryAmount[i] > 0 && u.Carry[i] is Good.IronOre or Good.Coal)
        {
            smelter.Stock[(int)u.Carry[i]] += u.CarryAmount[i];
            u.CarryAmount[i] = 0;
        }
        else if (u.CarryAmount[i] == 0 && smelter.Stock[(int)Good.Iron] > 0)
        {
            int take = System.Math.Min(Capacity(u.Owner[i]), smelter.Stock[(int)Good.Iron]);
            smelter.Stock[(int)Good.Iron] -= take;
            u.Carry[i] = Good.Iron;
            u.CarryAmount[i] = take;
        }
        DecideHaul(i, smelter);
    }

    private void DecideHaul(int i, Building smelter)
    {
        var u = U;
        if (u.CarryAmount[i] > 0)
        {
            if (u.Carry[i] is Good.IronOre or Good.Coal) SetPhase(i, JobPhase.ToWork);
            else GoToStorage(i);
            return;
        }
        if (smelter.Stock[(int)Good.Iron] > 0)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        Building? from = null;
        var need = Good.IronOre;
        if (smelter.Stock[(int)Good.IronOre] < 2 * OrePerIngot)
            from = NearestStorage(u.Owner[i], ux, uz, Good.IronOre);
        if (from == null && smelter.Stock[(int)Good.Coal] < 2 * CoalPerIngot)
        {
            need = Good.Coal;
            from = NearestStorage(u.Owner[i], ux, uz, Good.Coal);
        }
        if (from == null)
        {
            Wait(i);
            return;
        }
        u.JobStore[i] = from.Id;
        u.JobX[i] = (int)need;
        SetPhase(i, JobPhase.Fetch);
    }

    private void StepFetch(int i, Building smelter)
    {
        var u = U;
        var good = (Good)u.JobX[i];
        var s = Buildings.Get(u.JobStore[i]);
        if (s == null || s.Owner != u.Owner[i] || !s.Active || !s.Def.Storage || s.Stock[(int)good] == 0)
        {
            DecideHaul(i, smelter);
            return;
        }
        switch (Approach(i, AtBuilding(i, s)))
        {
            case Travel.Arrived:
                int take = System.Math.Min(Capacity(u.Owner[i]), s.Stock[(int)good]);
                s.Stock[(int)good] -= take;
                u.Carry[i] = good;
                u.CarryAmount[i] = take;
                DecideHaul(i, smelter);
                return;
            case Travel.NeedSpot:
                if (AccessSpot(s, out int x, out int z, out int y)) _sim.SendUnit(i, x, z, y);
                else Wait(i);
                return;
            case Travel.Failed:
                Wait(i);
                return;
        }
    }

    private void StepDeliver(int i)
    {
        switch (U.Phase[i])
        {
            case JobPhase.Wait:
                if (--U.JobTimer[i] <= 0) Resume(i);
                return;
            default:
                if (StepToStore(i)) EndJob(i);
                return;
        }
    }

    private void StepRaider(int i)
    {
        var b = Buildings.Get(U.JobTarget[i]);
        if (b == null || b.Owner == U.Owner[i] || !b.Def.Raidable)
        {
            EndJob(i);
            return;
        }
        if (WalkToBuilding(i, b, out bool there)) return;
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        _raiders.Add((b.Id, i));
    }

    /// <summary>
    /// The ToWork phase of a job at a building: walks there and switches to Working on arrival (or ends
    /// the job if it cannot get there). Returns true if this tick was spent walking; otherwise the unit is
    /// Working and <paramref name="there"/> says whether it is still at the building.
    /// </summary>
    private bool WalkToBuilding(int i, Building b, out bool there)
    {
        there = AtBuilding(i, b);
        if (U.Phase[i] != JobPhase.ToWork) return false;
        switch (Approach(i, there))
        {
            case Travel.Arrived:
                SetPhase(i, JobPhase.Working);
                break;
            case Travel.NeedSpot:
                // A wall has no door: its builder goes to the side of it it is on (and tries the other after).
                if (b.Def.IsWall && U.JobRetry[i] == 1 && SpotBeside(b, U.X[i].FloorToInt(), U.Z[i].FloorToInt(), out int x, out int z, out int y))
                    _sim.SendUnit(i, x, z, y);
                else if (AccessSpot(b, out x, out z, out y)) _sim.SendUnit(i, x, z, y);
                else EndJob(i);
                break;
            case Travel.Failed:
                EndJob(i);
                break;
        }
        return true;
    }

    // --- Storage trips -----------------------------------------------------------------------------

    /// <summary>Heads for the nearest storage to unload; waits if the player has none.</summary>
    private void GoToStorage(int i)
    {
        var s = NearestStorage(U.Owner[i], U.X[i].FloorToInt(), U.Z[i].FloorToInt());
        if (s == null)
        {
            Wait(i);
            return;
        }
        SetPhase(i, JobPhase.ToStore);
        U.JobStore[i] = s.Id;
    }

    /// <summary>Walks to storage <see cref="UnitStore.JobStore"/>; true on the tick it unloads there.</summary>
    private bool StepToStore(int i)
    {
        var u = U;
        var s = Buildings.Get(u.JobStore[i]);
        if (s == null || s.Owner != u.Owner[i] || !s.Active || !s.Def.Storage)
        {
            GoToStorage(i);
            return false;
        }
        switch (Approach(i, AtBuilding(i, s)))
        {
            case Travel.Arrived:
                if (u.CarryAmount[i] > 0) s.Stock[(int)u.Carry[i]] += u.CarryAmount[i];
                u.CarryAmount[i] = 0;
                return true;
            case Travel.NeedSpot:
                if (AccessSpot(s, out int x, out int z, out int y)) _sim.SendUnit(i, x, z, y);
                else Wait(i);
                return false;
            case Travel.Failed:
                Wait(i);
                return false;
            default:
                return false;
        }
    }

    /// <summary>After unloading: back to the job's work, or done.</summary>
    private void AfterUnload(int i)
    {
        switch (U.Job[i])
        {
            case UnitJob.Gather:
                var kind = U.JobBlock[i];
                if (Harvest.Matches(kind, World.Get(U.JobX[i], U.JobY[i], U.JobZ[i])) || (Harvest.Repeats(kind) && NextTarget(i)))
                    SetPhase(i, JobPhase.ToWork);
                else EndJob(i);
                return;
            case UnitJob.Farm:
                SetPhase(i, JobPhase.ToWork);
                return;
            case UnitJob.Dig:
                var area = DigAreaById(U.JobTarget[i]);
                if (area == null) EndJob(i);
                else if (area.Wants(World, U.JobX[i], U.JobY[i], U.JobZ[i]) && World.Get(U.JobX[i], U.JobY[i], U.JobZ[i]) == U.JobBlock[i])
                    SetPhase(i, JobPhase.ToWork);
                else PickDig(i, area);
                return;
            default:
                EndJob(i);
                return;
        }
    }

    /// <summary>The end of a wait (for storage to exist or to be reachable): try to unload again, or carry on.</summary>
    private void Resume(int i)
    {
        if (U.CarryAmount[i] > 0) GoToStorage(i);
        else AfterUnload(i);
    }

    // --- Phase bookkeeping -------------------------------------------------------------------------

    private void StartJob(int i, UnitJob job, int target)
    {
        U.ClearJob(i);
        U.ClearCombat(i);
        Halt(i);
        U.Job[i] = job;
        U.JobTarget[i] = target;
    }

    private void EndJob(int i)
    {
        U.ClearJob(i);
        Halt(i);
    }

    /// <summary>Ticks of work that take unit i, at its work speed, what take others <paramref name="ticks"/>.</summary>
    private int ByWork(int i, int ticks) => Math.Max(1, ticks * 100 / Content.Units[U.Def[i]].WorkPercent);

    private void SetPhase(int i, JobPhase phase)
    {
        Halt(i);
        U.Phase[i] = phase;
        U.JobRetry[i] = 0;
        U.JobTimer[i] = 0;
    }

    private void Wait(int i)
    {
        SetPhase(i, JobPhase.Wait);
        U.JobTimer[i] = WaitTicks;
    }

    private void Halt(int i)
    {
        U.Order[i] = UnitOrder.Idle;
        U.Blocked[i] = 0;
        U.Field[i] = 0;
    }

    /// <summary>One tick of a walk to the work: arrived (stopped), still walking, or needing a (new) walk.</summary>
    private Travel Approach(int i, bool there)
    {
        if (there)
        {
            Halt(i);
            U.JobRetry[i] = 0;
            return Travel.Arrived;
        }
        if (U.Order[i] == UnitOrder.Move) return Travel.Walking;
        if (U.JobRetry[i] >= MaxRetries) return Travel.Failed;
        U.JobRetry[i]++;
        return Travel.NeedSpot;
    }
}
