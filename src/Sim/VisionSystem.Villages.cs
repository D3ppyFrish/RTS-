using System;
using System.Collections.Generic;
using Rts.Sim.Economy;

namespace Rts.Sim;

/// <summary>
/// Clan Kage's hidden villages (see "Clan Kage" in docs/rules.md): with fog of war on, a rival sees a building of a
/// player whose faction has <see cref="Content.FactionDef.HiddenVillage"/> only once it has <em>found</em> it, by
/// having one of its units, or a building of its own, within that many columns of the building's footprint at a look.
/// Until then the building is not seen even where its columns are in sight (<see cref="HiddenFrom"/>): the world the
/// rival knows (<see cref="WorldMemory"/>) shows the ground without it, and its computer players know nothing of it.
/// Once found it stays found for that rival. Walls, gates, towers, bridges and great works are never hidden
/// (<see cref="Concealable"/>). Who has found what is history, so it is part of the state hash.
/// </summary>
public sealed partial class VisionSystem
{
    // Per player, the ids of other players' hidden buildings they have found (sorted, so the hash goes in order).
    private readonly SortedSet<int>?[] _found = new SortedSet<int>?[Simulation.WildPlayer];

    /// <summary>True if a building of this kind can be hidden: not a wall, gate, tower, bridge or great work.</summary>
    public static bool Concealable(BuildingDef def) => !def.IsRampart && !def.IsBridge && def.Kind != BuildingKind.GreatWork;

    /// <summary>Columns from building b within which its rivals find it (its owner's faction's hidden village), or 0 if it is not hidden.</summary>
    public int HiddenReach(Building b) =>
        (uint)b.Owner < (uint)Simulation.WildPlayer && Concealable(b.Def) ? _sim.FactionOf(b.Owner).HiddenVillage : 0;

    /// <summary>True if building b is hidden from those who have not found it: fog of war is on and it is of a hidden village.</summary>
    public bool Concealed(Building b) => _sim.Fog && HiddenReach(b) > 0;

    /// <summary>True if the player has found building <paramref name="id"/> of a hidden village.</summary>
    public bool Found(int player, int id) => (uint)player < (uint)_found.Length && _found[player] is { } f && f.Contains(id);

    /// <summary>
    /// True if <paramref name="player"/> does not see building b however near its columns are in sight: it is another
    /// player's, of a hidden village, and the player has not found it yet.
    /// </summary>
    public bool HiddenFrom(int player, Building b) => b.Owner != player && Concealed(b) && !Found(player, b.Id);

    /// <summary>True if column (x, z) is under a building hidden from the player (what <see cref="WorldMemory"/> keeps out of what it knows).</summary>
    public bool HiddenAt(int player, int x, int z) =>
        _sim.Fog && _sim.Buildings.AtColumn(x, z) is { } b && HiddenFrom(player, b);

    /// <summary>How many players have found building b (for its owner's panel).</summary>
    public int FoundBy(Building b)
    {
        int n = 0;
        for (int p = 0; p < _found.Length; p++)
            if (p != b.Owner && Found(p, b.Id)) n++;
        return n;
    }

    /// <summary>
    /// At each look: every player with a unit or a building of its own within a hidden building's reach of its
    /// footprint finds it. A plan is not found (others' plans are never seen).
    /// </summary>
    private void FindVillages()
    {
        var u = _sim.Units;
        var all = _sim.Buildings.All;
        foreach (var b in all)
        {
            int r = HiddenReach(b);
            if (r <= 0 || b.Planned) continue;
            for (int i = 0; i < u.Count; i++)
            {
                int p = u.Owner[i];
                if ((uint)p >= (uint)_found.Length || p == b.Owner || Found(p, b.Id)) continue;
                if (b.ColumnDistance(u.X[i].FloorToInt(), u.Z[i].FloorToInt()) <= r) Find(p, b.Id);
            }
            foreach (var o in all)
            {
                int p = o.Owner;
                if ((uint)p >= (uint)_found.Length || p == b.Owner || o.Planned || Found(p, b.Id)) continue;
                if (Math.Max(Gap(o.X, o.MaxX, b.X, b.MaxX), Gap(o.Z, o.MaxZ, b.Z, b.MaxZ)) <= r) Find(p, b.Id);
            }
        }
    }

    private void Find(int player, int id) => (_found[player] ??= new SortedSet<int>()).Add(id);

    // Columns between two stretches [a0, a1] and [b0, b1] along one axis: 0 where they overlap.
    private static int Gap(int a0, int a1, int b0, int b1) => b0 > a1 ? b0 - a1 : a0 > b1 ? a0 - b1 : 0;

    /// <summary>Folds who has found which hidden buildings into the state hash.</summary>
    private ulong HashFound(ulong h)
    {
        for (int p = 0; p < _found.Length; p++)
        {
            if (_found[p] is not { Count: > 0 } f) continue;
            h = (h ^ (ulong)(p + 1)) * 1099511628211UL;
            foreach (int id in f) h = (h ^ (ulong)(uint)id) * 1099511628211UL;
        }
        return h;
    }
}
