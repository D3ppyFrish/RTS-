using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Rts.Sim;
using Rts.Sim.AI;
using Rts.Sim.Replay;

// Headless match runner: plays a match from a seed (scripted players, or computer players with --ai) or
// from a command log, at full speed with no Godot, records the state hash every N ticks, and checks
// repeated runs agree.
//
//   dotnet run -c Release --project tools/MatchRunner -- [options]
//     --log=FILE          replay this command log instead of generating a match (a replay the game saved,
//                         say); prints who won
//     --ai                computer players (AiPlayer) play each other from the usual start until one of
//                         them wins or time runs out; the replays must end with the same winner
//     --levels=A,B        with --ai: each player's level, in player order (classic, easy, normal or hard;
//                         default classic for all; the last one given goes for the rest)
//     --fog               with --ai: play with fog of war
//     --dominion          with --ai: play with dominion (holding most of the ancient sites for 3 minutes wins)
//     --awakening         with --ai: play with the Awakening (the dark power wakes from 45 minutes and sends broods)
//     --story             with --ai: print what each computer player from Easy up decided, as it decides
//                         it (its journal; Hard's says the most)
//     --trace             with --ai: print every minute what each player has (workers, soldiers and their
//                         worth, goods, buildings, housing) and what its army is doing
//     --seed=N            seed for a generated match (default 1234)
//     --world=XxYxZ       world size for a generated match (default 512x64x512; with --ai, the game's
//                         default map, 768x64x768)
//     --minutes=M         generated match length in game minutes (default 10; with --ai, at most this
//                         long, default 40)
//     --players=N         scripted players (default 2)
//     --apm=N             dig/build commands per player per minute (default 600)
//     --units=N           units each player starts with; they get new orders every second (default 100)
//     --record=FILE       save the generated match as a command log (a replay)
//     --runs=N            replay the log N times and require identical hashes (default 2)
//     --hash-every=N      ticks between hash samples (default 1200 = one game minute)
//     --hashes=FILE       write the hash trace of the first run
// Exit code 0 when every run matches, 1 on a mismatch, 2 on bad arguments, 3 when an --ai match ends
// with no winner.

var opts = new Dictionary<string, string>();
foreach (var a in args)
{
    var t = a.TrimStart('-');
    int eq = t.IndexOf('=');
    if (eq > 0) opts[t[..eq]] = t[(eq + 1)..]; else opts[t] = "true";
}

string Opt(string key, string fallback) => opts.TryGetValue(key, out var v) ? v : fallback;
var ci = CultureInfo.InvariantCulture;

var told = new List<int>();
// --trace: one line a minute for each player still in the match.
void Trace(Simulation sim, IReadOnlyList<AiPlayer> players)
{
    if (opts.ContainsKey("story"))
    {
        // --story: each computer player's journal, as it is written.
        while (told.Count < players.Count) told.Add(0);
        for (int k = 0; k < players.Count; k++)
        {
            var journal = players[k].Journal;
            for (; told[k] < journal.Count; told[k]++)
            {
                var (tick, text) = journal[told[k]];
                Console.WriteLine($"{tick / (60 * Simulation.TicksPerSecond),2}:{tick / Simulation.TicksPerSecond % 60:D2} p{players[k].Player} {text}");
            }
        }
    }
    if (!opts.ContainsKey("trace") || sim.Tick % (60 * Simulation.TicksPerSecond) != 0) return;
    var u = sim.Units;
    foreach (var ai in players)
    {
        int p = ai.Player;
        if (!sim.Match.InPlay(p)) continue;
        int workers = 0, soldiers = 0;
        long power = 0;
        var roles = new SortedDictionary<string, int>();
        var jobs = new SortedDictionary<string, int>();
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != p || u.Subdued[i] || u.Town[i] != 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (def.Role == Rts.Sim.Units.UnitRole.Worker)
            {
                workers++;
                string job = u.Job[i] switch
                {
                    Rts.Sim.Units.UnitJob.Gather => u.JobBlock[i].ToString().ToLowerInvariant(),
                    Rts.Sim.Units.UnitJob.None => "idle",
                    var j => j.ToString().ToLowerInvariant(),
                };
                jobs[job] = jobs.GetValueOrDefault(job) + 1;
                continue;
            }
            if (!def.CanAttack || def.Role == Rts.Sim.Units.UnitRole.Trader) continue;
            soldiers++;
            power += (long)u.Hp[i] * def.Damage * Simulation.TicksPerSecond / Math.Max(1, def.AttackTicks);
            roles[def.Id] = roles.GetValueOrDefault(def.Id) + 1;
        }
        var kinds = new SortedDictionary<string, int>();
        foreach (var b in sim.Buildings.All)
            if (b.Owner == p && b.Active) kinds[b.Def.Id] = kinds.GetValueOrDefault(b.Def.Id) + 1;
        var stock = sim.Economy.Stockpile(p);
        var g = sim.Economy.Player(p).Gathered;
        string Join(SortedDictionary<string, int> d) { var l = new List<string>(); foreach (var (k, v) in d) l.Add($"{k}:{v}"); return string.Join(" ", l); }
        Console.WriteLine(string.Create(ci,
            $"{sim.Tick / (60 * Simulation.TicksPerSecond),2}m p{p} {ai.Level,-7} workers {workers,2} soldiers {soldiers,2} power {power / 1000,4}k pop {sim.Economy.Population(p)}/{sim.Economy.Housing(p)} food {stock[0]} wood {stock[1]} stone {stock[2]} (got {g[0]} {g[1]} {g[2]}) | wave {ai.Wave.Count} waves {ai.Waves} {ai.Doing} | {Join(jobs)} | {Join(roles)} | {Join(kinds)}"));
    }
}

