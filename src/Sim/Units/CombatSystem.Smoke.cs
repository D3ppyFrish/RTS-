using System;
using System.Collections.Generic;
using Rts.Sim.Content;

namespace Rts.Sim.Units;

/// <summary>
/// Smoke bombs, Clan Kage's (see "Clan Kage" in docs/rules.md): a unit whose player has the tech for it
/// (<see cref="TechEffectType.SmokeBombs"/>) and is ready throws a bomb at its feet (<see cref="SmokeCommand"/>). For
/// <see cref="SmokeRules.Ticks"/> a cloud hangs there, <see cref="SmokeRules.Radius"/> columns round and
/// <see cref="SmokeRules.Height"/> levels up and down (<see cref="InSmoke"/>). Anyone in it, of any side:
/// <list type="bullet">
/// <item>is not seen through the fog of war by its enemies (<see cref="VisionSystem.SeesUnit"/>);</item>
/// <item>is not fought unit to unit: nobody picks it or keeps it as a target, even on an order to attack it;</item>
/// <item>fights no unit itself (<see cref="Veiled"/>), so it can be walked out of a losing fight.</item>
/// </list>
/// Buildings are fought as ever. The thrower waits <see cref="SmokeRules.WaitTicks"/> (from the throw) before it can
/// throw the next.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>A cloud of smoke: whose it is, where it was thrown, and the ticks it has left.</summary>
    public readonly record struct SmokeCloud(int Owner, Fix64 X, int Y, Fix64 Z, int Left, long Thrown);

    // The clouds hanging about now, in the order they were thrown.
    private readonly List<SmokeCloud> _clouds = new();

    /// <summary>The clouds of smoke hanging about now, oldest first (for the game to draw).</summary>
    public IReadOnlyList<SmokeCloud> Clouds => _clouds;

    /// <summary>Smoke bombs thrown since the match began.</summary>
    public long SmokeBombs { get; private set; }

    /// <summary>True if unit i, of its type and its player's techs, can throw smoke bombs at all (ready or not).</summary>
    public bool CanSmoke(int i) =>
        (uint)i < (uint)U.Count && U.Town[i] == 0 && U.Owner[i] < Simulation.WildPlayer && _sim.Economy.CanSmoke(U.Owner[i], Def(i));

    /// <summary>
    /// True if unit i can throw a smoke bomb now: it can throw them, its last is long enough ago, and it is not beaten,
    /// aboard a ship, kept in a pen or dead. Running away it can: that is what the smoke is for.
    /// </summary>
    public bool ReadyToSmoke(int i)
    {
        var u = U;
        return CanSmoke(i) && u.SmokeWait[i] == 0 && !u.Subdued[i] && u.Aboard[i] == 0 && u.Penned[i] == 0 && u.Hp[i] > 0;
    }

    /// <summary>True if a cloud of smoke hangs over unit i now (see the top of this file).</summary>
    public bool InSmoke(int i)
    {
        if (_clouds.Count == 0 || (uint)i >= (uint)U.Count || U.Aboard[i] != 0) return false;
        return SmokeAt(U.X[i], U.Y[i], U.Z[i], -1, 0) >= 0;
    }

    /// <summary>
    /// The first cloud (index in <see cref="Clouds"/>) over the point, of <paramref name="owner"/>'s thrown on tick
    /// <paramref name="thrown"/> only when <paramref name="owner"/> is not -1; -1 if none.
    /// </summary>
    private int SmokeAt(Fix64 x, int y, Fix64 z, int owner, long thrown)
    {
        var rules = Rules.Smoke;
        Fix64 r = Fix64.FromInt(rules.Radius), r2 = r * r;
        for (int c = 0; c < _clouds.Count; c++)
        {
            var s = _clouds[c];
            if (owner >= 0 && (s.Owner != owner || s.Thrown != thrown)) continue;
            if (Math.Abs(y - s.Y) > rules.Height) continue;
            Fix64 dx = x - s.X, dz = z - s.Z;
            if (Fix64.Abs(dx) > r || Fix64.Abs(dz) > r) continue;
            if (dx * dx + dz * dz <= r2) return c;
        }
        return -1;
    }

    /// <summary>True if units i and t cannot fight each other because smoke hangs over either of them.</summary>
    public bool Veiled(int i, int t) => _clouds.Count > 0 && (InSmoke(i) || InSmoke(t));

    /// <summary>The ready throwers among the units throw a smoke bomb at their feet, one to a cloud's worth of them.</summary>
    internal void Apply(SmokeCommand c)
    {
        var u = U;
        var rules = Rules.Smoke;
        long tick = _sim.Tick;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!ReadyToSmoke(i)) continue;
            // Already in a cloud of its side thrown just now: it keeps its bomb for later.
            if (SmokeAt(u.X[i], u.Y[i], u.Z[i], u.Owner[i], tick) >= 0) continue;
            _clouds.Add(new SmokeCloud(u.Owner[i], u.X[i], u.Y[i], u.Z[i], Math.Max(1, rules.Ticks), tick));
            u.SmokeWait[i] = rules.WaitTicks;
            SmokeBombs++;
        }
    }

    /// <summary>Each tick, before the fighting: the clouds thin out, and the throwers' wait counts down.</summary>
    private void StepSmoke()
    {
        for (int c = _clouds.Count - 1; c >= 0; c--)
        {
            var s = _clouds[c];
            if (s.Left <= 1) _clouds.RemoveAt(c);
            else _clouds[c] = s with { Left = s.Left - 1 };
        }
        var u = U;
        for (int i = 0; i < u.Count; i++)
            if (u.SmokeWait[i] > 0) u.SmokeWait[i]--;
    }

    private void HashSmoke(Action<long> mix)
    {
        mix(_clouds.Count);
        foreach (var s in _clouds)
        {
            mix(s.Owner); mix(s.X.Raw); mix(s.Y); mix(s.Z.Raw); mix(s.Left); mix(s.Thrown);
        }
        mix(SmokeBombs);
    }
}
