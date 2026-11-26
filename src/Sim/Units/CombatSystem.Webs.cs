using System;

namespace Rts.Sim.Units;

/// <summary>
/// Webs: the forest's giant spiders (<see cref="UnitDef.Web"/>) wrap whoever they bite in silk, and so does the bearer
/// of a relic of their silk (<see cref="Content.RelicDef.Web"/>). Each blow that lands (not one a shield stops, nor a
/// blast's splash) webs the unit struck: for the web's time it walks at the web's pace (see
/// <see cref="UnitMovement"/>), so it cannot run from the fight. A fresh web starts the time again and the slower
/// pace holds. Ships and machines are not webbed, nor a unit that webs itself (a spider walks on silk), nor the units
/// of a player with silk knives (<see cref="Content.TechEffectType.CutWebs"/>), who cut themselves free.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Units webbed since the match began.</summary>
    public long Webs { get; private set; }

    /// <summary>The web unit i's blows wrap whom they strike in: its own (a spider's) or its relic's, else null.</summary>
    public WebDef? WebOf(int i, UnitDef def) => def.Web ?? _sim.RelicOf(i)?.Web;

    /// <summary>True if unit i is caught in a web now.</summary>
    public bool IsWebbed(int i) => (uint)i < (uint)U.Count && U.Webbed[i] > 0;

    /// <summary>Unit i's blow has landed on unit t: webs it, if i's blows web and t can be webbed.</summary>
    private void Ensnare(int i, UnitDef def, int t)
    {
        if (WebOf(i, def) is not { } web) return;
        var target = Def(t);
        if (target.Machine || target.IsBoat || target.Web != null || _sim.Economy.CutsWebs(U.Owner[t])) return;
        var u = U;
        u.WebPace[t] = u.Webbed[t] > 0 ? Math.Min(u.WebPace[t], web.Pace) : web.Pace;
        u.Webbed[t] = Math.Max(u.Webbed[t], web.Ticks);
        Webs++;
    }

    /// <summary>Webs wear off a tick at a time.</summary>
    private void StepWebs()
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
            if (u.Webbed[i] > 0 && --u.Webbed[i] == 0) u.WebPace[i] = 0;
    }
}