try
{
    int runs = int.Parse(Opt("runs", "2"), ci);
    int hashEvery = int.Parse(Opt("hash-every", "1200"), ci);
    if (runs < 1 || hashEvery < 1) throw new ArgumentException("--runs and --hash-every must be at least 1");

    var total = Stopwatch.StartNew();
    CommandLog log;
    // With --ai: who won (-1 for nobody) and when.
    int? aiWinner = null;
    long aiEnd = -1;
    string Clock(long ticks) => string.Create(ci, $"{ticks / (60 * Simulation.TicksPerSecond)}:{ticks / Simulation.TicksPerSecond % 60:D2}");
    if (opts.TryGetValue("log", out var logPath))
    {
        using var reader = File.OpenText(logPath);
        log = CommandLog.Read(reader);
        Console.WriteLine($"Loaded {logPath}: {log.Commands.Count} commands, {log.Ticks} ticks");
    }
    else if (opts.ContainsKey("ai"))
    {
        // Computer players play on the game's default map (issue #67).
        var size = Opt("world", $"{Rts.Sim.World.RegionGenerator.DefaultSize}x{Rts.Sim.World.RegionGenerator.DefaultHeight}x{Rts.Sim.World.RegionGenerator.DefaultSize}").Split('x');
        if (size.Length != 3) throw new ArgumentException("--world must look like 512x64x512");
        double minutes = double.Parse(Opt("minutes", "40"), ci);
        long ticks = (long)Math.Round(minutes * 60 * Simulation.TicksPerSecond);
        var sw = Stopwatch.StartNew();
        var levels = new List<AiLevel>();
        foreach (var name in Opt("levels", "classic").Split(','))
            levels.Add(Enum.TryParse<AiLevel>(name.Trim(), ignoreCase: true, out var level)
                ? level : throw new ArgumentException($"--levels: unknown level '{name}' (classic, easy, normal or hard)"));
        var result = AiMatch.Play(ulong.Parse(Opt("seed", "1234"), ci),
            int.Parse(size[0], ci), int.Parse(size[1], ci), int.Parse(size[2], ci), ticks, levels: levels, fog: opts.ContainsKey("fog"),
            watch: opts.ContainsKey("trace") || opts.ContainsKey("story") ? Trace : null, dominion: opts.ContainsKey("dominion"),
            awakening: opts.ContainsKey("awakening"));
        log = result.Log;
        aiWinner = result.Winner;
        aiEnd = result.EndTick;
        string outcome = result.Winner >= 0 ? $"player {result.Winner} won at {Clock(result.EndTick)}"
            : result.EndTick >= 0 ? $"a draw at {Clock(result.EndTick)}" : $"nobody had won after {Clock(log.Ticks)}";
        Console.WriteLine(string.Create(ci,
            $"AI match: seed {log.Seed}, world {log.SizeX}x{log.SizeY}x{log.SizeZ}: {outcome}; {log.Commands.Count} commands in {sw.ElapsedMilliseconds} ms"));
    }
    else
    {
        var size = Opt("world", "512x64x512").Split('x');
        if (size.Length != 3) throw new ArgumentException("--world must look like 512x64x512");
        double minutes = double.Parse(Opt("minutes", "10"), ci);
        long ticks = (long)Math.Round(minutes * 60 * Simulation.TicksPerSecond);
        int apm = int.Parse(Opt("apm", "600"), ci);
        var sw = Stopwatch.StartNew();
        log = ScriptedMatch.Generate(ulong.Parse(Opt("seed", "1234"), ci),
            int.Parse(size[0], ci), int.Parse(size[1], ci), int.Parse(size[2], ci), ticks,
            players: int.Parse(Opt("players", "2"), ci), actionsPerSecond: Math.Max(1, apm / 60),
            startUnits: int.Parse(Opt("units", "100"), ci));
        Console.WriteLine(string.Create(ci,
            $"Generated match: seed {log.Seed}, world {log.SizeX}x{log.SizeY}x{log.SizeZ}, {log.StartUnits} units per player, {log.Ticks} ticks ({minutes} min), {log.Commands.Count} commands in {sw.ElapsedMilliseconds} ms"));
    }

    if (opts.TryGetValue("record", out var recordPath))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(recordPath))!);
        using var w = new StreamWriter(recordPath) { NewLine = "\n" };
        log.Write(w);
        Console.WriteLine($"Recorded command log to {recordPath}");
    }

    // Replay through the text format so the saved log is what gets checked.
    var replayLog = CommandLog.Parse(log.ToString());
    List<HashSample>? first = null;
    for (int r = 1; r <= runs; r++)
    {
        var sw = Stopwatch.StartNew();
        var samples = MatchReplay.Run(replayLog, hashEvery, out var sim);
        double secs = sw.Elapsed.TotalSeconds;
        Console.WriteLine(string.Create(ci,
            $"Run {r}: {samples[^1].Tick} ticks in {secs:F2} s ({samples[^1].Tick / Math.Max(secs, 1e-9):F0} ticks/s), final hash {samples[^1].Hash:X16}"));
        if (r == 1)
        {
            var m = sim.Match;
            Console.WriteLine("Result: " + (m.Winner >= 0 ? $"player {m.Winner} won{(m.ByDominion ? " by dominion" : m.ByAscendancy ? " by ascendancy" : "")} at {Clock(m.EndTick)}"
                : m.Over ? $"a draw at {Clock(m.EndTick)}" : "nobody has won"));
        }
        if (aiWinner is int won && (sim.Match.Winner != won || sim.Match.EndTick != aiEnd))
        {
            Console.Error.WriteLine(string.Create(ci,
                $"DESYNC: run {r} ended with winner {sim.Match.Winner} at tick {sim.Match.EndTick}, the match with winner {won} at tick {aiEnd}"));
            return 1;
        }
        if (first == null)
        {
            first = samples;
            if (opts.TryGetValue("hashes", out var hashPath))
            {
                using var hw = new StreamWriter(hashPath) { NewLine = "\n" };
                foreach (var s in samples) hw.WriteLine(string.Create(ci, $"{s.Tick} {s.Hash:X16}"));
            }
            continue;
        }
        int bad = MatchReplay.FirstMismatch(first, samples);
        if (bad >= 0)
        {
            string Show(List<HashSample> l) => bad < l.Count ? string.Create(ci, $"tick {l[bad].Tick} hash {l[bad].Hash:X16}") : "no sample";
            Console.Error.WriteLine($"DESYNC: run {r} differs from run 1 at sample {bad}: run 1 {Show(first)}, run {r} {Show(samples)}");
            return 1;
        }
    }
    Console.WriteLine(string.Create(ci,
        $"OK: {runs} runs, {first!.Count} hash samples every {hashEvery} ticks all match. Total {total.Elapsed.TotalSeconds:F1} s"));
    if (aiWinner < 0)
    {
        Console.Error.WriteLine("NO WINNER: the AI match ended without one");
        return 3;
    }
    return 0;
}
catch (Exception e) when (e is ArgumentException or FormatException or IOException or OverflowException)
{
    Console.Error.WriteLine($"Error: {e.Message}");
    return 2;
}
