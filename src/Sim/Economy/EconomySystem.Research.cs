using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>Why a player cannot start researching a tech.</summary>
public enum ResearchResult : byte
{
    Ok,
    /// <summary>The player already has it.</summary>
    Researched,
    /// <summary>Only another faction can research it.</summary>
    OtherFaction,
    /// <summary>The player lacks a tech it requires.</summary>
    NeedsTechs,
    /// <summary>The player is researching another tech (one at a time).</summary>
    Busy,
    /// <summary>The player has no finished town hall to research at.</summary>
    NoTownHall,
    CannotAfford,
    /// <summary>A discovery tech whose discovery the player has not made yet (see <see cref="TechDef.Discovery"/>).</summary>
    NotDiscovered,
}

/// <summary>
/// Techs: each player researches them one at a time at their town hall, paying when research starts.
/// A researched tech unlocks the units, buildings and techs that require it and applies its effects
/// (faster gathering, bigger loads, richer farms, more housing, tougher or faster units, roads).
/// </summary>
public sealed partial class EconomySystem
{
    private ContentDb Content => _sim.Content;

    /// <summary>
    /// The player's faction (see <see cref="Simulation.FactionOf"/>): the one chosen for them, else the factions in
    /// data order; wild monsters are the wild side (<see cref="ContentDb.Wild"/>).
    /// </summary>
    public FactionDef Faction(int player) => _sim.FactionOf(player);

    public bool HasTech(int player, TechDef tech) => Find(player)?.Researched[tech.Index] ?? false;

    /// <summary>The tech the player is researching, or null.</summary>
    public TechDef? Researching(int player) => Find(player) is { Researching: >= 0 } p ? Content.Techs[p.Researching] : null;

    /// <summary>Ticks of research done on <see cref="Researching"/>.</summary>
    public int ResearchTicks(int player) => Find(player)?.ResearchTicks ?? 0;

    /// <summary>True if the player may train this unit: it is not another faction's, and they have the techs it needs.</summary>
    public bool Unlocked(int player, UnitDef def) => !Faction(player).Replaces(def) && Unlocked(player, def.Requires, def.Faction);

    /// <summary>
    /// True if the player may build this from the build menu: it is not another faction's, a city-state's totem
    /// or built only on a den, and they have the techs it needs.
    /// </summary>
    public bool Unlocked(int player, BuildingDef def) =>
        def.Kind != BuildingKind.Totem && !def.FromDen && !Faction(player).Replaces(def) && Unlocked(player, def.Requires, def.Faction);

    /// <summary>The first tech the player still needs for something, or null if they need none.</summary>
    public TechDef? MissingTech(int player, IReadOnlyList<TechDef> requires)
    {
        foreach (var t in requires)
            if (!HasTech(player, t)) return t;
        return null;
    }

    private bool Unlocked(int player, IReadOnlyList<TechDef> requires, FactionDef? faction) =>
        (faction == null || faction == Faction(player)) && MissingTech(player, requires) == null;

    /// <summary>True if the player can lay roads: they have a tech that allows it, or no tech is needed.</summary>
    public bool CanLayRoads(int player) => !Content.RoadsNeedTech || (Find(player)?.Roads ?? false);

    /// <summary>Whether the player can start researching the tech now (checking the cost too, unless told not to).</summary>
    public ResearchResult CanResearch(int player, TechDef tech, bool checkCost = true)
    {
        if (HasTech(player, tech)) return ResearchResult.Researched;
        if (tech.Faction != null && tech.Faction != Faction(player)) return ResearchResult.OtherFaction;
        if (!Discovered(player, tech.Discovery)) return ResearchResult.NotDiscovered;
        if (MissingTech(player, tech.Requires) != null) return ResearchResult.NeedsTechs;
        if (Researching(player) != null) return ResearchResult.Busy;
        if (TownHall(player) == null) return ResearchResult.NoTownHall;
        if (checkCost && !CanAfford(player, tech.Cost)) return ResearchResult.CannotAfford;
        return ResearchResult.Ok;
    }

    /// <summary>True if the player has made the discovery (always true for <see cref="Discovery.None"/>).</summary>
    public bool Discovered(int player, Discovery d) =>
        d == Discovery.None || ((Find(player)?.Discoveries ?? 0) & (1 << (int)d)) != 0;

