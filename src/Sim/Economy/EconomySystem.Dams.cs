using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Dams (<see cref="DamCommand"/>, <see cref="SluiceCommand"/>): workers and sappers build a wall of rammed earth
/// across water or a dug channel, from bank to bank, up to the level of the higher bank, so it holds the water back
/// and units walk across on top (see <see cref="Dam"/>). Each block costs <see cref="StonePerRamp"/> stone and
/// <see cref="RampBlockTicks"/> of work, as a ramp's does. The deepest column is a sluice of timber boards that the
/// dam's owner opens to let the water through, and shuts again.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Most columns a dam fills between its two banks.</summary>
    public const int MaxDam = 32;
    /// <summary>Most blocks one column of a dam takes (from the bed of the water up to its top).</summary>
    public const int MaxDamFill = 12;

    private readonly List<Dam> _dams = new();
    private readonly List<DamSpan> _damPlan = new();
    private int _nextDam = 1;

    /// <summary>The dams being built, and the finished ones, by id.</summary>
    public IReadOnlyList<Dam> Dams => _dams;

    public Dam? DamById(int id)
    {
        foreach (var d in _dams)
            if (d.Id == id) return d;
        return null;
    }

    /// <summary>The finished dam with a sluice that covers column (x, z), or null.</summary>
    public Dam? DamAt(int x, int z)
    {
        foreach (var d in _dams)
            if (d.Done && d.Covers(x, z)) return d;
        return null;
    }

    /// <summary>
    /// Plans a dam from bank column (x1, z1) to bank column (x2, z2) (the line <see cref="WallLine"/> gives) into
    /// <paramref name="spans"/>: both ends must be ground to stand on, at most a climb apart, and the dam's top runs
    /// level at the higher end's standing level. Each column in between is filled from its bed (the first solid
    /// block under it, below any water) up to a level under the top; ground already that high is left as it is, and
    /// left out of the spans. At most <see cref="MaxDam"/> columns and <see cref="MaxDamFill"/> blocks a column. The
    /// sluice is the column with the most blocks, nearest the middle (-1 if no column takes two or more). Returns the
    /// top level if the whole dam can go in (every column has room, and some column takes two blocks or more: there is
    /// water or a channel to hold), else -1; the spans are filled either way, for the game's preview.
    /// </summary>
    public int PlanDam(int x1, int z1, int x2, int z2, List<DamSpan> spans, out int sluice)
    {
        spans.Clear();
        sluice = -1;
        var w = World;
        if (!w.InBounds(x1, 1, z1) || !w.InBounds(x2, 1, z2)) return -1;
        int a = Walk.GroundLevel(w, x1, z1), b = Walk.GroundLevel(w, x2, z2);
        if (a < 0 || b < 0 || Math.Abs(a - b) > Walk.MaxClimb) return -1;
        int top = Math.Max(a, b);
        if (top + Walk.Clearance > w.SizeY - 1) return -1;
        var line = WallLine(x1, z1, x2, z2);
        bool fits = line.Count >= 3 && line.Count <= MaxDam + 2;
        for (int k = 1; k < line.Count - 1 && spans.Count < MaxDam; k++)
        {
            var (x, z) = line[k];
            int bed = top;
            while (bed > 1 && Ramp.Open(w, x, bed - 1, z)) bed--;
            if (bed == top) continue; // ground (or a trunk) at the dam's top already: nothing to fill
            bool room = top - bed <= MaxDamFill && DamRoom(x, z, top);
            spans.Add(new DamSpan(x, z, bed, top, room));
            fits &= room;
        }
        for (int k = 0; k < spans.Count; k++)
        {
            if (spans[k].Blocks < 2) continue;
            if (sluice < 0 || spans[k].Blocks > spans[sluice].Blocks
                || spans[k].Blocks == spans[sluice].Blocks
                   && Math.Abs(2 * k - (spans.Count - 1)) < Math.Abs(2 * sluice - (spans.Count - 1)))
                sluice = k;
        }
        return fits && sluice >= 0 ? top : -1;
    }

    /// <summary>True if column (x, z) can take a dam with its top at level <paramref name="top"/> (headroom over it).</summary>
    private bool DamRoom(int x, int z, int top)
    {
        var w = World;
        if (x < 1 || z < 1 || x > w.SizeX - 2 || z > w.SizeZ - 2) return false;
        if (Buildings.AtColumn(x, z) != null || _sim.Cities.Crowds(x, z, x, z)) return false;
        for (int y = top; y < top + Walk.Clearance; y++)
            if (w.Get(x, y, z) is var b && b != BlockType.Air && !BlockInfo.IsLeaves(b)) return false;
        return true;
    }

    internal void Apply(DamCommand c)
    {
        var units = OwnedUnits(c);
        units.RemoveAll(i => !CanDig(Content.Units[U.Def[i]]));
        if (units.Count == 0) return;
        if (PlanDam(c.X1, c.Z1, c.X2, c.Z2, _damPlan, out int sluice) < 0) return;
        foreach (var span in _damPlan)
            for (int y = span.Bed; y < span.Top + Walk.Clearance; y++)
                if (BlockInfo.IsLeaves(World.Get(span.X, y, span.Z))) World.Set(span.X, y, span.Z, BlockType.Air);
        var dam = new Dam(_nextDam++, c.PlayerId, new List<DamSpan>(_damPlan), sluice);
        _dams.Add(dam);
        foreach (int i in units)
        {
            StartJob(i, UnitJob.Dam, dam.Id);
            SetPhase(i, JobPhase.ToWork);
        }
    }

    /// <summary>
    /// Opens or shuts the sluice of the player's finished dam. Opening takes out its boards under the top one, so
    /// water on either side flows through; shutting puts boards back into every open block there, and is refused
    /// while anyone stands in the gap.
    /// </summary>
    internal void Apply(SluiceCommand c)
    {
        var dam = DamById(c.Dam);
        if (dam == null || dam.Owner != c.PlayerId || !dam.Done || dam.Open == c.Open) return;
        if (!dam.SluiceColumn(out var s)) return;
        var w = World;
        if (c.Open)
        {
            for (int y = s.Bed; y < s.Top - 1; y++)
                if (w.Get(s.X, y, s.Z) == BlockType.Planks) w.Set(s.X, y, s.Z, BlockType.Air);
            dam.Open = true;
            return;
        }
        for (int j = 0; j < U.Count; j++)
            if (U.X[j].FloorToInt() == s.X && U.Z[j].FloorToInt() == s.Z && U.Y[j] >= s.Bed - 1 && U.Y[j] < s.Top - 1)
                return; // someone is in the gap (or its head is)
        for (int y = s.Bed; y < s.Top - 1; y++)
            if (Ramp.Open(w, s.X, y, s.Z)) w.Set(s.X, y, s.Z, BlockType.Planks);
        dam.Open = false;
    }

    private void StepDam(int i)
    {
        var u = U;
        var dam = DamById(u.JobTarget[i]);
        if (dam == null || dam.Done)
        {
            EndJob(i);
            return;
        }
        if (u.Phase[i] == JobPhase.Wait)
        {
            if (--u.JobTimer[i] > 0) return;
            SetPhase(i, JobPhase.ToWork);
        }
        if (!dam.Next(World, out int x, out int y, out int z, out int column))
        {
            dam.Done = true;
            EndJob(i);
            return;
        }
        Heap(i, x, y, z, dam.BlockOf(column));
    }

    /// <summary>Drops the unfinished dams nobody builds any more (what was built stays). Once a second.</summary>
    private void PruneDams()
    {
        if (_dams.Count == 0 || _sim.Tick % Tps != 0) return;
        _dams.RemoveAll(d =>
        {
            if (d.Done) return false;
            for (int i = 0; i < U.Count; i++)
                if (U.Job[i] == UnitJob.Dam && U.JobTarget[i] == d.Id) return false;
            return true;
        });
    }

    private void MixDams(ref ulong h)
    {
        foreach (var d in _dams) d.Mix(ref h);
    }
}
