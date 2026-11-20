using System;
using Rts.Sim.Content;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>
/// The Settlers' signature: units that earn experience and evolve. A unit whose faction has
/// <see cref="FactionDef.Experience"/> earns it by dealing damage to enemy units and buildings, by killing
/// enemy units, by building, and by staying alive. At each level (<see cref="ExperienceRules.Levels"/>) it
/// can take its next form: one of the unit types that evolve from its own (<see cref="UnitDef.Forms"/>),
/// picked by the player (<see cref="EvolveCommand"/>) or by automation, on a standing choice the player
/// made for that unit type (<see cref="AutoEvolveCommand"/>). A form is a unit type of its own with its own
/// numbers, and most simply grow stronger; some give something and take something: less speed, or a
/// weakness to one role's attacks.
///
/// Evolving is immediate and free. The unit keeps its id, place, orders, experience and the same share of
/// its hit points. A worker that becomes a soldier stops working: it drops its job and what it carries, and
/// is no longer automatic. Experience is never spent, so a form of a form needs only the next level.
/// </summary>
public sealed class EvolutionSystem
{
    /// <summary>The most experience a unit keeps.</summary>
    public const int MaxXp = 1_000_000;

    private readonly Simulation _sim;

    public EvolutionSystem(Simulation sim)
    {
        _sim = sim;
    }

    private UnitStore U => _sim.Units;
    private EconomySystem Economy => _sim.Economy;
    private UnitDef Def(int i) => _sim.Content.Units[U.Def[i]];

    /// <summary>How the player's units earn experience, or null if they earn none.</summary>
    public ExperienceRules? Rules(int player) => Economy.Faction(player).Experience;

    /// <summary>Unit i's level: 0 until it has the experience for the first.</summary>
    public int Level(int i) => Rules(U.Owner[i])?.Level(U.Xp[i]) ?? 0;

    /// <summary>The experience unit i needs for its next level, or -1 if there is no next level.</summary>
    public int NextLevelXp(int i)
    {
        if (Rules(U.Owner[i]) is not { } rules) return -1;
        int level = rules.Level(U.Xp[i]);
        return level < rules.Levels.Count ? rules.Levels[level] : -1;
    }

    /// <summary>
    /// True if unit i can take the form now: the form evolves from its type, it has a level for the step,
    /// its player may have the form (it is their faction's, and they have the techs it needs), and for a hero
    /// (<see cref="UnitDef.Hero"/>), its player has none of that hero already.
    /// </summary>
    public bool CanEvolve(int i, UnitDef form)
    {
        var def = Def(i);
        return form.EvolvesFrom == def && !form.FromDen && Level(i) > def.Tier && Economy.Unlocked(U.Owner[i], form)
               && !(form.Hero && HasHero(U.Owner[i], form));
    }

    /// <summary>
    /// True if unit i has the level for a hero form of its type but cannot take it only because its player
    /// has that hero already (for the game, to say why).
    /// </summary>
    public bool WaitsForHero(int i)
    {
        var def = Def(i);
        foreach (var form in def.Forms)
            if (form.Hero && Level(i) > def.Tier && Economy.Unlocked(U.Owner[i], form) && HasHero(U.Owner[i], form)) return true;
        return false;
    }

