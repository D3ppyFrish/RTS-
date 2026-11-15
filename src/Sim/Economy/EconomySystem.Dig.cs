using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Dig areas (<see cref="DigAreaCommand"/>): workers and sappers dig out an area the player marks, a layer at
/// a time from the top (see <see cref="DigArea"/>). Workers carry what the blocks give (stone, ore, coal,
/// wood, berries) to storage as when gathering; plain ground is just dug away. Sappers dig
/// <see cref="CombatSystem.SapperDigSpeed"/> times as fast and leave everything where it falls.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Ticks a digger whose layer is all taken by others waits before looking again.</summary>
    public const int DigBusyTicks = 10;

    private readonly List<DigArea> _digAreas = new();
    private int _nextDigArea = 1;

    /// <summary>The dig areas being worked, by id.</summary>
    public IReadOnlyList<DigArea> DigAreas => _digAreas;

    public DigArea? DigAreaById(int id)
    {
        foreach (var a in _digAreas)
            if (a.Id == id) return a;
        return null;
    }

    private static bool CanDig(UnitDef d) => d.Role is UnitRole.Worker or UnitRole.Sapper;

    internal void Apply(DigAreaCommand c)
    {
        int x1 = Math.Max(0, Math.Min(c.X1, c.X2)), x2 = Math.Min(World.SizeX - 1, Math.Max(c.X1, c.X2));
        int z1 = Math.Max(0, Math.Min(c.Z1, c.Z2)), z2 = Math.Min(World.SizeZ - 1, Math.Max(c.Z1, c.Z2));
        if (x1 > x2 || z1 > z2 || x2 - x1 >= DigArea.MaxSide || z2 - z1 >= DigArea.MaxSide) return;
        int top = -1;
        for (int z = z1; z <= z2; z++)
        for (int x = x1; x <= x2; x++)
            top = Math.Max(top, Walk.GroundLevel(World, x, z));
        int floor = c.Y;
        if (top < 0 || floor < 1 || floor >= top + 1 || top - floor > DigArea.MaxDepth) return;

        var units = OwnedUnits(c);
        units.RemoveAll(i => !CanDig(Content.Units[U.Def[i]]));
        if (units.Count == 0) return;
        var area = new DigArea(_nextDigArea++, c.PlayerId, x1, z1, x2, z2, floor, top);
        _digAreas.Add(area);
        foreach (int i in units)
        {
            StartJob(i, UnitJob.Dig, area.Id);
            PickDig(i, area);
        }
    }

    private void StepDig(int i)
    {
        var u = U;
        var area = DigAreaById(u.JobTarget[i]);
        switch (u.Phase[i])
        {
            case JobPhase.ToStore:
                if (StepToStore(i)) AfterUnload(i);
                return;
            case JobPhase.Wait:
                if (--u.JobTimer[i] > 0) return;
                if (u.CarryAmount[i] > 0 && (area == null || MustUnloadFirst(i, u.JobBlock[i]))) GoToStorage(i);
                else if (area == null) EndDig(i);
                else PickDig(i, area);
                return;
        }
        if (area == null)
        {
            EndDig(i);
            return;
        }
        int x = u.JobX[i], y = u.JobY[i], z = u.JobZ[i];
        if (!area.Wants(World, x, y, z) || World.Get(x, y, z) != u.JobBlock[i])
        {
            if (_sim.Physics.TreeFallingFrom(x, y, z)) return; // the tree it felled is coming down
            PickDig(i, area); // someone else took it, or it changed
            return;
        }
        var (baseY, topY) = Harvest.Span(World, x, y, z);
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        bool there = Harvest.InReach(ux, u.Y[i], uz, x, baseY, topY, z);

        if (u.Phase[i] == JobPhase.ToWork)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    u.JobMisses[i] = 0;
                    SetPhase(i, JobPhase.Working);
                    return;
                case Travel.NeedSpot:
                    if (Harvest.WorkSpot(World, x, baseY, topY, z, ux, u.Y[i], uz, out int sx, out int sy, out int sz))
                    {
                        _sim.SendUnit(i, sx, sz, sy);
                        return;
                    }
                    break; // nowhere to stand: as if the walk failed
                case Travel.Walking:
                    return;
            }
            // Cannot get to it: leave it, and try another (giving up after MaxMisses in a row).
            area.Skipped.Add(Harvest.Key(World, x, y, z));
            if (++u.JobMisses[i] < MaxMisses) PickDig(i, area);
            else EndDig(i);
            return;
        }

        // Working.
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        var kind = u.JobBlock[i];
        bool sapper = Content.Units[u.Def[i]].Role == UnitRole.Sapper;
        int ticks = sapper ? ByWork(i, CombatSystem.DigTicks(kind)) : ByWork(i, WorkTicks(u.Owner[i], kind));
        if (++u.JobTimer[i] < ticks * (topY - baseY + 1)) return;
        u.JobTimer[i] = 0;
        if (BlockInfo.IsTrunk(kind))
        {
            // Felled: it topples away from the digger, who waits for it to land (its rubble is dug like the rest).
            _sim.Physics.FellTree(x, baseY, topY, z, ux, uz);
            return;
        }
        World.Set(x, y, z, BlockType.Air);
        if (!sapper && Harvest.Yields(kind, out var good)) AddCarry(i, good, Harvest.Amount(kind));
        if (u.CarryAmount[i] > 0 && (u.CarryAmount[i] >= Capacity(u.Owner[i]) || Harvest.Bulk(kind))) GoToStorage(i);
        else PickDig(i, area);
    }

    /// <summary>Sends the digger to the area's next block, waits if its layer is taken, or ends the job when the area is done.</summary>
    private void PickDig(int i, DigArea area)
    {
        EnsureClaims();
        var u = U;
        switch (area.Next(World, _claimed, u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt(), out int x, out int y, out int z))
        {
            case DigArea.Pick.Found:
                SetPhase(i, JobPhase.ToWork);
                u.JobBlock[i] = World.Get(x, y, z);
                Claim(i, x, y, z);
                if (Content.Units[u.Def[i]].Role == UnitRole.Worker && MustUnloadFirst(i, u.JobBlock[i])) GoToStorage(i);
                return;
            case DigArea.Pick.Busy:
                SetPhase(i, JobPhase.Wait);
                u.JobTimer[i] = DigBusyTicks;
                return;
            default:
                _digAreas.Remove(area);
                EndDig(i);
                return;
        }
    }

    /// <summary>The digger is done: it takes anything it carries to storage, then stops.</summary>
    private void EndDig(int i)
    {
        if (U.CarryAmount[i] > 0)
        {
            StartJob(i, UnitJob.Deliver, 0);
            GoToStorage(i);
        }
        else EndJob(i);
    }

    /// <summary>Drops the areas nobody digs any more (every unit on one was given other orders, or died). Once a second.</summary>
    private void PruneDigAreas()
    {
        if (_digAreas.Count == 0 || _sim.Tick % Tps != 0) return;
        _digAreas.RemoveAll(a =>
        {
            for (int i = 0; i < U.Count; i++)
                if (U.Job[i] == UnitJob.Dig && U.JobTarget[i] == a.Id) return false;
            return true;
        });
    }

    private void MixDigAreas(ref ulong h)
    {
        foreach (var a in _digAreas) a.Mix(ref h);
    }
}
