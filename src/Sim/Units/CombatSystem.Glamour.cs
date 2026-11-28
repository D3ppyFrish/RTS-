using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.World;

namespace Rts.Sim.Units;

/// <summary>
/// Glamour, the Fey's (see "The Fey" in docs/rules.md): a caster whose player has the tech for it
/// (<see cref="TechEffectType.Glamour"/>) and is ready weaves <see cref="GlamourRules.Count"/> illusions round itself
/// (<see cref="GlamourCommand"/>), within <see cref="GlamourRules.Spread"/> columns of it, for
/// <see cref="GlamourRules.Mana"/> mana. An illusion (<see cref="UnitStore.Illusion"/>) is a unit of
/// <see cref="GlamourRules.Unit"/>'s type, its hit points full, that its side orders like any other and its enemies
/// cannot tell from the real thing: they see it, pick it and fight it as they would one. But:
/// <list type="bullet">
/// <item>its blows and shots deal nothing, to units or buildings (<see cref="Hit"/>);</item>
/// <item>the first blow, shot or splash that lands on it, or anything else that hurts it, ends it (<see cref="Wound"/>,
/// <see cref="Resolve"/>): it fades, and leaves no blood, no loss, no kill, no experience and no fear behind;</item>
/// <item>it fades by itself after <see cref="GlamourRules.Ticks"/>;</item>
/// <item>it takes no housing, works no job, captures and burns nothing, and picks nothing up.</item>
/// </list>
/// The caster waits <see cref="GlamourRules.WaitTicks"/> (from the weaving) before the next.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Illusions woven since the match began.</summary>
    public long IllusionsWoven { get; private set; }

    /// <summary>Illusions ended by a blow (or other harm) since the match began, not counting those that faded in time.</summary>
    public long IllusionsBroken { get; private set; }

    /// <summary>True if unit i is an illusion (see the top of this file).</summary>
    public bool IsIllusion(int i) => (uint)i < (uint)U.Count && U.Illusion[i] > 0;

    /// <summary>True if unit i, of its type and its player's techs, can weave illusions at all (ready or not).</summary>
    public bool CanGlamour(int i) =>
        (uint)i < (uint)U.Count && U.Town[i] == 0 && U.Owner[i] < Simulation.WildPlayer && U.Illusion[i] == 0
        && _sim.Economy.CanGlamour(U.Owner[i], Def(i));

    /// <summary>
    /// True if unit i can weave illusions now: it can at all, its last weaving is long enough ago, its player has the mana,
    /// and it is not beaten, running away, aboard a ship, in a pen or dead.
    /// </summary>
    public bool ReadyToGlamour(int i)
    {
        var u = U;
        return CanGlamour(i) && u.GlamourWait[i] == 0 && !u.Subdued[i] && u.Rout[i] == 0 && u.Aboard[i] == 0
            && u.Penned[i] == 0 && u.RunStage[i] == 0 && u.Hp[i] > 0
            && _sim.Economy.Player(u.Owner[i]).Mana >= Rules.Glamour.Mana;
    }

    /// <summary>Each ready caster among the units weaves its illusions round itself, while its player has the mana for it.</summary>
    internal void Apply(GlamourCommand c)
    {
        var u = U;
        var rules = Rules.Glamour;
        if (!_sim.Content.TryGetUnit(rules.Unit, out var look)) return;
        // Who casts, by id, before any illusion is added (adding units does not move the others, but keeps the list plain).
        var casters = new List<int>();
        foreach (int i in _sim.OwnedUnits(c))
            if (ReadyToGlamour(i)) casters.Add(u.Id[i]);
        foreach (int id in casters)
        {
            int i = u.IndexOf(id);
            if (i < 0 || !ReadyToGlamour(i)) continue; // the mana ran out on an earlier caster
            int owner = u.Owner[i];
            var spots = GlamourSpots(i, rules.Spread);
            if (spots.Count == 0) continue;
            for (int k = 0; k < rules.Count; k++)
            {
                var s = spots[k % spots.Count];
                int ill = u.IndexOf(u.Spawn(owner, look.Index, Fix64.FromInt(s.X) + Fix64.Half, s.Y, Fix64.FromInt(s.Z) + Fix64.Half,
                    _sim.Economy.MaxHp(owner, look)));
                u.Illusion[ill] = Math.Max(1, rules.Ticks);
                u.Stance[ill] = u.Stance[i];
                IllusionsWoven++;
            }
            _sim.Economy.SpendMana(owner, rules.Mana);
            u.GlamourWait[i] = Math.Max(1, rules.WaitTicks);
        }
    }

    /// <summary>
    /// The spots round caster i its illusions appear on: the standing columns within <paramref name="spread"/> of it, at
    /// most 2 levels off its own, the nearest first (ties in a fixed order), never its own column.
    /// </summary>
    private List<(int D, int X, int Z, int Y)> GlamourSpots(int i, int spread)
    {
        var u = U;
        var w = _sim.World;
        int cx = u.X[i].FloorToInt(), cz = u.Z[i].FloorToInt(), cy = u.Y[i];
        var spots = new List<(int D, int X, int Z, int Y)>();
        for (int dz = -spread; dz <= spread; dz++)
        for (int dx = -spread; dx <= spread; dx++)
        {
            if (dx == 0 && dz == 0 || dx * dx + dz * dz > spread * spread) continue;
            int x = cx + dx, z = cz + dz;
            if (!w.InBounds(x, 1, z) || _sim.Buildings.AtColumn(x, z) != null) continue;
            int y = Walk.NearestStandLevel(w, x, z, cy, 2);
            if (y < 1 || Math.Abs(y - cy) > 2 || w.Get(x, y, z) == BlockType.Water || HazardSystem.IsHazard(w, x, y, z)) continue;
            spots.Add((dx * dx + dz * dz, x, z, y));
        }
        spots.Sort();
        return spots;
    }

    /// <summary>Each tick, before the fighting: illusions whose time is up fade, and the casters' wait counts down.</summary>
    private void StepGlamour()
    {
        var u = U;
        for (int i = u.Count - 1; i >= 0; i--)
        {
            if (u.GlamourWait[i] > 0) u.GlamourWait[i]--;
            if (u.Illusion[i] > 0 && --u.Illusion[i] == 0) u.RemoveAt(i);
        }
    }

    private void HashGlamour(Action<long> mix)
    {
        mix(IllusionsWoven);
        mix(IllusionsBroken);
    }
}
