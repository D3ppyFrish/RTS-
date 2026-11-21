using System;
using System.Collections.Generic;
using Rts.Sim.Economy;

namespace Rts.Sim;

public enum GameEventKind : byte
{
    /// <summary>The player's units are being hit by another side's (at most once per place every few seconds).</summary>
    UnitsAttacked,
    /// <summary>The player's building is being hit, captured or burned by another side (throttled like <see cref="UnitsAttacked"/>).</summary>
    BuildingAttacked,
    /// <summary>A building site of the player's is finished.</summary>
    BuildingFinished,
    /// <summary>The player finished researching a tech (<see cref="GameEvent.Def"/> is its index).</summary>
    ResearchDone,
    /// <summary>A unit stepped out of the player's building (<see cref="GameEvent.Def"/> is its unit type).</summary>
    UnitTrained,
    /// <summary>The player's town hall is gone: knocked down, burned or fallen in.</summary>
    TownHallDestroyed,
    /// <summary>The player's town hall was captured by another side.</summary>
    TownHallCaptured,
    /// <summary>An enemy town hall the player last attacked is gone.</summary>
    EnemyTownHallDestroyed,
    /// <summary>The player captured an enemy town hall.</summary>
    EnemyTownHallTaken,
    /// <summary>The player's units destroyed a city-state's totem, and the town is theirs (<see cref="GameEvent.Def"/> is the city-state).</summary>
    TownConquered,
    /// <summary>Another side destroyed the totem of the player's city-state town and holds it now.</summary>
    TownLost,
    /// <summary>A city-state town the player holds sent them a tribute caravan.</summary>
    TributeSent,
    /// <summary>
    /// A unit of the player's was killed by the ground alone: poison bog, lava's heat or a sinkhole giving way
    /// (see <see cref="World.HazardSystem"/>). <see cref="GameEvent.Def"/> is its unit type.
    /// </summary>
    UnitLostToHazard,
    /// <summary>
    /// The player's units searched an old ruin and found what it held (see <see cref="World.RuinSystem"/>).
    /// <see cref="GameEvent.Subject"/> is the unit that found it, <see cref="GameEvent.Def"/> the ruin's index.
    /// </summary>
    RuinSearched,
    /// <summary>
    /// The player's fighting units took an ancient site (see <see cref="World.DominionSystem"/>), or (for the player
    /// who held it) a rival took it from them. <see cref="GameEvent.Subject"/> is the player who took it,
    /// <see cref="GameEvent.Def"/> the ruin's index.
    /// </summary>
    SiteTaken,
    /// <summary>The player lost an ancient site they held to a rival (<see cref="GameEvent.Subject"/> took it).</summary>
    SiteLost,
    /// <summary>
    /// A player (<see cref="GameEvent.Subject"/>, maybe this one) now holds more than half the ancient sites: the
    /// dominion clock is running. <see cref="GameEvent.Def"/> is the ticks it runs for.
    /// </summary>
    DominionStarted,
    /// <summary>
    /// The player made a discovery (searched a ruin, cleared a lair, took a town) that lets them research a discovery
    /// tech (<see cref="GameEvent.Def"/> is its index; see <see cref="Content.TechDef.Discovery"/>).
    /// </summary>
    TechDiscovered,
    /// <summary>
    /// A player (<see cref="GameEvent.Subject"/>, maybe this one) began building their great work (see
    /// <see cref="World.AscendancySystem"/>) at (x, y, z). <see cref="GameEvent.Def"/> is the building type.
    /// </summary>
    GreatWorkBegun,
    /// <summary>
    /// A player's (<see cref="GameEvent.Subject"/>) great work is finished: if it still stands
    /// <see cref="World.AscendancySystem.AscendancyTicks"/> from now, they win. <see cref="GameEvent.Def"/> is the building type.
    /// </summary>
    GreatWorkFinished,
    /// <summary>A player's (<see cref="GameEvent.Subject"/>) great work was brought down. <see cref="GameEvent.Def"/> is the building type.</summary>
    GreatWorkLost,
    /// <summary>
    /// The dark power under the Heartscar (see <see cref="World.AwakeningSystem"/>) will wake in
    /// <see cref="World.AwakeningSystem.WarnTicks"/> or less; (x, y, z) is the ground over its heart.
    /// </summary>
    AwakeningNear,
    /// <summary>A stage of the dark power's waking came (<see cref="GameEvent.Def"/>, from 1); (x, y, z) is the ground over its heart.</summary>
    AwakeningStage,
    /// <summary>A brood of corrupted beasts (<see cref="GameEvent.Def"/> of them) rose out of the Heartscar at (x, y, z) and is coming for the player.</summary>
    BroodRising,
    /// <summary>
    /// A unit of the player's (<see cref="GameEvent.Subject"/>) came onto a toll town's ground and they could not pay
    /// the toll, or are at feud with it, so its guards fight it. <see cref="GameEvent.Def"/> is the city-state. Only
    /// when none of the player's units already owed that town.
    /// </summary>
    TollUnpaid,
    /// <summary>
    /// A rival (<see cref="GameEvent.Subject"/>) offers the player a truce (see <see cref="TruceSystem"/>); it stands
    /// for <see cref="GameEvent.Def"/> ticks.
    /// </summary>
    TruceOffered,
    /// <summary>The player and a rival (<see cref="GameEvent.Subject"/>) are at peace for <see cref="GameEvent.Def"/> ticks.</summary>
    TruceStarted,
    /// <summary>
    /// The truce with a rival (<see cref="GameEvent.Subject"/>) ends in <see cref="TruceSystem.WarnTicks"/>;
    /// <see cref="GameEvent.Def"/> is the player who broke it off, or -1 if its time is up.
    /// </summary>
    TruceEnding,
    /// <summary>The truce with a rival (<see cref="GameEvent.Subject"/>) is over; <see cref="GameEvent.Def"/> as for <see cref="TruceEnding"/>.</summary>
    TruceEnded,
    /// <summary>
    /// A rival (<see cref="GameEvent.Subject"/>) offers the player a trade deal (see <see cref="TradeDealSystem"/>); it
    /// stands for <see cref="GameEvent.Def"/> ticks.
    /// </summary>
    DealOffered,
    /// <summary>The player and a rival (<see cref="GameEvent.Subject"/>) are trade partners now.</summary>
    DealStarted,
    /// <summary>
    /// The trade deal with a rival (<see cref="GameEvent.Subject"/>) is over; <see cref="GameEvent.Def"/> is the player
    /// who ended it, or -1 if one of the two is out of the match.
    /// </summary>
    DealEnded,
}

