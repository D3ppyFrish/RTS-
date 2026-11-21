using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.Automation;

/// <summary>
/// Rebuild orders (<see cref="RebuildCommand"/>): a player who turns them on has each finished building of
/// theirs that is destroyed (knocked down, undermined or burned; not one that is captured) put back where it
/// stood. Automation remembers the ruin, and every <see cref="RebuildTicks"/> looks at it: while an enemy is
/// within <see cref="RebuildThreatRadius"/> columns it waits; while rubble lies on the footprint its
/// automatic workers who take building work clear it (up to <see cref="RuinClearers"/> at a time); then, once
/// the player can pay, it places the building again as a site (the same command a player gives), and the
/// automatic workers build it as they build any site. A wall, gate or tower comes back at its first tier. A
/// ruin that cannot be built on for <see cref="RebuildGiveUpTicks"/> (a crater where it stood, say), or whose
/// spot someone else built on, is forgotten.
///
/// The ruins are automation's own bookkeeping, like the rest of it: they only decide which commands it gives.
/// </summary>
public sealed partial class AutomationSystem
{
    /// <summary>Ticks between a player's looks at their ruins.</summary>
    public const int RebuildTicks = 2 * Tps;
    /// <summary>Columns from the middle of a ruin an enemy must be beyond before work on it starts.</summary>
    public const int RebuildThreatRadius = 12;
    /// <summary>Most automatic workers clearing one ruin's rubble at a time.</summary>
    public const int RuinClearers = 2;
    /// <summary>A ruin not rebuilt this long after it fell is forgotten.</summary>
    public const int RebuildGiveUpTicks = 10 * 60 * Tps;

    /// <summary>A destroyed building automation means to put back.</summary>
    public sealed class Ruin
    {
        public int Owner { get; init; }
        public BuildingDef Def { get; init; } = null!;
        public int X { get; init; }
        /// <summary>Level it stood on (for drawing; it is placed on the ground as it is then).</summary>
        public int Y { get; init; }
        public int Z { get; init; }
        /// <summary>Tick it fell.</summary>
        public long Since { get; init; }
        public int MaxX => X + Def.SizeX - 1;
        public int MaxZ => Z + Def.SizeZ - 1;
        public bool Covers(int x, int z) => x >= X && x <= MaxX && z >= Z && z <= MaxZ;
    }

    // In the order they fell.
    private readonly List<Ruin> _ruins = new();

    /// <summary>The ruins automation still means to rebuild, oldest first (the HUD marks them).</summary>
    public IReadOnlyList<Ruin> Ruins => _ruins;

    /// <summary>Turns the player's rebuild orders on or off; off forgets their ruins.</summary>
    internal void Apply(RebuildCommand c)
    {
        if (!Economy.HasPlayer(c.PlayerId)) return;
        Economy.Player(c.PlayerId).Rebuild = c.On;
        if (!c.On) _ruins.RemoveAll(r => r.Owner == c.PlayerId);
    }

    /// <summary>
    /// Building b is about to go (destroyed, not captured): if it was finished and its player has rebuild
    /// orders on, it is remembered as a ruin to rebuild. Bridges and a wall's facings are not.
    /// </summary>
    internal void Destroyed(Building b)
    {
        int p = b.Owner;
        if (p < 0 || p >= Economy.PlayerCount || !Economy.Player(p).Rebuild || !b.Active) return;
        var def = b.Def;
        while (def.UpgradeOf != null) def = def.UpgradeOf;
        if (def.IsBridge || def.FacingOf != null) return;
        _ruins.Add(new Ruin { Owner = p, Def = def, X = b.X, Y = b.Y, Z = b.Z, Since = _sim.Tick });
    }

    /// <summary>Each player's ruins, every <see cref="RebuildTicks"/>: forget, wait, or place the site again.</summary>
    private void StepRebuild()
    {
        if (_ruins.Count == 0) return;
        long tick = _sim.Tick;
        for (int k = 0; k < _ruins.Count; k++)
        {
            var r = _ruins[k];
            if ((tick + r.Owner * 5) % RebuildTicks != 0) continue;
            if (!Economy.Player(r.Owner).Rebuild || tick - r.Since > RebuildGiveUpTicks)
            {
                _ruins.RemoveAt(k--);
                continue;
            }
            if (Threatened(r) || HasRubble(r)) continue;
            // A great work stands under other ground: put back at its own level, not on top.
            int level = r.Def.Kind == BuildingKind.GreatWork ? r.Y : -1;
            switch (Economy.CanPlace(r.Owner, r.Def, r.X, r.Z, out _, level: level))
            {
                case PlaceResult.Ok:
                    Issue(new BuildCommand(r.Owner, Array.Empty<int>(), r.Def.Id, r.X, r.Z, level));
                    _ruins.RemoveAt(k--);
                    break;
                case PlaceResult.Crowded or PlaceResult.Locked or PlaceResult.OutOfBounds:
                    _ruins.RemoveAt(k--); // built over, or no longer theirs to build
                    break;
                // Cannot afford it yet, or the ground is not ready: wait.
            }
        }
    }

