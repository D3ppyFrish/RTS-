using System;
using System.Collections.Generic;
using Rts.Sim.Content;

namespace Rts.Sim.Units;

/// <summary>
/// The shield wall (issue #145): units whose player has the tech for it (<see cref="TechEffectType.ShieldWall"/>,
/// the Settlers' Drilled ranks for line infantry) lock shields facing one way (<see cref="ShieldWallCommand"/>).
/// In the wall a unit cannot walk; it fights what comes within its reach and never chases. Shots from the
/// front half deal only <see cref="ShieldWallRules.Front"/> percent of their damage; from the sides or the back
/// they deal it all, unless another unit of the same wall stands beside it on that side
/// (<see cref="ShieldWallRules.Cover"/>), so a line is stronger than a lone unit. Spells and siege are not
/// stopped. A rider striking the wall's front deals only <see cref="ShieldWallRules.Charge"/> percent, takes
/// <see cref="ShieldWallRules.Repel"/> percent of its blow itself and stands stopped dead for a moment. Any order
/// to go somewhere or fight lowers the wall, and the unit takes <see cref="ShieldWallRules.DropTicks"/> to do so
/// before it can walk. A unit that routs, boards a ship or loses the tech drops it at once.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Hits a shield wall took less from since the match began (shots from the front, riders thrown back).</summary>
    public long WallHits { get; private set; }

    private readonly List<int> _wallNear = new();

    /// <summary>True if unit i, of its type and its player's techs, can take up a shield wall.</summary>
    public bool CanShieldWall(int i) =>
        (uint)i < (uint)U.Count && U.Town[i] == 0 && _sim.Economy.CanShieldWall(U.Owner[i], Def(i));

    /// <summary>Raises or lowers the shield wall of the player's units that can take one up.</summary>
    internal void Apply(ShieldWallCommand c)
    {
        var u = U;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!c.Raise)
            {
                LowerWall(i);
                continue;
            }
            if (u.ShieldWall[i] || !CanShieldWall(i) || u.Aboard[i] != 0 || u.Rout[i] > 0 || u.Subdued[i]) continue;
            var (fx, fz) = WallFacing(i);
            u.ShieldWall[i] = true;
            u.WallX[i] = fx;
            u.WallZ[i] = fz;
            u.Stunned[i] = 0;
            u.ClearJob(i);
            u.ClearCombat(i);
            u.ClearFormation(i);
            Halt(i);
        }
    }

    /// <summary>
    /// Unit i lowers its shield wall, if it stands in one: it cannot walk for <see cref="ShieldWallRules.DropTicks"/>.
    /// Any order that sends it somewhere or into a fight does this first (see <see cref="Simulation"/>).
    /// </summary>
    internal void LowerWall(int i)
    {
        var u = U;
        if (!u.ShieldWall[i]) return;
        u.ShieldWall[i] = false;
        u.Stunned[i] = Math.Max(u.Stunned[i], Rules.Wall.DropTicks);
    }

    /// <summary>The units of an order that sends them somewhere or into a fight lower their shield walls.</summary>
    internal void LowerWalls(UnitsCommand c)
    {
        foreach (int i in _sim.OwnedUnits(c)) LowerWall(i);
    }

    /// <summary>True if unit i cannot walk this tick: it stands in a shield wall, or is lowering one or thrown back by one.</summary>
    public bool Rooted(int i) => U.ShieldWall[i] || U.Stunned[i] > 0 || U.Molting[i] > 0;

    /// <summary>
    /// Counts down the units' stunned ticks, and has a unit in a shield wall that routs, boards a ship, is beaten
    /// or can no longer take up a wall drop it at once.
    /// </summary>
    private void StepWalls()
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Stunned[i] > 0) u.Stunned[i]--;
            if (u.ShieldWall[i] && (u.Rout[i] > 0 || u.Aboard[i] != 0 || u.Subdued[i] || !CanShieldWall(i)))
                u.ShieldWall[i] = false;
        }
    }

    /// <summary>
    /// The way unit i faces when it raises its shield wall, as a unit vector: the way it faces now (see
    /// <see cref="Facing"/>), else towards the nearest enemy it can see, else along +Z.
    /// </summary>
    private (Fix64 X, Fix64 Z) WallFacing(int i)
    {
        var u = U;
        var (fx, fz) = Facing(i);
        if (fx == Fix64.Zero && fz == Fix64.Zero)
        {
            int t = Nearest(i, Def(i));
            if (t >= 0) (fx, fz) = (u.X[t] - u.X[i], u.Z[t] - u.Z[i]);
        }
        Fix64 len = Fix64.Sqrt(fx * fx + fz * fz);
        if (len == Fix64.Zero) return (Fix64.Zero, Fix64.One);
        return (fx / len, fz / len);
    }

    /// <summary>True if unit i stands in front of unit t's shield wall: on the front half, the way the wall faces.</summary>
    public bool InFrontOfWall(int i, int t)
    {
        var u = U;
        return u.WallX[t] * (u.X[i] - u.X[t]) + u.WallZ[t] * (u.Z[i] - u.Z[t]) > Fix64.Zero;
    }

    /// <summary>
    /// True if unit t's shield wall covers it against unit i: i is in front, or off to a side (more to the side
    /// than behind) where another unit of t's player in a shield wall stands beside t within
    /// <see cref="ShieldWallRules.Cover"/>.
    /// </summary>
    public bool WallCovers(int i, int t)
    {
        var u = U;
        Fix64 dx = u.X[i] - u.X[t], dz = u.Z[i] - u.Z[t];
        Fix64 along = u.WallX[t] * dx + u.WallZ[t] * dz;
        if (along > Fix64.Zero) return true;
        // Sideways: along the wall's line (its facing turned a quarter).
        Fix64 across = u.WallZ[t] * dx - u.WallX[t] * dz;
        if (Fix64.Abs(across) < Fix64.Abs(along)) return false; // from behind
        return WallBeside(t, across > Fix64.Zero);
    }

    /// <summary>True if another unit of t's player in a shield wall stands beside t on one side of its line.</summary>
    private bool WallBeside(int t, bool positive)
    {
        var u = U;
        Fix64 reach = Rules.Wall.Cover;
        if (reach <= Fix64.Zero) return false;
        Near(u.X[t], u.Y[t], u.Z[t], reach, 1, u.Owner[t], allies: true, _wallNear);
        foreach (int j in _wallNear)
        {
            if (j == t || !u.ShieldWall[j]) continue;
            Fix64 across = u.WallZ[t] * (u.X[j] - u.X[t]) - u.WallX[t] * (u.Z[j] - u.Z[t]);
            if (positive ? across > Fix64.Zero : across < Fix64.Zero) return true;
        }
        return false;
    }

    /// <summary>
    /// What unit i's hit on unit t deals after t's shield wall, out of <paramref name="damage"/>, and whether i is a
    /// rider thrown back onto the spears. A unit in no wall, and spells and siege, take it all.
    /// </summary>
    private (int Damage, bool Repelled) ThroughWall(int i, UnitDef def, int t, int damage)
    {
        if (!U.ShieldWall[t] || damage <= 0) return (damage, false);
        if (def.Role is UnitRole.Caster or UnitRole.Siege || def.Machine || def.Lobs) return (damage, false);
        var wall = Rules.Wall;
        if (def.IsRanged)
            return WallCovers(i, t) ? (Math.Max(1, damage * wall.Front / 100), false) : (damage, false);
        if (def.Role == UnitRole.Cavalry && InFrontOfWall(i, t))
            return (Math.Max(1, damage * wall.Charge / 100), true);
        return (damage, false);
    }

    /// <summary>
    /// Rider i struck the front of unit t's shield wall: it takes <see cref="ShieldWallRules.Repel"/> percent of its
    /// <paramref name="blow"/> itself (landing with this tick's other hits, t's) and stands stopped dead.
    /// </summary>
    private void Repel(int i, int t, int blow)
    {
        var u = U;
        var wall = Rules.Wall;
        int back = blow * wall.Repel / 100;
        if (back > 0)
        {
            var part = Location(t, i);
            _pending[i] += back;
            int w = i * Body.Parts + (int)part;
            u.Wounds[w] = (byte)Math.Min(255, u.Wounds[w] + back);
            if (_hitBy[i] == 0)
            {
                Fix64 dx = u.X[i] - u.X[t], dz = u.Z[i] - u.Z[t];
                _hitBy[i] = u.Id[t];
                _hitFrom[i] = dx * dx + dz * dz;
            }
        }
        u.Stunned[i] = Math.Max(u.Stunned[i], wall.StaggerTicks);
        u.Cooldown[i] = Math.Max(u.Cooldown[i], wall.StaggerTicks);
    }
}