    /// <summary>
    /// The player made the discovery (searched a ruin, cleared a lair, took a town): the discovery techs it opens
    /// can be researched from now on, and the player hears of each one their faction can research. Once a match.
    /// </summary>
    public void Discover(int player, Discovery d)
    {
        if (d == Discovery.None || player < 0 || player >= Simulation.WildPlayer || Discovered(player, d)) return;
        Player(player).Discoveries |= (byte)(1 << (int)d);
        foreach (var t in Content.Techs)
            if (t.Discovery == d && (t.Faction == null || t.Faction == Faction(player)) && !HasTech(player, t))
                _sim.Events.TechDiscovered(player, t.Index);
    }

    /// <summary>True if wild monsters leave the player's workers alone (a <see cref="TechEffectType.SpareWorkers"/> tech).</summary>
    public bool SparesWorkers(int player) => Find(player)?.SpareWorkers ?? false;

    /// <summary>True if the player's units of this type can take up a shield wall (a <see cref="TechEffectType.ShieldWall"/> tech).</summary>
    public bool CanShieldWall(int player, UnitDef def) => Find(player)?.ShieldWall[def.Index] ?? false;

    /// <summary>The player's first finished town hall, where research happens; null if they have none.</summary>
    public Building? TownHall(int player)
    {
        foreach (var b in Buildings.All)
            if (b.Owner == player && b.Active && b.Def.Kind == BuildingKind.TownHall) return b;
        return null;
    }

    /// <summary>Gives the player a tech at once, with its effects (match setup and tests).</summary>
    public void Grant(int player, TechDef tech) => Complete(player, tech);

    internal void Apply(ResearchCommand c)
    {
        if (!Content.TryGetTech(c.Tech, out var tech) || CanResearch(c.PlayerId, tech) != ResearchResult.Ok) return;
        var hall = TownHall(c.PlayerId)!;
        if (!TrySpend(c.PlayerId, tech.Cost, hall.X + hall.Def.SizeX / 2, hall.Z + hall.Def.SizeZ / 2)) return;
        var p = Player(c.PlayerId);
        p.Researching = tech.Index;
        p.ResearchTicks = 0;
    }

    /// <summary>Research goes on while the player has a finished town hall, and pauses while they have none.</summary>
    private void StepResearch()
    {
        for (int player = 0; player < _players.Count; player++)
        {
            var p = _players[player];
            if (p.Researching < 0 || TownHall(player) == null) continue;
            var tech = Content.Techs[p.Researching];
            if (++p.ResearchTicks < tech.ResearchTicks) continue;
            Complete(player, tech);
            _sim.Events.ResearchDone(player, tech.Index);
        }
    }

    private void Complete(int player, TechDef tech)
    {
        var p = Player(player);
        if (p.Researched[tech.Index]) return;
        p.Researched[tech.Index] = true;
        if (p.Researching == tech.Index)
        {
            p.Researching = -1;
            p.ResearchTicks = 0;
        }
        UpdateBonuses(p);
        foreach (var e in tech.Effects)
            if (e.Type == TechEffectType.Reveal) Reveal(player, e.Reveal);
        // More hit points reach the units already alive as well as those trained later.
        foreach (var e in tech.Effects)
        {
            if (e.Type != TechEffectType.UnitHp) continue;
            for (int i = 0; i < U.Count; i++)
                if (U.Owner[i] == player && e.AppliesTo(Content.Units[U.Def[i]])) U.Hp[i] += e.Amount;
        }
    }

