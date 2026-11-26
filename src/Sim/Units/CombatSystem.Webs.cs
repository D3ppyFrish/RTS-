using System;

namespace Rts.Sim.Units;

/// <summary>
/// Webs and poison: the hills' giant spiders (<see cref="UnitDef.Web"/>) spit webs at whom they fight, and the bearer
/// of a relic of their silk (<see cref="Content.RelicDef.Web"/>) wraps whom its blows strike. A web spat from up to the
/// web's range (<see cref="WebDef.Range"/>), with a clear line, at a unit further off than the spider's bite and not
/// webbed already, every <see cref="WebDef.EveryTicks"/>, or a relic's blow that lands (not one a shield stops, nor a
/// blast's splash), webs the unit: for the web's time it walks at the web's pace (see <see cref="UnitMovement"/>), so it
/// cannot run from the fight. A fresh web starts the time again and the slower pace holds. Ships and machines are not
/// webbed, nor a unit that webs itself (a spider walks on silk), nor the units of a player with silk knives
/// (<see cref="Content.TechEffectType.CutWebs"/>), who cut themselves free.
///
/// A spider's bite that lands poisons the unit bitten (<see cref="UnitDef.Poison"/>): it loses the poison's hit points
/// every second for the poison's time, landing with the tick's hits like bleeding (and killing if bleeding does). A fresh
/// bite starts the time again. Ships, machines and units that poison themselves are not poisoned; a healer draws the
/// poison out when it stems bleeding.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Units webbed since the match began.</summary>
    public long Webs { get; private set; }
    /// <summary>Webs spat since the match began.</summary>
    public long Spat { get; private set; }
    /// <summary>Units poisoned since the match began.</summary>
    public long Poisonings { get; private set; }

    /// <summary>The web unit i's blows wrap whom they strike in: its relic's, else null (a spider spits its own, see <see cref="StepWebs"/>).</summary>
    public WebDef? WebOf(int i, UnitDef def) => def.Web is { Range.Raw: 0 } own ? own : _sim.RelicOf(i)?.Web;

    /// <summary>True if unit i is caught in a web now.</summary>
    public bool IsWebbed(int i) => (uint)i < (uint)U.Count && U.Webbed[i] > 0;

    /// <summary>True if unit i is poisoned now.</summary>
    public bool IsPoisoned(int i) => (uint)i < (uint)U.Count && U.Poisoned[i] > 0;

    /// <summary>Unit i's blow has landed on unit t: webs it, if i's blows web.</summary>
    private void Ensnare(int i, UnitDef def, int t)
    {
        if (WebOf(i, def) is { } web) Wrap(web, t);
    }

    /// <summary>True if unit t can be caught in a web.</summary>
    private bool Webbable(int t)
    {
        var target = Def(t);
        return !target.Machine && !target.IsBoat && target.Web == null && !_sim.Economy.CutsWebs(U.Owner[t]);
    }

    /// <summary>Unit t is caught in the web, if it can be.</summary>
    private void Wrap(WebDef web, int t)
    {
        if (!Webbable(t)) return;
        var u = U;
        u.WebPace[t] = u.Webbed[t] > 0 ? Math.Min(u.WebPace[t], web.Pace) : web.Pace;
        u.Webbed[t] = Math.Max(u.Webbed[t], web.Ticks);
        Webs++;
    }

    /// <summary>Unit i's blow has landed on unit t: poisons it, if i's blows poison and t can be poisoned.</summary>
    private void Envenom(int i, UnitDef def, int t)
    {
        if (def.Poison is not { } poison) return;
        var target = Def(t);
        if (target.Machine || target.IsBoat || target.Poison != null) return;
        var u = U;
        u.PoisonRate[t] = Math.Max(u.Poisoned[t] > 0 ? u.PoisonRate[t] : 0, poison.PerSecond);
        u.Poisoned[t] = Math.Max(u.Poisoned[t], poison.Ticks);
        u.Cutter[t] = u.Id[i]; // the poison's work is the biter's, as a bleeding wound is the cutter's
        Poisonings++;
    }

    /// <summary>
    /// Webs wear off a tick at a time; a unit that spits webs (<see cref="WebDef.Range"/>) and is ready spits one at the
    /// unit it is fighting if it is out of the spider's bite, within the web's range, in a clear line and not webbed yet.
    /// </summary>
    private void StepWebs()
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Webbed[i] > 0 && --u.Webbed[i] == 0) u.WebPace[i] = 0;
            if (u.SpitWait[i] > 0) { u.SpitWait[i]--; continue; }
            if (u.Target[i] == 0) continue;
            var def = Def(i);
            if (def.Web is not { } web || web.Range == Fix64.Zero || u.Rout[i] != 0 || u.Subdued[i]) continue;
            int t = u.IndexOf(u.Target[i]);
            if (t < 0 || u.Webbed[t] > 0 || u.Aboard[t] != 0 || !Hostile(u.Owner[i], u.Owner[t]) || !Webbable(t)) continue;
            Fix64 dx = u.X[t] - u.X[i], dz = u.Z[t] - u.Z[i];
            Fix64 d2 = dx * dx + dz * dz;
            if (d2 <= def.Range * def.Range || d2 > web.Range * web.Range || Math.Abs(u.Y[t] - u.Y[i]) > web.Range.FloorToInt()) continue;
            if (!ClearShot(i, t)) continue;
            Wrap(web, t);
            Spat++;
            u.SpitWait[i] = web.EveryTicks;
            _shots.Add(new Shot(_sim.Tick, u.Id[i], u.X[i], u.Y[i], u.Z[i], u.X[t], u.Y[t], u.Z[t], true, false, Web: true));
        }
    }

    /// <summary>Poison works a tick at a time, and once a second (spread over units by id) takes its hit points, landing with the tick's hits.</summary>
    private void StepPoison()
    {
        var u = U;
        long tick = _sim.Tick;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Poisoned[i] == 0) continue;
            if ((tick + u.Id[i]) % Tps == 0) _bled[i] += u.PoisonRate[i];
            if (--u.Poisoned[i] == 0) u.PoisonRate[i] = 0;
        }
    }
}