    /// <summary>True if a unit hostile to the ruin's owner is within <see cref="RebuildThreatRadius"/> columns of its middle.</summary>
    private bool Threatened(Ruin r)
    {
        var u = U;
        int cx = r.X + r.Def.SizeX / 2, cz = r.Z + r.Def.SizeZ / 2;
        const int r2 = RebuildThreatRadius * RebuildThreatRadius;
        for (int j = 0; j < u.Count; j++)
        {
            if (u.Owner[j] == r.Owner || u.Subdued[j] || (u.Town[j] != 0 && u.Owner[j] == Simulation.WildPlayer)) continue;
            int dx = u.X[j].FloorToInt() - cx, dz = u.Z[j].FloorToInt() - cz;
            if (dx * dx + dz * dz <= r2) return true;
        }
        return false;
    }

    /// <summary>True if any rubble lies on the ruin's footprint.</summary>
    private bool HasRubble(Ruin r)
    {
        var w = World;
        for (int z = r.Z; z <= r.MaxZ; z++)
        for (int x = r.X; x <= r.MaxX; x++)
        for (int y = 1; y < w.SizeY - 1; y++)
            if (BlockInfo.IsRubble(w.Get(x, y, z))) return true;
        return false;
    }

    /// <summary>
    /// The rubble block on the ruin's footprint a clearer standing at (fromX, fromZ) should take next: of each
    /// column's top rubble block that nobody is gathering and the worker does not keep away from, the highest,
    /// then the nearest. False if there is none.
    /// </summary>
    private bool PickRubble(Ruin r, int fromX, int fromZ, Misses? m, out int bx, out int by, out int bz)
    {
        var w = World;
        long tick = _sim.Tick;
        bx = by = bz = -1;
        long bestD = long.MaxValue;
        for (int z = r.Z; z <= r.MaxZ; z++)
        for (int x = r.X; x <= r.MaxX; x++)
        {
            if (KeptAway(m, x, z, tick)) continue;
            for (int y = w.SizeY - 2; y >= 1; y--)
            {
                if (!BlockInfo.IsRubble(w.Get(x, y, z))) continue;
                if (Economy.Claimed(x, y, z) || Avoided(Harvest.Key(w, x, y, z), tick)) break;
                long d = (long)(fromX - x) * (fromX - x) + (long)(fromZ - z) * (fromZ - z);
                if (y > by || (y == by && d < bestD))
                {
                    (bx, by, bz) = (x, y, z);
                    bestD = d;
                }
                break;
            }
        }
        return by >= 0;
    }

    /// <summary>
    /// Work clearing a ruin for idle automatic worker i (who takes building work): the rubble block it should
    /// gather on the nearest of its player's ruins that is safe and has fewer than <see cref="RuinClearers"/>
    /// workers on it. Null if there is none.
    /// </summary>
    private Command? ClearRuin(int i, out Assignment? assignment)
    {
        assignment = null;
        if (_ruins.Count == 0) return null;
        var u = U;
        int owner = u.Owner[i];
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        long tick = _sim.Tick;
        _misses.TryGetValue(u.Id[i], out var m);
        Ruin? best = null;
        long bestD = long.MaxValue;
        foreach (var r in _ruins)
        {
            if (r.Owner != owner) continue;
            long dx = r.X + r.Def.SizeX / 2 - x, dz = r.Z + r.Def.SizeZ / 2 - z;
            long d = dx * dx + dz * dz;
            if (d >= bestD || Clearers(r) >= RuinClearers || Threatened(r)) continue;
            if (!PickRubble(r, x, z, m, out _, out _, out _)) continue;
            best = r;
            bestD = d;
        }
        if (best == null || !PickRubble(best, x, z, m, out int bx, out int by, out int bz)) return null;
        assignment = new Assignment(tick, UnitJob.Gather, 0, bx, by, bz, World.Get(bx, by, bz));
        return new GatherCommand(owner, new[] { u.Id[i] }, bx, by, bz);
    }

    /// <summary>The player's units gathering on the ruin's footprint.</summary>
    private int Clearers(Ruin r)
    {
        var u = U;
        int n = 0;
        for (int j = 0; j < u.Count; j++)
            if (u.Owner[j] == r.Owner && u.Job[j] == UnitJob.Gather && r.Covers(u.JobX[j], u.JobZ[j])) n++;
        return n;
    }
}
