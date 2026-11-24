using System;
using Rts.Sim.Content;

namespace Rts.Sim.Units;

/// <summary>
/// Molting (issue #153): units whose player has the tech for it (<see cref="TechEffectType.Molt"/>, the Deepfolk's
/// Molting) and are badly hurt (at most <see cref="MoltRules.Below"/> percent of their hit points left) can shed their
/// shells (<see cref="MoltCommand"/>). A molting unit stands where it is and does not fight for
/// <see cref="MoltRules.Ticks"/>, healing back to full a little each tick; then it stays soft for
/// <see cref="MoltRules.SoftTicks"/>, walking at <see cref="MoltRules.Pace"/> percent. While molting and soft it has no
/// armour and every hit on it deals <see cref="MoltRules.Tender"/> percent. A hurt unit that can molt, left idle
/// out of the fight for the recovery delay, molts by itself. Orders given while it molts wait until it is done.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Molts begun since the match began.</summary>
    public long Molts { get; private set; }

    /// <summary>True if unit i, of its type and its player's techs, can molt at all (hurt or not).</summary>
    public bool CanMolt(int i) =>
        (uint)i < (uint)U.Count && U.Town[i] == 0 && _sim.Economy.CanMolt(U.Owner[i], Def(i));

    /// <summary>True if unit i can molt now: it can molt, is hurt badly enough, and is not molting, soft, beaten, running or aboard.</summary>
    public bool ReadyToMolt(int i)
    {
        var u = U;
        if (!CanMolt(i) || u.Molting[i] > 0 || u.Soft[i] > 0 || u.Subdued[i] || u.Rout[i] > 0 || u.Aboard[i] != 0) return false;
        if (u.Penned[i] != 0 || u.RunStage[i] != 0) return false;
        return u.Hp[i] * 100 <= _sim.MaxHpOf(i) * Rules.Molt.Below;
    }

    /// <summary>True if unit i is shedding its shell: standing still, not fighting, healing.</summary>
    public bool Molting(int i) => (uint)i < (uint)U.Count && U.Molting[i] > 0;

    /// <summary>True if unit i has no shell to speak of: molting, or soft after it.</summary>
    public bool Shelled(int i) => (uint)i < (uint)U.Count && U.Molting[i] == 0 && U.Soft[i] == 0;

    /// <summary>The badly hurt units among those named that can molt begin to.</summary>
    internal void Apply(MoltCommand c)
    {
        foreach (int i in _sim.OwnedUnits(c))
            if (ReadyToMolt(i)) BeginMolt(i);
    }

    private void BeginMolt(int i)
    {
        var u = U;
        u.Molting[i] = Math.Max(1, Rules.Molt.Ticks);
        u.ShieldWall[i] = false;
        u.ClearJob(i);
        u.ClearCombat(i);
        u.ClearFormation(i);
        Halt(i);
        Molts++;
    }

    /// <summary>
    /// Each tick: molting units heal toward full (the rest of the way at the last tick), then turn soft; soft ones count
    /// down. Once a second each, a hurt unit that can molt and stands idle out of the fight molts by itself.
    /// </summary>
    private void StepMolts()
    {
        var u = U;
        long tick = _sim.Tick;
        var rules = Rules;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Molting[i] > 0)
            {
                int max = _sim.MaxHpOf(i);
                int left = u.Molting[i], missing = Math.Max(0, max - u.Hp[i]);
                // An even share of what is missing each tick, in hundredths of a hit point (as recovery keeps them);
                // the rest at the last tick.
                int gain;
                if (left == 1)
                {
                    gain = missing;
                    u.Mending[i] = Fix64.Zero;
                }
                else
                {
                    var mending = u.Mending[i] + Fix64.FromInt(missing * 100) / left;
                    gain = Math.Min(missing, (mending / 100).FloorToInt());
                    u.Mending[i] = mending - Fix64.FromInt(gain * 100);
                }
                if (gain > 0)
                {
                    u.Hp[i] += gain;
                    Mend(i, gain, u.Hp[i] >= max);
                }
                if (--u.Molting[i] == 0) u.Soft[i] = rules.Molt.SoftTicks;
                continue;
            }
            if (u.Soft[i] > 0) u.Soft[i]--;
            if ((tick + u.Id[i]) % Tps != 0) continue;
            if (u.Order[i] != UnitOrder.Idle || u.Target[i] != 0 || u.TargetBuilding[i] != 0 || u.Job[i] != UnitJob.None) continue;
            if (tick - u.LastFight[i] < rules.RecoveryDelayTicks) continue;
            if (ReadyToMolt(i)) BeginMolt(i);
        }
    }

    /// <summary>Unit i's armour on the part, unless it is molting or soft: then none.</summary>
    private int ShellArmour(int i, BodyPart part) =>
        Shelled(i) ? Def(i).ArmourOn(part) + U.Extra(i, part) + (_sim.RelicOf(i)?.Armour ?? 0) : 0;

    /// <summary>A hit's damage on unit t, after its soft body: more while it molts or is soft.</summary>
    private int Tender(int t, int damage) => Shelled(t) ? damage : damage * Rules.Molt.Tender / 100;

    /// <summary>The percent of its pace unit i walks at: less while soft after molting.</summary>
    public int MoltPace(int i) => (uint)i < (uint)U.Count && U.Soft[i] > 0 ? Rules.Molt.Pace : 100;
}
