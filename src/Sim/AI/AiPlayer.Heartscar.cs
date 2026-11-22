using System;
using System.Collections.Generic;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.AI;

// Keeping away from the Heartscar (see AwakeningSystem), at the smart levels (Easy and up), with the Awakening on.
//
// - Digging: it digs nothing near the heart, where every block taken out wakes the dark power sooner (within
//   HeartReach columns of the column over it and no more than HeartHeight levels over it, with HeartMargin to spare):
//   no quarry is marked out there, and its diggers do not breach a rival's dam there.
// - Corrupted ground: once the dark power wakes, or in the warning before a stage, it keeps off the ground the
//   corruption reaches by then and ScarMargin columns round it (see KeepsOff): no building is laid out there, no tree
//   felled, no ruin searched, no site held, no lair hunted and no herd hunted. A ruin party or a garrison there breaks
//   off and comes home.
// - Its units on corrupted ground that stand about or work there are walked out of it (see LeaveScar), straight away
//   from the heart, to just past the ground it keeps off. Those fighting, routed or only passing through (a plain move
//   or a march) carry on, as do those striking a rival's great work and those retaking an ancient site: the match is
//   at stake there.
// The classic level does none of this.
public sealed partial class AiPlayer
{
    /// <summary>Columns round the corrupted ground (as far as it reaches by its next stage) it also keeps off.</summary>
    public const int ScarMargin = 4;
    /// <summary>Columns past <see cref="AwakeningSystem.HeartReach"/> it keeps its digging back from the heart.</summary>
    public const int HeartMargin = 2;
    /// <summary>Columns past the ground it keeps off that its units are walked out to.</summary>
    public const int ScarExit = 3;

    /// <summary>
    /// How far (columns from the column over the heart) the ground it keeps off reaches now: the corruption's reach,
    /// by its next stage in the warning before one (see <see cref="AwakeningSystem.WarnTicks"/>), with its ragged edge
    /// and <see cref="ScarMargin"/>; 0 while it keeps off nothing (the classic level, the Awakening off, or the dark
    /// power asleep and not about to wake).
    /// </summary>
    public int ScarReach(Simulation sim)
    {
        if (!Smart || !sim.Awakening) return 0;
        var dp = sim.DarkPower;
        int stage = dp.Stage;
        if (stage < AwakeningSystem.Stages && dp.TicksToNextStage <= AwakeningSystem.WarnTicks) stage++;
        if (stage == 0) return 0;
        return dp.ReachOf(stage) + AwakeningSystem.Ragged + ScarMargin;
    }

    /// <summary>True if it keeps off column (x, z): it is on or near the corrupted ground (see <see cref="ScarReach"/>).</summary>
    public bool KeepsOff(Simulation sim, int x, int z)
    {
        int reach = ScarReach(sim);
        if (reach == 0) return false;
        var dp = sim.DarkPower;
        return Dist2(x, z, dp.HeartX, dp.HeartZ) <= (long)reach * reach;
    }

    /// <summary>True if it keeps off any column of the box from (x1, z1) to (x2, z2).</summary>
    private bool KeepsOff(Simulation sim, int x1, int z1, int x2, int z2)
    {
        int reach = ScarReach(sim);
        if (reach == 0) return false;
        var dp = sim.DarkPower;
        return Dist2(Math.Clamp(dp.HeartX, x1, x2), Math.Clamp(dp.HeartZ, z1, z2), dp.HeartX, dp.HeartZ) <= (long)reach * reach;
    }

    /// <summary>
    /// True if digging the box from (x1, z1) to (x2, z2) down to level <paramref name="floor"/> could wake the dark
    /// power sooner (see <see cref="AwakeningSystem.HeartReach"/>), with <see cref="HeartMargin"/> to spare: the
    /// smart levels dig nothing there while the Awakening is on.
    /// </summary>
    public bool NearHeart(Simulation sim, int x1, int z1, int x2, int z2, int floor)
    {
        if (!Smart || !sim.Awakening) return false;
        var dp = sim.DarkPower;
        if (floor > dp.HeartY + AwakeningSystem.HeartHeight) return false;
        int reach = AwakeningSystem.HeartReach + HeartMargin;
        return Dist2(Math.Clamp(dp.HeartX, x1, x2), Math.Clamp(dp.HeartZ, z1, z2), dp.HeartX, dp.HeartZ) <= (long)reach * reach;
    }

    /// <summary>Units it walked off the corrupted ground, all told.</summary>
    public int LeftScar { get; private set; }

    /// <summary>Walks its units that stand about or work on corrupted ground out of it (see the top of this file).</summary>
    private void LeaveScar(Simulation sim, Action<Command> issue)
    {
        int reach = ScarReach(sim);
        if (reach == 0 || sim.DarkPower.Radius == 0) return;
        var dp = sim.DarkPower;
        var u = sim.Units;
        var keep = new List<int>(_strike);
        foreach (var g in _garrisons)
            if (g.Retake) keep.AddRange(g.Ids);
        // By where they go: a point straight out from the heart, past the ground it keeps off.
        var groups = new SortedDictionary<(int X, int Z), List<int>>();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || u.Hp[i] <= 0 || u.Subdued[i] || u.Rout[i] > 0) continue;
            if (u.Target[i] != 0 || u.TargetBuilding[i] != 0 || u.Order[i] == UnitOrder.Chase) continue; // fighting
            if (u.Order[i] != UnitOrder.Idle && u.Job[i] == UnitJob.None) continue; // passing through
            if (!dp.OnCorruptGround(i) || keep.Contains(u.Id[i])) continue;
            var to = OutOfScar(sim, u.X[i].FloorToInt(), u.Z[i].FloorToInt(), reach + ScarExit);
            if (!groups.TryGetValue(to, out var ids)) groups[to] = ids = new List<int>();
            ids.Add(u.Id[i]);
        }
        if (groups.Count == 0) return;
        int n = 0;
        foreach (var (to, ids) in groups)
        {
            var arr = ids.ToArray();
            issue(new MoveCommand(Player, arr, to.X, to.Z));
            n += arr.Length;
        }
        LeftScar += n;
        Note(sim, $"walks {n} off the corrupted ground");
    }

    /// <summary>
    /// The point <paramref name="dist"/> columns from the column over the heart, straight out through (x, z) (towards
    /// its town hall from right over the heart), kept on the map; rounded to 4 columns, so that units near each other
    /// go together.
    /// </summary>
    private (int X, int Z) OutOfScar(Simulation sim, int x, int z, int dist)
    {
        var dp = sim.DarkPower;
        var w = sim.World;
        long dx = x - dp.HeartX, dz = z - dp.HeartZ;
        if (dx == 0 && dz == 0 && sim.Economy.TownHall(Player) is { } hall)
        {
            var (hx, hz) = Middle(hall);
            (dx, dz) = (hx - dp.HeartX, hz - dp.HeartZ);
        }
        if (dx == 0 && dz == 0) dx = 1;
        long len = Math.Max(1, IntMath.ISqrt(dx * dx + dz * dz));
        int px = dp.HeartX + (int)(dx * dist / len), pz = dp.HeartZ + (int)(dz * dist / len);
        px = (px + 2) & ~3;
        pz = (pz + 2) & ~3;
        return (Math.Clamp(px, 1, w.SizeX - 2), Math.Clamp(pz, 1, w.SizeZ - 2));
    }
}