/// <summary>
/// Something that happened in the simulation that a player should hear about. <see cref="Player"/> is the
/// player it is for; (X, Y, Z) is where it happened. <see cref="Subject"/> is the unit or building id
/// (0 for research), <see cref="Def"/> the unit, building or tech definition index.
/// </summary>
public readonly record struct GameEvent(long Seq, long Tick, int Player, GameEventKind Kind, int X, int Y, int Z, int Subject, int Def);

/// <summary>
/// The events of the last while, for alerts: under attack, buildings finished, research done, units trained
/// and town halls lost or taken. The systems report them as they happen; the log keeps the newest
/// <see cref="Capacity"/>, each with a sequence number that only grows, so a reader asks for those after
/// the last one it saw. Nothing in the simulation reads the log, so it is not part of the state hash, but
/// it is filled deterministically like everything else.
///
/// Under attack events for one player are throttled by place: none within <see cref="AttackRadius"/>
/// columns of the last one there for <see cref="AttackRepeatTicks"/>. Town halls are watched at the end of
/// every tick: one that is gone or has changed hands tells its old owner, and the player who took it (or
/// last hit it).
/// </summary>
public sealed class EventLog
{
    public const int Capacity = 256;
    /// <summary>Columns: an attack this close to a recent one is the same fight.</summary>
    public const int AttackRadius = 16;
    /// <summary>Ticks before an attack in the same place is reported again.</summary>
    public const int AttackRepeatTicks = 5 * Simulation.TicksPerSecond;

    private readonly Simulation _sim;
    private readonly List<GameEvent> _events = new();
    private long _nextSeq = 1;
    // The latest under attack event in each place, per player: (x, z, tick).
    private readonly List<(int Player, int X, int Z, long Tick)> _attacks = new();
    // Active town halls at the end of the last tick, by id: owner and place.
    private readonly SortedDictionary<int, (int Owner, int X, int Y, int Z, int Def)> _halls = new();
    // The player who last hit, captured or burned each building, by building id.
    private readonly SortedDictionary<int, int> _lastAttacker = new();

