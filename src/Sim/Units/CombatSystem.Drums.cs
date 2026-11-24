using System;
using System.Collections.Generic;

namespace Rts.Sim.Units;

/// <summary>
/// War drums (issue #151): the war drummer (<see cref="UnitDef.Drums"/>, the Orcs' once they have War drums).
/// <para>
/// Rally: a broken (routing) unit of the drummer's side within its earshot (its <see cref="UnitDef.Rally"/> range,
/// and <see cref="MoraleHeight"/> levels) runs to the drummer instead of home; once within <see cref="RallyReach"/>
/// columns of it, it stops running and rallies there, at <see cref="RallyMorale"/> at least. Near several drummers it
/// runs to the nearest. A drummer that is broken itself, beaten or aboard a ship drums for nobody.
/// </para>
/// <para>
/// Charge: a drummer that is ready beats the charge (<see cref="ChargeCommand"/>): for <see cref="DrumDef.ChargeTicks"/>
/// its side's units within earshot walk <see cref="DrumDef.Speed"/> percent faster (see <see cref="ChargeOf"/>), and the
/// first blow each of them strikes in that time deals <see cref="DrumDef.Blow"/> percent more. Then it waits
/// <see cref="DrumDef.WaitTicks"/> (counted from the start of the charge) before it can beat the next.
/// </para>
/// <para>
/// The enemy hears it: while drumming (beating the charge, or rallying a broken unit), it shows through the fog of war
/// to every rival with a unit or building within <see cref="DrumDef.Heard"/> columns (see <see cref="VisionSystem"/>).
/// </para>
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>A broken unit this near (blocks) the drummer it runs to stops running and rallies.</summary>
    public static readonly Fix64 RallyReach = Fix64.FromInt(2);

    // This tick's charges being beaten, with where each drummer stood: its side's units near it walk faster.
    private readonly List<(int Owner, Fix64 X, int Y, Fix64 Z, Fix64 Range, int Percent)> _charges = new();
    // The drummers (unit ids) drumming this tick, lowest id first, for the fog of war.
    private readonly List<int> _drumming = new();
    // This tick's drummers that drum (unit indexes), in unit order.
    private readonly List<int> _drummers = new();

    /// <summary>Unit ids of the war drummers drumming now (beating the charge or rallying broken units), lowest id first.</summary>
    public IReadOnlyList<int> Drumming => _drumming;

    /// <summary>True if unit i is a war drummer drumming now (see <see cref="Drumming"/>).</summary>
    public bool IsDrumming(int i) => (uint)i < (uint)U.Count && Def(i).Drums != null && _drumming.Contains(U.Id[i]);

    /// <summary>True if unit i is a war drummer that can beat the charge now.</summary>
    public bool CanCharge(int i)
    {
        var u = U;
        return (uint)i < (uint)u.Count && Def(i).Drums != null && u.DrumWait[i] == 0 && u.Rout[i] == 0 && !u.Subdued[i]
            && u.Aboard[i] == 0 && u.Hp[i] > 0;
    }

    /// <summary>The player's ready war drummers among the units beat the charge.</summary>
    internal void Apply(ChargeCommand c)
    {
        var u = U;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!CanCharge(i)) continue;
            var drums = Def(i).Drums!;
            u.DrumLeft[i] = drums.ChargeTicks;
            u.DrumWait[i] = drums.WaitTicks;
        }
    }

    /// <summary>
    /// Percent faster unit i walks this tick because a drummer of its side beating the charge stood within earshot of
    /// it when the tick began; the best of them, else 0. Machines and ships do not march to the drums.
    /// </summary>
    public int ChargeOf(int i)
    {
        if (_charges.Count == 0) return 0;
        var u = U;
        if ((uint)i >= (uint)u.Count) return 0;
        var def = Def(i);
        if (def.Machine || def.IsBoat) return 0;
        int best = 0;
        foreach (var s in _charges)
        {
            if (s.Owner != u.Owner[i] || s.Percent <= best || Math.Abs(u.Y[i] - s.Y) > MoraleHeight) continue;
            Fix64 dx = u.X[i] - s.X, dz = u.Z[i] - s.Z;
            if (Fix64.Abs(dx) > s.Range || Fix64.Abs(dz) > s.Range) continue;
            if (dx * dx + dz * dz <= s.Range * s.Range) best = s.Percent;
        }
        return best;
    }

    /// <summary>True if unit i's next blow deals more because a charge was beaten for it (see the top of this file).</summary>
    public bool Charged(int i) => (uint)i < (uint)U.Count && U.ChargeLeft[i] > 0;

    /// <summary>
    /// Once a tick, after the heroes' rally: the drummers' time counts down, the charges being beaten arm the units in
    /// earshot, and broken units in earshot of a drummer run to it or, once there, rally.
    /// </summary>
    private void StepDrums()
    {
        var u = U;
        _charges.Clear();
        _drumming.Clear();
        _drummers.Clear();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.ChargeLeft[i] > 0) u.ChargeLeft[i]--;
            if (Def(i).Drums == null) continue;
            if (u.DrumWait[i] > 0) u.DrumWait[i]--;
            if (u.DrumLeft[i] > 0) u.DrumLeft[i]--;
            if (u.Hp[i] <= 0 || u.Rout[i] > 0 || u.Subdued[i] || u.Aboard[i] != 0)
            {
                u.DrumLeft[i] = 0; // a broken drummer stops beating
                continue;
            }
            _drummers.Add(i);
        }
        if (_drummers.Count == 0) return;
        long tick = _sim.Tick;
        foreach (int h in _drummers)
        {
            if (u.DrumLeft[h] == 0) continue;
            var def = Def(h);
            _charges.Add((u.Owner[h], u.X[h], u.Y[h], u.Z[h], def.Rally, def.Drums!.Speed));
            _drumming.Add(u.Id[h]);
            // Each unit in earshot is armed once a charge: the first blow it strikes before the charge ends deals more.
            int mark = (int)(tick + u.DrumLeft[h]);
            Near(u.X[h], u.Y[h], u.Z[h], def.Rally, MoraleHeight, u.Owner[h], allies: true, _near);
            foreach (int j in _near)
            {
                var jd = Def(j);
                if (!jd.CanAttack || jd.Machine || jd.IsBoat || u.ChargeMark[j] == mark) continue;
                u.ChargeMark[j] = mark;
                u.ChargeLeft[j] = u.DrumLeft[h];
                u.ChargeBlow[j] = def.Drums.Blow;
            }
        }
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Rout[i] == 0 || u.Subdued[i] || u.Aboard[i] != 0) continue;
            int best = -1;
            Fix64 bestD = Fix64.MaxValue;
            foreach (int h in _drummers)
            {
                if (h == i || u.Owner[h] != u.Owner[i] || Math.Abs(u.Y[h] - u.Y[i]) > MoraleHeight) continue;
                var r = Def(h).Rally;
                Fix64 dx = u.X[h] - u.X[i], dz = u.Z[h] - u.Z[i];
                if (Fix64.Abs(dx) > r || Fix64.Abs(dz) > r) continue;
                Fix64 d = dx * dx + dz * dz;
                if (d > r * r || d >= bestD) continue;
                (best, bestD) = (h, d);
            }
            if (best < 0) continue;
            if (!_drumming.Contains(u.Id[best])) _drumming.Add(u.Id[best]);
            if (bestD <= RallyReach * RallyReach)
            {
                // At the drum: it stops running and rallies here.
                u.Rout[i] = 0;
                u.Morale[i] = Math.Max(u.Morale[i], RallyMorale);
                Halt(i);
                continue;
            }
            int hx = u.X[best].FloorToInt(), hz = u.Z[best].FloorToInt();
            if (u.Order[i] != UnitOrder.Move || Math.Abs(u.OrderX[i] - hx) + Math.Abs(u.OrderZ[i] - hz) > 2)
                _sim.SendUnit(i, hx, hz, u.Y[best]);
        }
        if (_drumming.Count > 1) _drumming.Sort();
    }
}
