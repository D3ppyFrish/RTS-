using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Replay;

namespace Rts.Sim.AI;

/// <summary>How a match between computer players ended.</summary>
/// <param name="Log">The match as a command log: replaying it plays the same match.</param>
/// <param name="Winner">The player who won, or -1 (a draw, or nobody had won when time ran out).</param>
/// <param name="EndTick">The tick the match was decided on, or -1 if time ran out first.</param>
public sealed record AiMatchResult(CommandLog Log, int Winner, long EndTick);

/// <summary>
/// Plays computer players (<see cref="AiPlayer"/>) against each other from the usual start (see
/// <see cref="Simulation.SetUpUsualStart"/>): a town hall and four of every unit type that is trained for
/// each player, the monster lairs and the city-states' towns. The match runs until one player is left in
/// it (see <see cref="MatchSystem"/>) or time runs out, and is recorded as it goes (see
/// <see cref="Simulation.Recording"/>).
/// </summary>
public static class AiMatch
{
    /// <param name="levels">
    /// Each player's level, in the order of <see cref="MatchSystem.Players"/> (the last one given for the
    /// rest); <see cref="AiLevel.Classic"/> for everyone if not given.
    /// </param>
    /// <param name="fog">Play with fog of war (see <see cref="Simulation.Fog"/>).</param>
    /// <param name="dayNight">Play with day, night and weather (see <see cref="Simulation.DayNight"/>), from <paramref name="dayStart"/>.</param>
    /// <param name="dominion">Play with dominion, the ancient sites' win (see <see cref="Simulation.Dominion"/>).</param>
    /// <param name="watch">Called before every tick with the match as it stands and the computer players, to report on it (it must change nothing).</param>
    public static AiMatchResult Play(ulong seed, int sizeX, int sizeY, int sizeZ, long maxTicks, ContentDb? content = null,
        IReadOnlyList<AiLevel>? levels = null, bool fog = false, bool dayNight = false, int dayStart = Conditions.StartMinute,
        Action<Simulation, IReadOnlyList<AiPlayer>>? watch = null, bool dominion = false)
    {
        content ??= ContentDb.Default;
        var log = CommandLog.UsualStart(seed, sizeX, sizeY, sizeZ, content, fog, dayNight, dayStart,
            dominion: dominion);
        var sim = log.CreateSimulation(content);
        sim.Recording = log;
        var players = new List<AiPlayer>();
        foreach (int p in sim.Match.Players)
        {
            var level = levels is { Count: > 0 } ? levels[Math.Min(players.Count, levels.Count - 1)] : AiLevel.Classic;
            players.Add(new AiPlayer(p, seed, level));
        }
        void Issue(Command c) => sim.Schedule(sim.Tick, c);
        while (sim.Tick < maxTicks && !sim.Match.Over)
        {
            watch?.Invoke(sim, players);
            foreach (var ai in players) ai.Think(sim, Issue);
            sim.Step();
        }
        return new AiMatchResult(log, sim.Match.Winner, sim.Match.EndTick);
    }
}