    public EventLog(Simulation sim)
    {
        _sim = sim;
    }

    /// <summary>The kept events, oldest first.</summary>
    public IReadOnlyList<GameEvent> All => _events;

    /// <summary>The sequence number the next event will get (one more than the last one's).</summary>
    public long NextSeq => _nextSeq;

    /// <summary>The kept events with a sequence number above <paramref name="seq"/>, oldest first.</summary>
    public List<GameEvent> After(long seq, int player = -1)
    {
        var result = new List<GameEvent>();
        foreach (var e in _events)
            if (e.Seq > seq && (player < 0 || e.Player == player)) result.Add(e);
        return result;
    }

    private void Add(int player, GameEventKind kind, int x, int y, int z, int subject, int def)
    {
        if (player < 0 || player >= Simulation.WildPlayer) return;
        _events.Add(new GameEvent(_nextSeq++, _sim.Tick, player, kind, x, y, z, subject, def));
        if (_events.Count > Capacity) _events.RemoveRange(0, _events.Count - Capacity);
    }

    /// <summary>A unit of <paramref name="player"/>'s was hit by <paramref name="attacker"/>'s at (x, y, z).</summary>
    internal void UnitAttacked(int player, int attacker, int x, int y, int z, int unitId, int def)
    {
        if (attacker == player || !Throttle(player, x, z)) return;
        Add(player, GameEventKind.UnitsAttacked, x, y, z, unitId, def);
    }

    /// <summary>A building was hit, or a capture or burning of it began, by <paramref name="attacker"/>.</summary>
    internal void BuildingAttacked(Building b, int attacker)
    {
        if (attacker == b.Owner) return;
        _lastAttacker[b.Id] = attacker;
        int x = (b.X + b.MaxX) / 2, z = (b.Z + b.MaxZ) / 2;
        if (!Throttle(b.Owner, x, z)) return;
        Add(b.Owner, GameEventKind.BuildingAttacked, x, b.Y, z, b.Id, b.Def.Index);
    }

    internal void BuildingFinished(Building b) =>
        Add(b.Owner, GameEventKind.BuildingFinished, (b.X + b.MaxX) / 2, b.Y, (b.Z + b.MaxZ) / 2, b.Id, b.Def.Index);

    /// <summary>The player took the town; <paramref name="old"/> (-1 for nobody) lost it.</summary>
    internal void TownConquered(CityState town, int player, int old)
    {
        Add(player, GameEventKind.TownConquered, town.X, town.Y, town.Z, town.Id, town.Def.Index);
        if (old >= 0 && old != player) Add(old, GameEventKind.TownLost, town.X, town.Y, town.Z, town.Id, town.Def.Index);
    }

    internal void TollUnpaid(CityState town, int player, int unit) =>
        Add(player, GameEventKind.TollUnpaid, town.X, town.Y, town.Z, unit, town.Def.Index);

    internal void TributeSent(CityState town, int player) =>
        Add(player, GameEventKind.TributeSent, town.X, town.Y, town.Z, town.Id, town.Def.Index);

    internal void ResearchDone(int player, int tech)
    {
        var hall = _sim.Economy.TownHall(player);
        if (hall != null) Add(player, GameEventKind.ResearchDone, (hall.X + hall.MaxX) / 2, hall.Y, (hall.Z + hall.MaxZ) / 2, 0, tech);
        else Add(player, GameEventKind.ResearchDone, -1, -1, -1, 0, tech);
    }

    internal void TechDiscovered(int player, int tech) =>
        Add(player, GameEventKind.TechDiscovered, -1, -1, -1, 0, tech);

    internal void UnitTrained(int player, int unitId, int def, int x, int y, int z) =>
        Add(player, GameEventKind.UnitTrained, x, y, z, unitId, def);

    /// <summary>The ground killed a unit of the player's at (x, y, z) (see <see cref="World.HazardSystem"/>).</summary>
    internal void UnitLostToHazard(int player, int unitId, int def, int x, int y, int z) =>
        Add(player, GameEventKind.UnitLostToHazard, x, y, z, unitId, def);

    /// <summary>The player's unit (id) found what the ruin held.</summary>
    internal void RuinSearched(int player, World.AncientRuin ruin, int unitId) =>
        Add(player, GameEventKind.RuinSearched, ruin.X, ruin.Y + 1, ruin.Z, unitId, ruin.Index);

