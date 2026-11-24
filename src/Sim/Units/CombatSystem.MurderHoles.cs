using System;
using System.Collections.Generic;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>
/// Murder holes (Masonry, issue #147): a gate with <see cref="BuildingDef.MurderHoles"/> whose owner has a
/// <see cref="Content.TechEffectType.MurderHoles"/> tech drops a stone through the holes in its roof every few
/// seconds while it is held: a unit of its owner stands on a wall, gate or tower within
/// <see cref="MurderHoles.Held"/> columns of it. The stone falls on the enemy nearest the gate that stands in
/// the gateway or on the ground up to <see cref="MurderHoles.Reach"/> columns out in front of it (straight out
/// across the line, and a column to each side of that), a battering ram as much as a soldier. It lands on the
/// head as a crushing blow, by no unit, so armour on the head counts and no shield stops it. An unheld gate
/// drops nothing, so an attacker clears the walls first.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Stones dropped through murder holes since the match began.</summary>
    public long StonesDropped { get; private set; }

    private readonly List<(int X, int Z)> _holeCells = new();

    /// <summary>Every second, each held gate with murder holes whose turn it is drops a stone (see the summary above).</summary>
    private void StepMurderHoles()
    {
        long tick = _sim.Tick;
        if (tick % Tps != 0) return;
        long second = tick / Tps;
        var econ = _sim.Economy;
        foreach (var b in Buildings.All)
        {
            var holes = b.Def.MurderHoles;
            if (holes == null || !b.Active || (second + b.Id) % holes.EverySeconds != 0 || !econ.HasMurderHoles(b.Owner)) continue;
            if (!Held(b, holes)) continue;
            int t = UnderHoles(b, holes);
            if (t >= 0) DropStone(b, holes, t);
        }
    }

    /// <summary>True if a unit of the gate's owner stands on a wall, gate or tower of theirs within <see cref="MurderHoles.Held"/> columns of it.</summary>
    public bool Held(Building gate, MurderHoles holes)
    {
        var u = U;
        Fix64 cx = Fix64.FromInt(gate.X) + Fix64.Half, cz = Fix64.FromInt(gate.Z) + Fix64.Half;
        Near(cx, gate.Y + gate.Shape.Height, cz, Fix64.FromInt(holes.Held + 1), 16, gate.Owner, allies: true, _near);
        foreach (int j in _near)
        {
            if (u.Subdued[j] || u.Aboard[j] != 0 || u.Hp[j] <= 0) continue;
            if (gate.ColumnDistance(u.X[j].FloorToInt(), u.Z[j].FloorToInt()) > holes.Held) continue;
            if (_sim.Economy.WallUnder(j) is { } on && on.Owner == gate.Owner) return true;
        }
        return false;
    }

    /// <summary>
    /// The columns the gate's holes drop stones on: the gate's own (the gateway), and out from it across its line
    /// (see <see cref="EconomySystem.Sides"/>) up to <see cref="MurderHoles.Reach"/> columns, a column wide to each side.
    /// </summary>
    public List<(int X, int Z)> HoleColumns(Building gate, MurderHoles holes)
    {
        _holeCells.Clear();
        _holeCells.Add((gate.X, gate.Z));
        foreach (var (sx, sz) in _sim.Economy.Sides(gate))
        {
            int dx = sx - gate.X, dz = sz - gate.Z;
            if (dx != 0 && dz != 0) continue; // the outside corner of a turn is not in front of the gate
            for (int k = 1; k <= holes.Reach; k++)
            for (int l = -1; l <= 1; l++)
            {
                // Across the way out: along Z if it runs along X, and the other way round.
                var c = (gate.X + dx * k + dz * l, gate.Z + dz * k + dx * l);
                if (!_holeCells.Contains(c)) _holeCells.Add(c);
            }
        }
        return _holeCells;
    }

    /// <summary>
    /// The enemy the gate's next stone falls on: of those on the ground in its <see cref="HoleColumns"/> (from a
    /// level under the gate's foot to two over it), the nearest to the gate, ties to the lowest id; -1 if none.
    /// </summary>
    public int UnderHoles(Building gate, MurderHoles holes)
    {
        var u = U;
        var cells = HoleColumns(gate, holes);
        Fix64 cx = Fix64.FromInt(gate.X) + Fix64.Half, cz = Fix64.FromInt(gate.Z) + Fix64.Half;
        // Near looks from the middle of the band: a level under the foot to two over it.
        Near(cx, gate.Y + 1, cz, Fix64.FromInt(holes.Reach + 2), 2, gate.Owner, allies: false, _near);
        int best = -1;
        Fix64 bestD = Fix64.Zero;
        foreach (int j in _near)
        {
            if (u.Subdued[j] || u.Aboard[j] != 0 || u.Hp[j] <= _pending[j] || u.Burrowed[j]) continue;
            if (!cells.Contains((u.X[j].FloorToInt(), u.Z[j].FloorToInt()))) continue;
            Fix64 dx = u.X[j] - cx, dz = u.Z[j] - cz, d = dx * dx + dz * dz;
            if (best < 0 || d < bestD || (d == bestD && u.Id[j] < u.Id[best]))
            {
                best = j;
                bestD = d;
            }
        }
        return best;
    }

    /// <summary>A stone through the gate's holes on unit t: a crushing blow on the head, by no unit.</summary>
    private void DropStone(Building gate, MurderHoles holes, int t)
    {
        var u = U;
        var part = BodyPart.Head;
        int damage = Body.Damage(holes.Damage, Def(t).Body, part, AttackKind.Crush, Armour(t, part));
        _pending[t] += damage;
        int w = t * Body.Parts + (int)part;
        u.Wounds[w] = (byte)Math.Min(255, u.Wounds[w] + damage);
        StonesDropped++;
        _shots.Add(new Shot(_sim.Tick, 0, u.X[t], gate.Y + gate.Shape.Height - 2, u.Z[t], u.X[t], u.Y[t], u.Z[t], true, false,
            part, damage, Dropped: true));
    }
}
