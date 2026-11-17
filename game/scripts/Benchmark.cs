using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>
/// <c>--benchmark</c> mode: digs 100 blocks per second near the camera focus while the camera
/// slowly orbits, keeps every spawned unit walking to a shared point, records every frame time, then
/// prints a summary line and quits.
/// </summary>
public sealed class Benchmark
{
    public const int DigsPerSecond = 100;
    private const int WarmupFrames = 30;

    private readonly double _seconds;
    private readonly double _loadMs;
    private readonly DeterministicRandom _rng;
    private readonly List<double> _frameMs = new();
    private readonly List<double> _processMs = new();
    private int _warmup = WarmupFrames;
    private double _elapsed;
    private int _digsScheduled;
    private int _maxPending;
    private long _rebuildsAtStart = -1;
    private int _digCarry;

    private readonly string _outPath;
    private readonly List<double> _tris = new();
    // Frame times by the clock: the engine's own delta is rounded to whole screen refreshes.
    private readonly List<double> _clockMs = new();
    private long _lastFrame;
    private readonly List<double> _draws = new();

    /// <summary>More to add to the result line (what was drawn and how), worked out when the run ends.</summary>
    public Func<string>? Extra;

    public Benchmark(double seconds, ulong seed, double loadMs, string outPath)
    {
        _outPath = outPath;
        _seconds = seconds;
        _loadMs = loadMs;
        _rng = new DeterministicRandom(seed ^ 0xBE7C4UL);
    }

    /// <summary>Called once per simulation tick; queues this tick's share of digs.</summary>
    public void ScheduleDigs(Simulation sim, Vector3 focusBlocks)
    {
        _digCarry += DigsPerSecond;
        int count = _digCarry / Simulation.TicksPerSecond;
        _digCarry %= Simulation.TicksPerSecond;
        var w = sim.World;
        for (int i = 0; i < count; i++)
        {
            int x = Math.Clamp((int)focusBlocks.X + _rng.Range(-48, 48), 0, w.SizeX - 1);
            int z = Math.Clamp((int)focusBlocks.Z + _rng.Range(-48, 48), 0, w.SizeZ - 1);
            int y = w.SurfaceHeight(x, z);
            if (y <= 0) continue;
            sim.Schedule(sim.Tick, new DigCommand(0, x, y, z));
            _digsScheduled++;
        }
    }

    /// <summary>
    /// Every 8 seconds, sends every spawned unit to one new random point near the focus (one move order
    /// per player, all sharing one flow field), so the benchmark keeps a big crowd walking to one place.
    /// </summary>
    public void ScheduleMoves(Simulation sim, List<(int Owner, int Id)> units, Vector3 focusBlocks)
    {
        if (units.Count == 0 || sim.Tick % (Simulation.TicksPerSecond * 8) != 0) return;
        var w = sim.World;
        int x = Math.Clamp((int)focusBlocks.X + _rng.Range(-40, 41), 1, w.SizeX - 2);
        int z = Math.Clamp((int)focusBlocks.Z + _rng.Range(-40, 41), 1, w.SizeZ - 2);
        foreach (int owner in units.Select(u => u.Owner).Distinct().OrderBy(o => o))
        {
            var ids = units.Where(u => u.Owner == owner).Select(u => u.Id).ToArray();
            sim.Schedule(sim.Tick, new MoveCommand(owner, ids, x, z));
        }
        _unitsMoving = units.Count;
    }

    private int _unitsMoving;

    /// <summary>Records one frame. Returns true when the run is over.</summary>
    public bool Record(double delta, double gameMs, ChunkRenderer chunks)
    {
        if (_warmup > 0) { _warmup--; return false; }
        if (_rebuildsAtStart < 0) _rebuildsAtStart = chunks.RebuildsCompleted;
        _frameMs.Add(delta * 1000.0);
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastFrame != 0) _clockMs.Add((now - _lastFrame) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        _lastFrame = now;
        _tris.Add(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame));
        _draws.Add(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame));
        // Main-thread time spent on our own work this frame: simulation ticks plus chunk dispatch and upload.
        _processMs.Add(gameMs + chunks.LastProcessMs);
        _maxPending = Math.Max(_maxPending, chunks.PendingCount);
        _elapsed += delta;
        if (_elapsed < _seconds) return false;

        var sorted = _frameMs.OrderBy(f => f).ToArray();
        double P(double q) => sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        double avg = sorted.Average();
        int slow = sorted.Count(f => f > 1000.0 / 60.0);
        var ci = CultureInfo.InvariantCulture;
        string line = string.Format(ci,
            "BENCHMARK load_ms={0:F0} frames={1} seconds={2:F1} avg_fps={3:F1} avg_ms={4:F2} p50_ms={5:F2} " +
            "p95_ms={6:F2} p99_ms={7:F2} max_ms={8:F2} frames_over_16.7ms={9} digs={10} rebuilds={11} max_pending={12} chunks={13} " +
            "cpu_work_avg_ms={14:F2} cpu_work_max_ms={15:F2} units={17} renderer={16}",
            _loadMs, sorted.Length, _elapsed, sorted.Length / _elapsed, avg, P(0.5), P(0.95), P(0.99), sorted[^1], slow,
            _digsScheduled, chunks.RebuildsCompleted - _rebuildsAtStart, _maxPending, chunks.ChunkNodeCount,
            _processMs.Average(), _processMs.Max(), RenderingServer.GetVideoAdapterName(), _unitsMoving);
        line += string.Format(ci, " avg_tris={0:F0} avg_draws={1:F0}", _tris.Average(), _draws.Average());
        if (_clockMs.Count > 0)
        {
            var clock = _clockMs.OrderBy(f => f).ToArray();
            line += string.Format(ci, " clock_avg_ms={0:F1} clock_p95_ms={1:F1}", clock.Average(), clock[Math.Min(clock.Length - 1, (int)(0.95 * clock.Length))]);
        }
        if (Extra != null) line += " " + Extra();
        GD.Print(line);
        // Release builds on Windows have no console, so also append the result to a file.
        using (var file = FileAccess.Open(_outPath, FileAccess.ModeFlags.ReadWrite) ?? FileAccess.Open(_outPath, FileAccess.ModeFlags.Write))
        {
            if (file != null)
            {
                file.SeekEnd();
                file.StoreLine($"{Time.GetDatetimeStringFromSystem()} {line}");
                GD.Print($"Benchmark result appended to {ProjectSettings.GlobalizePath(_outPath)}");
            }
        }
        return true;
    }
}