    /// <summary>Recomputes the player's bonuses from the techs they have, in data order.</summary>
    private void UpdateBonuses(PlayerEconomy p)
    {
        var units = Content.Units;
        p.GatherPercent = p.CarryBonus = p.FarmBonus = 0;
        p.Roads = p.SpareWorkers = false;
        Array.Clear(p.HousingBonus);
        Array.Clear(p.HpBonus);
        Array.Clear(p.ShieldWall);
        var speedPercent = new int[units.Count];
        foreach (var t in Content.Techs)
        {
            if (!p.Researched[t.Index]) continue;
            foreach (var e in t.Effects)
            {
                switch (e.Type)
                {
                    case TechEffectType.GatherSpeed: p.GatherPercent += e.Amount; break;
                    case TechEffectType.Carry: p.CarryBonus += e.Amount; break;
                    case TechEffectType.FarmYield: p.FarmBonus += e.Amount; break;
                    case TechEffectType.Housing: p.HousingBonus[e.Building!.Index] += e.Amount; break;
                    case TechEffectType.Roads: p.Roads = true; break;
                    case TechEffectType.SpareWorkers: p.SpareWorkers = true; break;
                    case TechEffectType.UnitHp:
                        foreach (var d in units)
                            if (e.AppliesTo(d)) p.HpBonus[d.Index] += e.Amount;
                        break;
                    case TechEffectType.ShieldWall:
                        foreach (var d in units)
                            if (e.AppliesTo(d)) p.ShieldWall[d.Index] = true;
                        break;
                    case TechEffectType.UnitSpeed:
                        foreach (var d in units)
                            if (e.AppliesTo(d)) speedPercent[d.Index] += e.Amount;
                        break;
                }
            }
        }
        foreach (var d in units)
            p.SpeedPerTick[d.Index] = d.SpeedPerTick * (100 + speedPercent[d.Index]) / 100;
    }

    /// <summary>How far round each ruin, lair or town a reveal effect shows the ground, in columns.</summary>
    public const int RevealRadius = 8;

    /// <summary>Puts the ground round every feature of that kind on the map in the player's fog as seen.</summary>
    private void Reveal(int player, MapFeature what)
    {
        var vision = _sim.Vision;
        switch (what)
        {
            case MapFeature.Ruins:
                foreach (var r in _sim.Ruins.Ruins) vision.Reveal(player, r.X, r.Z, RevealRadius);
                break;
            case MapFeature.Lairs:
                foreach (var l in _sim.Monsters.Lairs)
                    if (!l.Used) vision.Reveal(player, l.X, l.Z, RevealRadius);
                break;
            case MapFeature.Towns:
                foreach (var t in _sim.Cities.Towns) vision.Reveal(player, t.X, t.Z, RevealRadius);
                break;
        }
    }

    // --- Numbers that techs change -----------------------------------------------------------------

    /// <summary>Most goods one of the player's workers carries.</summary>
    public int Capacity(int player) => CarryCapacity + (Find(player)?.CarryBonus ?? 0);

    /// <summary>
    /// Most goods unit i carries on a supply run: a cart's load (<see cref="Units.UnitDef.Carry"/>, which techs do
    /// not change), otherwise what one of its player's workers carries.
    /// </summary>
    public int LoadOf(int i)
    {
        int carry = _sim.Content.Units[U.Def[i]].Carry;
        return carry > 0 ? carry : Capacity(U.Owner[i]);
    }

    /// <summary>Ticks of work for one of the player's workers to take one block of this kind.</summary>
    public int WorkTicks(int player, BlockType kind) =>
        Math.Max(1, Harvest.WorkTicks(kind) * 100 / (100 + (Find(player)?.GatherPercent ?? 0)));

    /// <summary>Ticks of one round of work at a quarry or mine of the player's, with their gathering techs.</summary>
    public int QuarryTicks(int player, BuildingDef def) =>
        Math.Max(1, def.WorkTicks * 100 / (100 + (Find(player)?.GatherPercent ?? 0)));

    /// <summary>Food from one harvest at one of the player's farms.</summary>
    public int FarmYieldOf(int player) => FarmYield + (Find(player)?.FarmBonus ?? 0);

    /// <summary>People the building houses, with its owner's techs.</summary>
    public int HousingOf(Building b) => b.Def.Housing + (Find(b.Owner)?.HousingBonus[b.Def.Index] ?? 0);

    /// <summary>Hit points a unit of this type has when the player trains it.</summary>
    public int MaxHp(int player, UnitDef def) => def.MaxHp + (Find(player)?.HpBonus[def.Index] ?? 0);

    /// <summary>Blocks per tick a unit of this type walks for the player (before roads).</summary>
    public Fix64 SpeedPerTick(int player, UnitDef def) => Find(player)?.SpeedPerTick[def.Index] ?? def.SpeedPerTick;

    /// <summary>The player's state if it exists; queries use this so that asking never creates (and so hashes) a player.</summary>
    internal PlayerEconomy? Find(int player) => player >= 0 && player < _players.Count ? _players[player] : null;
}
