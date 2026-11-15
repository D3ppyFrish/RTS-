using System;
using System.Collections.Generic;

namespace Rts.Sim.Replay;

/// <summary>State hash recorded at one tick.</summary>
public readonly record struct HashSample(long Tick, ulong Hash);

/// <summary>Runs a command log on a fresh simulation and records state hashes along the way.</summary>
public static class MatchReplay
{
    /// <summary>
    /// Replays <paramref name="log"/> for <see cref="CommandLog.Ticks"/> ticks, sampling the state hash
    /// every <paramref name="hashEvery"/> ticks and once at the end.
    /// </summary>
    public static List<HashSample> Run(CommandLog log, int hashEvery) => Run(log, hashEvery, out _);

    /// <summary>As <see cref="Run(CommandLog, int)"/>, and gives the simulation as it stands at the end (who won, say).</summary>
    public static List<HashSample> Run(CommandLog log, int hashEvery, out Simulation sim)
    {
        if (hashEvery <= 0) throw new ArgumentOutOfRangeException(nameof(hashEvery));
        sim = log.CreateSimulation();
        int next = 0;
        var samples = new List<HashSample>();
        while (sim.Tick < log.Ticks)
        {
            // Schedule just in time so the pending queue stays small.
            while (next < log.Commands.Count && log.Commands[next].Tick <= sim.Tick)
            {
                var c = log.Commands[next++];
                if (c.Tick < sim.Tick) throw new InvalidOperationException($"Command at tick {c.Tick} is in the past");
                sim.Schedule(c.Tick, c.Command);
            }
            sim.Step();
            if (sim.Tick % hashEvery == 0) samples.Add(new HashSample(sim.Tick, sim.StateHash()));
        }
        if (samples.Count == 0 || samples[^1].Tick != sim.Tick) samples.Add(new HashSample(sim.Tick, sim.StateHash()));
        return samples;
    }

    /// <summary>Index of the first sample that differs, or -1 if both runs match.</summary>
    public static int FirstMismatch(IReadOnlyList<HashSample> a, IReadOnlyList<HashSample> b)
    {
        int n = Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i]) return i;
        return a.Count == b.Count ? -1 : n;
    }
}
