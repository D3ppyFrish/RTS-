using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.World;

namespace Rts.Sim.Units;

/// <summary>Why a shadow step can or cannot be made (see <see cref="CombatSystem.CanShadowStep(int, int, int)"/>).</summary>
public enum StepResult
{
    Ok,
    /// <summary>The unit cannot step through the shadows at all (not a caster of Clan Kage's with Shadow step).</summary>
    NoCaster,
    /// <summary>It stepped too lately, or is beaten, aboard a ship or in a pen.</summary>
    NotReady,
    /// <summary>Its player has too little mana.</summary>
    NoMana,
    /// <summary>It stands in the daylight: no shadow to step into.</summary>
    InTheLight,
    /// <summary>The spot is off the map or further than the range.</summary>
    TooFar,
    /// <summary>Its side has never seen the spot.</summary>
    Unseen,
    /// <summary>The spot lies in the daylight.</summary>
    LitThere,
    /// <summary>Nowhere to stand there: water, a building, a hazard, or no ground.</summary>
    NoGround,
}

/// <summary>
/// Shadow step, Clan Kage's (see "Clan Kage" in docs/rules.md): a shadow caster whose player has the tech for it
/// (<see cref="TechEffectType.ShadowStep"/>) takes itself and the units of the command standing within
/// <see cref="ShadowStepRules.Gather"/> columns of it (up to <see cref="ShadowStepRules.Carry"/> in all, nearest first)
/// through the shadows to a spot its side has seen, up to <see cref="ShadowStepRules.Range"/> columns off
/// (<see cref="ShadowStepCommand"/>). They come out of the shadows there at once, within
/// <see cref="ShadowStepRules.Spread"/> columns of it, with no orders. Both where the caster stands and the spot must be
/// in shadow (<see cref="Shadowed"/>): out of the daylight, or in the shadowed wilds. It costs
/// <see cref="ShadowStepRules.Mana"/> mana, and the caster waits <see cref="ShadowStepRules.WaitTicks"/> before the next.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Ticks a step's marks (where the units went into the shadows and came out) show, for the game to draw.</summary>
    public const int StepShowTicks = 2 * Simulation.TicksPerSecond;

    /// <summary>A shadow step made lately: whose, from where to where, how many went, and the ticks its marks still show.</summary>
    public readonly record struct ShadowStepMark(int Owner, int FromX, int FromY, int FromZ, int ToX, int ToY, int ToZ, int Count, int Left);

    private readonly List<ShadowStepMark> _steps = new();

    /// <summary>Shadow steps made lately, oldest first (for the game to draw).</summary>
    public IReadOnlyList<ShadowStepMark> ShadowSteps => _steps;

    /// <summary>Shadow steps made since the match began.</summary>
    public long StepsMade { get; private set; }

    /// <summary>Units taken through the shadows since the match began, the casters too.</summary>
    public long UnitsStepped { get; private set; }

    /// <summary>True if unit i, of its type and its player's techs, can step through the shadows at all (ready or not).</summary>
    public bool CanStep(int i) =>
        (uint)i < (uint)U.Count && U.Town[i] == 0 && U.Owner[i] < Simulation.WildPlayer && _sim.Economy.CanShadowStep(U.Owner[i], Def(i));

    /// <summary>True if unit i can step now: it can step at all, its last step is long enough ago, and it is free to.</summary>
    public bool ReadyToStep(int i)
    {
        var u = U;
        return CanStep(i) && u.StepWait[i] == 0 && !u.Subdued[i] && u.Aboard[i] == 0 && u.Penned[i] == 0 && u.Rout[i] == 0
            && u.RunStage[i] == 0 && u.Hp[i] > 0;
    }

    /// <summary>True if (x, y, z), feet at y, is in shadow: out of the daylight (<see cref="Conditions.InDaylight"/>), or in the shadowed wilds.</summary>
    public bool Shadowed(int x, int y, int z) => !_sim.Conditions.InDaylight(x, y, z) || _sim.Conditions.InGloom(x, z);

    /// <summary>
    /// Whether caster i can step through the shadows to column (x, z) now, and if not why; the level it would come out at
    /// there in <paramref name="y"/>.
    /// </summary>
    public StepResult CanShadowStep(int i, int x, int z, out int y)
    {
        y = -1;
        var u = U;
        if (!CanStep(i)) return StepResult.NoCaster;
        if (!ReadyToStep(i)) return StepResult.NotReady;
        int owner = u.Owner[i];
        var rules = Rules.ShadowStep;
        if (_sim.Economy.Player(owner).Mana < rules.Mana) return StepResult.NoMana;
        int cx = u.X[i].FloorToInt(), cz = u.Z[i].FloorToInt();
        if (!Shadowed(cx, u.Y[i], cz)) return StepResult.InTheLight;
        var w = _sim.World;
        long dx = x - cx, dz = z - cz;
        if (x < 0 || z < 0 || x >= w.SizeX || z >= w.SizeZ || dx * dx + dz * dz > (long)rules.Range * rules.Range)
            return StepResult.TooFar;
        if (_sim.Fog && _sim.Vision.At(owner, x, z) == Rts.Sim.Sight.Unseen) return StepResult.Unseen;
        y = StandAt(x, z);
        if (y < 0) return StepResult.NoGround;
        if (!Shadowed(x, y, z)) return StepResult.LitThere;
        return StepResult.Ok;
    }

    /// <summary>Whether caster i can step through the shadows to column (x, z) now, and if not why.</summary>
    public StepResult CanShadowStep(int i, int x, int z) => CanShadowStep(i, x, z, out _);

    /// <summary>The level a unit comes out of the shadows at in column (x, z), on the ground, or -1 if there is nowhere to stand.</summary>
    private int StandAt(int x, int z)
    {
        var w = _sim.World;
        if (!w.InBounds(x, 1, z)) return -1;
        int y = Walk.GroundLevel(w, x, z);
        if (y < 1 || w.Get(x, y, z) == BlockType.Water || HazardSystem.IsHazard(w, x, y, z)) return -1;
        if (_sim.Buildings.AtColumn(x, z) != null) return -1;
        return y;
    }

    /// <summary>
    /// The first ready caster among the units takes itself and the others standing round it through the shadows to the
    /// spot (see the top of this file).
    /// </summary>
    internal void Apply(ShadowStepCommand c)
    {
        var u = U;
        var rules = Rules.ShadowStep;
        var units = _sim.OwnedUnits(c);
        int caster = -1, y = -1;
        foreach (int i in units)
            if (ReadyToStep(i))
            {
                caster = i;
                break;
            }
        if (caster < 0 || CanShadowStep(caster, c.X, c.Z, out y) != StepResult.Ok) return;

        // Who goes: the caster, then the nearest of the others within reach of it (ties to the lowest id).
        Fix64 cx = u.X[caster], cz = u.Z[caster];
        long reach = (long)rules.Gather * rules.Gather;
        var near = new List<(long D, int Id, int I)>();
        foreach (int i in units)
        {
            if (i == caster || Def(i).IsBoat || Def(i).Machine || u.Burrowed[i] || u.Hp[i] <= 0) continue;
            if (Math.Abs(u.Y[i] - u.Y[caster]) > 2) continue;
            long dx = (u.X[i] - cx).FloorToInt(), dz = (u.Z[i] - cz).FloorToInt();
            long d = dx * dx + dz * dz;
            if (d <= reach) near.Add((d, u.Id[i], i));
        }
        near.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.Id.CompareTo(b.Id));
        var go = new List<int> { caster };
        foreach (var n in near)
        {
            if (go.Count >= rules.Carry) break;
            go.Add(n.I);
        }

        // Where they come out: the standing spots round the spot, in shadow, nearest first, one each while there are enough.
        var spots = new List<(int D, int X, int Z, int Y)> { (0, c.X, c.Z, y) };
        int r = rules.Spread;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx == 0 && dz == 0 || dx * dx + dz * dz > r * r) continue;
            int sx = c.X + dx, sz = c.Z + dz, sy = StandAt(sx, sz);
            if (sy < 0 || Math.Abs(sy - y) > 2 || !Shadowed(sx, sy, sz)) continue;
            spots.Add((dx * dx + dz * dz, sx, sz, sy));
        }
        spots.Sort();

        int fx = u.X[caster].FloorToInt(), fy = u.Y[caster], fz = u.Z[caster].FloorToInt();
        for (int k = 0; k < go.Count; k++)
        {
            int i = go[k];
            var s = spots[k % spots.Count];
            u.ClearFormation(i);
            u.ClearJob(i);
            u.ClearCombat(i);
            u.Order[i] = UnitOrder.Idle;
            u.Field[i] = 0;
            u.Blocked[i] = 0;
            u.X[i] = Fix64.FromInt(s.X) + Fix64.Half;
            u.Z[i] = Fix64.FromInt(s.Z) + Fix64.Half;
            u.Y[i] = s.Y;
        }
        _sim.Economy.SpendMana(c.PlayerId, rules.Mana);
        u.StepWait[caster] = Math.Max(1, rules.WaitTicks);
        _steps.Add(new ShadowStepMark(c.PlayerId, fx, fy, fz, c.X, y, c.Z, go.Count, StepShowTicks));
        StepsMade++;
        UnitsStepped += go.Count;
    }

    /// <summary>Each tick: the casters' wait counts down, and the marks of steps made fade.</summary>
    private void StepShadowSteps()
    {
        for (int k = _steps.Count - 1; k >= 0; k--)
        {
            var s = _steps[k];
            if (s.Left <= 1) _steps.RemoveAt(k);
            else _steps[k] = s with { Left = s.Left - 1 };
        }
        var u = U;
        for (int i = 0; i < u.Count; i++)
            if (u.StepWait[i] > 0) u.StepWait[i]--;
    }

    private void HashShadowSteps(Action<long> mix)
    {
        mix(_steps.Count);
        foreach (var s in _steps)
        {
            mix(s.Owner); mix(s.FromX); mix(s.FromY); mix(s.FromZ); mix(s.ToX); mix(s.ToY); mix(s.ToZ); mix(s.Count); mix(s.Left);
        }
        mix(StepsMade);
        mix(UnitsStepped);
    }
}