    /// <summary>The player took the site; <paramref name="old"/> (-1 for nobody) held it.</summary>
    internal void SiteTaken(int player, int old, World.AncientRuin ruin)
    {
        Add(player, GameEventKind.SiteTaken, ruin.X, ruin.Y + 1, ruin.Z, player, ruin.Index);
        if (old >= 0 && old != player) Add(old, GameEventKind.SiteLost, ruin.X, ruin.Y + 1, ruin.Z, player, ruin.Index);
    }

    /// <summary>Tells the player that <paramref name="leader"/>'s dominion clock has started.</summary>
    internal void DominionStarted(int player, int leader) =>
        Add(player, GameEventKind.DominionStarted, -1, -1, -1, leader, World.DominionSystem.DominionTicks);

    /// <summary>Tells the player about a great work: begun, finished or brought down (one of the GreatWork kinds).</summary>
    internal void GreatWork(int player, GameEventKind kind, World.AscendancySystem.Work work) =>
        Add(player, kind, work.X, work.Y, work.Z, work.Owner, work.Def);

    /// <summary>Tells the player about the dark power's waking (<see cref="GameEventKind.AwakeningNear"/> or <see cref="GameEventKind.AwakeningStage"/>).</summary>
    internal void Awakening(int player, GameEventKind kind, int x, int y, int z, int stage) =>
        Add(player, kind, x, y, z, 0, stage);

    /// <summary>A brood of <paramref name="count"/> corrupted beasts rose at (x, y, z) and is coming for the player.</summary>
    internal void BroodRising(int player, int x, int y, int z, int count) =>
        Add(player, GameEventKind.BroodRising, x, y, z, 0, count);

    /// <summary>Tells the player about a truce with <paramref name="rival"/> (one of the Truce kinds).</summary>
    internal void Truce(int player, GameEventKind kind, int rival, int def) =>
        Add(player, kind, -1, -1, -1, rival, def);

    /// <summary>True (and remembered) if no attack on the player was reported near (x, z) lately.</summary>
    private bool Throttle(int player, int x, int z)
    {
        if (player < 0 || player >= Simulation.WildPlayer) return false;
        long now = _sim.Tick;
        _attacks.RemoveAll(a => now - a.Tick >= AttackRepeatTicks);
        foreach (var a in _attacks)
            if (a.Player == player && Math.Abs(a.X - x) <= AttackRadius && Math.Abs(a.Z - z) <= AttackRadius) return false;
        _attacks.Add((player, x, z, now));
        return true;
    }

    /// <summary>End of tick: town halls that are gone or changed hands since the last tick.</summary>
    internal void Step()
    {
        var seen = new SortedDictionary<int, (int Owner, int X, int Y, int Z, int Def)>();
        foreach (var b in _sim.Buildings.All)
            if (b.Active && b.Def.Kind == BuildingKind.TownHall)
                seen[b.Id] = (b.Owner, (b.X + b.MaxX) / 2, b.Y, (b.Z + b.MaxZ) / 2, b.Def.Index);
        foreach (var (id, was) in _halls)
        {
            if (seen.TryGetValue(id, out var now))
            {
                if (now.Owner == was.Owner) continue;
                Add(was.Owner, GameEventKind.TownHallCaptured, was.X, was.Y, was.Z, id, was.Def);
                Add(now.Owner, GameEventKind.EnemyTownHallTaken, was.X, was.Y, was.Z, id, was.Def);
                continue;
            }
            Add(was.Owner, GameEventKind.TownHallDestroyed, was.X, was.Y, was.Z, id, was.Def);
            if (_lastAttacker.TryGetValue(id, out int by) && by != was.Owner)
                Add(by, GameEventKind.EnemyTownHallDestroyed, was.X, was.Y, was.Z, id, was.Def);
        }
        _halls.Clear();
        foreach (var (id, hall) in seen) _halls[id] = hall;
        // Forget attackers of buildings that are gone.
        if (_lastAttacker.Count > 0 && _sim.Tick % Simulation.TicksPerSecond == 0)
        {
            var gone = new List<int>();
            foreach (var id in _lastAttacker.Keys)
                if (_sim.Buildings.Get(id) == null) gone.Add(id);
            foreach (int id in gone) _lastAttacker.Remove(id);
        }
    }
}