    /// <summary>True if the player has a living unit of this hero type, so none of theirs can become it now.</summary>
    public bool HasHero(int player, UnitDef hero)
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
            if (u.Def[i] == hero.Index && u.Owner[i] == player && u.Hp[i] > 0) return true;
        return false;
    }

    /// <summary>True if unit i can take one of its forms now.</summary>
    public bool CanEvolve(int i)
    {
        foreach (var form in Def(i).Forms)
            if (CanEvolve(i, form)) return true;
        return false;
    }

    /// <summary>The form the player's units of this type take on their own once they can, or null if the player picks.</summary>
    public UnitDef? AutoForm(int player, UnitDef from)
    {
        if (Economy.Find(player) is not { } p) return null;
        int form = p.AutoForm[from.Index];
        return form >= 0 ? _sim.Content.Units[form] : null;
    }

    /// <summary>True if some player has chosen a form for some unit type.</summary>
    internal bool AnyAutoForms()
    {
        for (int p = 0; p < Economy.PlayerCount; p++)
        {
            var player = Economy.Find(p);
            if (player == null) continue;
            foreach (int form in player.AutoForm)
                if (form >= 0) return true;
        }
        return false;
    }

    // --- Earning experience ------------------------------------------------------------------------

    /// <summary>Ticks in a minute: how often units earn experience for staying alive.</summary>
    public const int Minute = 60 * Simulation.TicksPerSecond;

    /// <summary>Once a minute, every unit that earns experience earns some for staying alive.</summary>
    public void Step()
    {
        long tick = _sim.Tick;
        if (tick == 0 || tick % Minute != 0) return;
        var u = U;
        for (int i = 0; i < u.Count; i++)
            if (Rules(u.Owner[i]) is { PerMinute: > 0 } rules) Add(i, rules.PerMinute);
    }

    /// <summary>Unit i dealt this much damage to an enemy unit or building.</summary>
    internal void Damaged(int i, int damage)
    {
        if (damage > 0 && Rules(U.Owner[i]) is { PerDamage: > 0 } rules) Add(i, damage * rules.PerDamage);
    }

    /// <summary>Unit i killed an enemy unit.</summary>
    internal void Killed(int i)
    {
        if (Rules(U.Owner[i]) is { PerKill: > 0 } rules) Add(i, rules.PerKill);
    }

    /// <summary>Unit i spent another second building.</summary>
    internal void Built(int i)
    {
        if (Rules(U.Owner[i]) is { PerBuildSecond: > 0 } rules) Add(i, rules.PerBuildSecond);
    }

    private void Add(int i, int xp) => U.Xp[i] = (int)Math.Min(MaxXp, (long)U.Xp[i] + xp);

    // --- Commands ----------------------------------------------------------------------------------

    /// <summary>The command's units that can take the form do.</summary>
    internal void Apply(EvolveCommand c)
    {
        if (!_sim.Content.TryGetUnit(c.Form, out var form)) return;
        foreach (int i in _sim.OwnedUnits(c))
            if (CanEvolve(i, form)) Evolve(i, form);
    }

    /// <summary>Sets (or clears) the form the player's units of a type take on their own.</summary>
    internal void Apply(AutoEvolveCommand c)
    {
        if (!_sim.Content.TryGetUnit(c.Unit, out var from) || !Economy.HasPlayer(c.PlayerId)) return;
        var player = Economy.Player(c.PlayerId);
        if (c.Form == null)
        {
            player.AutoForm[from.Index] = -1;
            return;
        }
        if (!_sim.Content.TryGetUnit(c.Form, out var form) || form.EvolvesFrom != from || form.FromDen || Rules(c.PlayerId) == null) return;
        if (form.Faction != null && form.Faction != Economy.Faction(c.PlayerId)) return;
        player.AutoForm[from.Index] = form.Index;
    }

    /// <summary>Unit i takes the form (see the class notes); also how a unit takes a den's form (see <see cref="MonsterSystem"/>).</summary>
    internal void Evolve(int i, UnitDef form)
    {
        var u = U;
        var old = Def(i);
        int owner = u.Owner[i];
        long oldMax = Math.Max(1, Economy.MaxHp(owner, old)), newMax = Math.Max(1, Economy.MaxHp(owner, form));
        u.Def[i] = form.Index;
        u.Hp[i] = (int)Math.Clamp(u.Hp[i] * newMax / oldMax, 1, newMax);
        if (old.Role != UnitRole.Worker || form.Role == UnitRole.Worker) return;
        if (u.Job[i] != UnitJob.None)
        {
            u.ClearJob(i);
            u.Order[i] = UnitOrder.Idle;
            u.Blocked[i] = 0;
            u.Field[i] = 0;
        }
        u.CarryAmount[i] = 0;
        u.Auto[i] = u.Held[i] = false;
    }
}
