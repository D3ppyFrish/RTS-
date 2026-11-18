using System;
using Rts.Sim.Units;

namespace Rts.Sim.World;

/// <summary>
/// Natural hazards: ground that hurts the units on or near it (issue #45). Once a second (spread over units
/// by id) every unit standing on poison bog (<see cref="BlockType.Bog"/>) loses <see cref="BogDamage"/> hit
/// points, and every unit standing next to lava (within one column, at its feet's level or the one below)
/// loses <see cref="LavaDamage"/>. Both land with the tick's hits, by no one, so no one gets the kill and
/// armour does not help. Wild monsters are hurt the same. Digging the bog away (it is gathered like mud)
/// makes the ground safe. The numbers are first guesses for playtesting.
/// </summary>
public sealed class HazardSystem
{
    /// <summary>Hit points a second lost standing on poison bog.</summary>
    public const int BogDamage = 3;
    /// <summary>Hit points a second lost standing next to lava.</summary>
    public const int LavaDamage = 6;

    private readonly Simulation _sim;

    public HazardSystem(Simulation sim)
    {
        _sim = sim;
    }

    /// <summary>Hit points a second a unit with its feet at block (x, y, z) loses to the ground around it.</summary>
    public int DamageAt(int x, int y, int z)
    {
        var w = _sim.World;
        int damage = 0;
        if (w.Get(x, y - 1, z) == BlockType.Bog) damage += BogDamage;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            if (w.Get(x + dx, y, z + dz) == BlockType.Lava || w.Get(x + dx, y - 1, z + dz) == BlockType.Lava)
                return damage + LavaDamage;
        return damage;
    }

    /// <summary>Before the fighting: the units whose second it is take what the ground around them does to them.</summary>
    public void Step()
    {
        var u = _sim.Units;
        long tick = _sim.Tick;
        int tps = Simulation.TicksPerSecond;
        for (int i = 0; i < u.Count; i++)
        {
            if ((tick + u.Id[i]) % tps != 0 || u.Hp[i] <= 0) continue;
            int damage = DamageAt(u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt());
            if (damage > 0) _sim.Combat.Crush(i, damage);
        }
    }
}
