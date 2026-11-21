using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.World;

/// <summary>
/// The dark power, the design doc's Awakening phase: something sleeps under the heart of the continent, and late in
/// the match it wakes, in <see cref="Stages"/> stages, and turns on every faction alike. On only with
/// <see cref="Simulation.Awakening"/>.
///
/// <para><b>The heart.</b> It lies in the deep caves' chamber nearest the middle of the map, on their lower level
/// where there is one (<see cref="RegionMap.Deeps"/>); on a map with no deep caves, under the middle of the map. The
/// ground over it is the Heartscar.</para>
///
/// <para><b>Waking.</b> The first stage comes <see cref="FirstStageTicks"/> into the match and each other one
/// <see cref="StageGapTicks"/> after the one before; everyone is warned <see cref="WarnTicks"/> before the first.
/// Digging into the heart wakes it sooner: every block taken out (dug, blasted or fallen) within
/// <see cref="HeartReach"/> columns of it and no more than <see cref="HeartHeight"/> levels over it brings the next
/// stage <see cref="UnrestTicks"/> nearer.</para>
///
/// <para><b>Corrupted ground.</b> From the first stage the Heartscar's ground turns corrupt, creeping out from the
/// column over the heart one column every <see cref="CreepTicks"/> to each stage's reach (<see cref="ReachOf"/>),
/// with a ragged edge. A player's unit standing on it, or no more than <see cref="CorruptDepth"/> levels under its
/// ground, loses <see cref="CorruptDamage"/> hit points every <see cref="CorruptInterval"/> ticks. Monsters do not
/// mind it.</para>
///
/// <para><b>Broods.</b> At each stage, and every <see cref="BroodTicks"/> after while it is awake, a brood of
/// corrupted beasts claws up out of the Heartscar for every player still in: every monster with an
/// <see cref="UnitDef.Awakening"/> stage that has come, <see cref="UnitDef.Brood"/> of each. A brood goes for its
/// player's town hall nearest the heart, fighting everything on the way, and batters the hall once there (another
/// of their buildings once it is down). A player with <see cref="MaxBeastsPerPlayer"/> of the beasts already after
/// them gets no new brood. Beasts are wild monsters: Orcs beat them rather than kill them, and they pay their
/// <see cref="UnitDef.Bounty"/> to whoever gets them. All numbers are starting guesses to be tuned in playtests.</para>
/// </summary>
public sealed class AwakeningSystem
{
    public const int Stages = 3;
    private const int Tps = Simulation.TicksPerSecond;
    /// <summary>Ticks into the match that the first stage comes, unless digging brings it sooner: 45 minutes.</summary>
    public const long FirstStageTicks = 45L * 60 * Tps;
    /// <summary>Ticks from one stage to the next: 15 minutes.</summary>
    public const long StageGapTicks = 15L * 60 * Tps;
    /// <summary>Ticks before the first stage that everyone is warned: 2 minutes.</summary>
    public const long WarnTicks = 2L * 60 * Tps;
    /// <summary>Columns from the heart that a block taken out stirs it.</summary>
    public const int HeartReach = 48;
    /// <summary>Levels over the heart that a block taken out stirs it.</summary>
    public const int HeartHeight = 12;
    /// <summary>Ticks each block taken out near the heart brings the next stage nearer: 2 seconds.</summary>
    public const int UnrestTicks = 2 * Tps;
    /// <summary>Ticks for the corrupted ground to creep one column further out.</summary>
    public const int CreepTicks = Tps;
    /// <summary>Hit points a unit on corrupted ground loses every <see cref="CorruptInterval"/>.</summary>
    public const int CorruptDamage = 2;
    public const int CorruptInterval = 2 * Tps;
    /// <summary>Levels under a corrupted column's ground that it still hurts units.</summary>
    public const int CorruptDepth = 6;
    /// <summary>Columns the corrupted ground's edge wanders in or out.</summary>
    public const int Ragged = 3;
    /// <summary>Ticks between broods while the dark power is awake: 4 minutes.</summary>
    public const long BroodTicks = 4L * 60 * Tps;
    /// <summary>Corrupted beasts after one player beyond which they get no new brood.</summary>
    public const int MaxBeastsPerPlayer = 16;
    /// <summary>Columns from its hall that a brood stops walking and batters it.</summary>
    public const int BatterRange = 10;

    /// <summary>A brood of corrupted beasts sent at a player.</summary>
    public sealed class Brood
    {
        public int Player { get; internal set; }
        /// <summary>The building it is going for (a town hall, at first), or 0 if the player has none left.</summary>
        public int Building { get; internal set; }
        internal readonly List<int> BeastIds = new();
        /// <summary>Its beasts still in the wild (ids), in the order they rose.</summary>
        public IReadOnlyList<int> Beasts => BeastIds;
    }

    private readonly Simulation _sim;
    private readonly List<Brood> _broods = new();
    private readonly List<UnitDef> _beasts = new();
    private bool _warned;

    public AwakeningSystem(Simulation sim)
    {
        _sim = sim;
        var w = sim.World;
        var deeps = sim.Map.Deeps;
        if (deeps.Count > 0)
        {
            // The lower level's chamber nearest the middle (any chamber, with no lower level).
            int cx = w.SizeX / 2, cz = w.SizeZ / 2, upper = w.SizeY / 4;
            long best = long.MaxValue;
            foreach (var d in deeps)
            {
                long score = (long)(d.X - cx) * (d.X - cx) + (long)(d.Z - cz) * (d.Z - cz) + (d.Y > upper ? 1L << 40 : 0);
                if (score < best) (best, HeartX, HeartY, HeartZ) = (score, d.X, d.Y, d.Z);
            }
        }
        else
        {
            HeartX = w.SizeX / 2;
            HeartZ = w.SizeZ / 2;
            HeartY = Math.Max(0, Walk.GroundLevel(w, HeartX, HeartZ) - 8);
        }
        ScarY = Math.Max(0, Walk.GroundLevel(w, HeartX, HeartZ));
        foreach (var u in sim.Content.Units)
            if (u.Awakening > 0) _beasts.Add(u);
        w.BlockChanged += Stirred;
    }

    /// <summary>The heart's place: a point in its chamber's open middle.</summary>
    public int HeartX { get; }
    public int HeartY { get; }
    public int HeartZ { get; }
    /// <summary>The level of the ground over the heart, where the broods rise (as it was when last looked at).</summary>
    public int ScarY { get; private set; }
    /// <summary>The stages that have come, 0 while it sleeps.</summary>
    public int Stage { get; private set; }
    /// <summary>The tick the next stage comes, or <see cref="long.MaxValue"/> once it is fully awake.</summary>
    public long NextStageAt { get; private set; } = FirstStageTicks;
    /// <summary>Ticks digging near the heart has brought its waking forward, all told.</summary>
    public long Unrest { get; private set; }
    /// <summary>How far out (columns) the corrupted ground reaches now; 0 while it sleeps.</summary>
    public int Radius { get; private set; }
    /// <summary>The tick the next broods rise, once it is awake.</summary>
    public long NextBroodAt { get; private set; } = long.MaxValue;
    /// <summary>The broods still out, in the order they rose.</summary>
    public IReadOnlyList<Brood> Broods => _broods;
    /// <summary>The kinds of corrupted beast, in the data's order.</summary>
    public IReadOnlyList<UnitDef> Beasts => _beasts;

    /// <summary>Ticks left before the next stage, or -1 once it is fully awake.</summary>
    public long TicksToNextStage => Stage >= Stages ? -1 : Math.Max(0, NextStageAt - _sim.Tick);

    /// <summary>How far out (columns) the corrupted ground creeps at the stage: a 32nd, a 16th and a 10th of the map's width.</summary>
    public int ReachOf(int stage) => stage switch
    {
        <= 0 => 0,
        1 => Math.Max(8, _sim.World.SizeX / 32),
        2 => Math.Max(16, _sim.World.SizeX / 16),
        _ => Math.Max(24, _sim.World.SizeX / 10),
    };

    /// <summary>True if column (x, z) is corrupted ground now.</summary>
    public bool Corrupt(int x, int z)
    {
        if (Radius <= 0) return false;
        int dx = x - HeartX, dz = z - HeartZ, far = Radius + Ragged;
        if (Math.Abs(dx) > far || Math.Abs(dz) > far) return false;
        int d = IntMath.Distance(x, z, HeartX, HeartZ);
        if (d > far) return false;
        // The edge wanders in and out in patches of four by four columns.
        int wobble = (int)(DeterministicRandom.Hash(x >> 2, z >> 2, _sim.Seed ^ 0x5D1B6A3C9E47F21BUL) % (2 * Ragged + 1)) - Ragged;
        return d + wobble <= Radius;
    }

    /// <summary>True if unit i stands on corrupted ground or close enough under it to be hurt by it.</summary>
    public bool OnCorruptGround(int i)
    {
        var u = _sim.Units;
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        if (!Corrupt(x, z)) return false;
        int ground = Walk.GroundLevel(_sim.World, x, z);
        return ground >= 0 && u.Y[i] >= ground - CorruptDepth;
    }

    public ulong ComputeHash()
    {
        if (!_sim.Awakening) return 0;
        ulong h = 0x8A5CD789635D2DFFUL;
        void Mix(long v) { h = (h ^ (ulong)v) * 0x100000001B3UL; }
        Mix(Stage); Mix(NextStageAt); Mix(Unrest); Mix(Radius); Mix(NextBroodAt); Mix(_warned ? 1 : 0);
        Mix(_broods.Count);
        foreach (var b in _broods)
        {
            Mix(b.Player); Mix(b.Building); Mix(b.BeastIds.Count);
            foreach (int id in b.BeastIds) Mix(id);
        }
        return h;
    }

    // A block changed: one taken out near the heart stirs it (not at match setup, and not once it is fully awake).
    private void Stirred(int x, int y, int z)
    {
        if (!_sim.Awakening || _sim.Tick == 0 || Stage >= Stages || _sim.Match.Over) return;
        if (y > HeartY + HeartHeight || _sim.World.Get(x, y, z) != BlockType.Air) return;
        if (Math.Abs(x - HeartX) > HeartReach || Math.Abs(z - HeartZ) > HeartReach || IntMath.Distance(x, z, HeartX, HeartZ) > HeartReach) return;
        NextStageAt -= UnrestTicks;
        Unrest += UnrestTicks;
    }

    /// <summary>
    /// Test mode and screenshots: the dark power wakes to the stage at once (its corrupted ground reaching that stage's
    /// width straight away), and the next broods rise on the next tick.
    /// </summary>
    public void WakeTo(int stage)
    {
        stage = Math.Clamp(stage, 0, Stages);
        if (stage <= Stage) return;
        Stage = stage;
        Radius = ReachOf(stage);
        _warned = true;
        NextStageAt = Stage < Stages ? _sim.Tick + StageGapTicks : long.MaxValue;
        NextBroodAt = _sim.Tick;
    }

    /// <summary>
    /// After the hazards, before the fighting: the clock and the stages, the corrupted ground creeping out and hurting
    /// the units on it, and the broods rising and going for their halls (see the class notes).
    /// </summary>
    internal void Step()
    {
        if (!_sim.Awakening || _sim.Match.Over) return;
        long t = _sim.Tick;
        if (Stage < Stages)
        {
            if (!_warned && Stage == 0 && t >= NextStageAt - WarnTicks)
            {
                _warned = true;
                Tell(GameEventKind.AwakeningNear, 0);
            }
            if (t >= NextStageAt)
            {
                Stage++;
                NextStageAt = Stage < Stages ? t + StageGapTicks : long.MaxValue;
                NextBroodAt = t;
                Tell(GameEventKind.AwakeningStage, Stage);
            }
        }
        if (Stage == 0) return;
        if (Radius < ReachOf(Stage) && t % CreepTicks == 0) Radius++;
        Hurt(t);
        if (t >= NextBroodAt)
        {
            NextBroodAt = t + BroodTicks;
            RaiseBroods();
        }
        else if (t % Tps == 0) Drive();
    }

    // Every player's unit on corrupted ground loses hit points, each on its own beat.
    private void Hurt(long t)
    {
        var u = _sim.Units;
        for (int i = 0; i < u.Count; i++)
        {
            if ((t + u.Id[i]) % CorruptInterval != 0 || u.Hp[i] <= 0 || u.Owner[i] >= Simulation.WildPlayer) continue;
            if (OnCorruptGround(i)) _sim.Combat.Crush(i, CorruptDamage);
        }
    }

    /// <summary>The players the broods go for: those still in the match (with no match, everyone with a town hall).</summary>
    private List<int> Targets()
    {
        var list = new List<int>();
        var match = _sim.Match;
        for (int p = 0; p < Simulation.WildPlayer; p++)
            if (match.Players.Count > 0 ? match.InPlay(p) : _sim.Economy.TownHall(p) != null) list.Add(p);
        return list;
    }

    private void RaiseBroods()
    {
        Prune();
        var kinds = new List<UnitDef>();
        foreach (var def in _beasts)
            if (def.Awakening <= Stage)
                for (int k = 0; k < def.Brood; k++) kinds.Add(def);
        if (kinds.Count == 0) return;
        var spots = Spots(kinds.Count * 2);
        if (spots.Count == 0) return;
        int next = 0;
        foreach (int p in Targets())
        {
            int after = 0;
            foreach (var b in _broods)
                if (b.Player == p) after += b.BeastIds.Count;
            if (after >= MaxBeastsPerPlayer) continue;
            var goal = Goal(p, HeartX, HeartZ);
            if (goal == null) continue;
            var brood = new Brood { Player = p, Building = goal.Id };
            foreach (var def in kinds)
            {
                var (x, y, z) = spots[next++ % spots.Count];
                int id = _sim.SpawnUnit(Simulation.WildPlayer, def.Id, x, z, y);
                if (id > 0) brood.BeastIds.Add(id);
            }
            if (brood.BeastIds.Count == 0) continue;
            _broods.Add(brood);
            var (gx, gz) = Middle(goal);
            _sim.Issue(new AttackMoveCommand(Simulation.WildPlayer, brood.BeastIds.ToArray(), gx, gz));
            _sim.Events.BroodRising(p, HeartX, ScarY, HeartZ, brood.BeastIds.Count);
        }
    }

    // Columns round the column over the heart where beasts can rise: dry ground, nearest first.
    private List<(int X, int Y, int Z)> Spots(int count)
    {
        var w = _sim.World;
        var spots = new List<(int X, int Y, int Z)>();
        ScarY = Math.Max(0, Walk.GroundLevel(w, HeartX, HeartZ));
        for (int r = 0; r <= 24 && spots.Count < count; r += 2)
        for (int dz = -r; dz <= r && spots.Count < count; dz += 2)
        for (int dx = -r; dx <= r && spots.Count < count; dx += 2)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
            int x = HeartX + dx, z = HeartZ + dz;
            if (!w.InBounds(x, 0, z)) continue;
            int y = Walk.GroundLevel(w, x, z);
            if (y <= 0 || w.Get(x, y, z) != BlockType.Air) continue;
            if (w.Get(x, y - 1, z) is BlockType.Water or BlockType.Lava or BlockType.Bog or BlockType.Quicksand or BlockType.Crust) continue;
            if (_sim.Buildings.At(x, y, z) != null) continue;
            spots.Add((x, y, z));
        }
        return spots;
    }

    /// <summary>
    /// What a brood after the player goes for: their town hall nearest (x, z), or with none left their building
    /// nearest it (not a plan); null if they have none.
    /// </summary>
    private Building? Goal(int player, int x, int z)
    {
        Building? hall = null, any = null;
        long hallD = long.MaxValue, anyD = long.MaxValue;
        foreach (var b in _sim.Buildings.All)
        {
            if (b.Owner != player || b.State == BuildingState.Planned) continue;
            var (mx, mz) = Middle(b);
            long d = (long)(mx - x) * (mx - x) + (long)(mz - z) * (mz - z);
            if (b.Def.Kind == BuildingKind.TownHall && b.Active && d < hallD) (hallD, hall) = (d, b);
            if (d < anyD) (anyD, any) = (d, b);
        }
        return hall ?? any;
    }

    private static (int X, int Z) Middle(Building b) => (b.X + b.Def.SizeX / 2, b.Z + b.Def.SizeZ / 2);

    // Forget the beasts that died or were beaten, and the broods with none left.
    private void Prune()
    {
        var u = _sim.Units;
        for (int k = 0; k < _broods.Count; k++)
        {
            var b = _broods[k];
            b.BeastIds.RemoveAll(id => u.IndexOf(id) is var i && (i < 0 || u.Owner[i] != Simulation.WildPlayer || u.Subdued[i]));
            if (b.BeastIds.Count == 0) _broods.RemoveAt(k--);
        }
    }

    /// <summary>
    /// Once a second: each brood's beasts with nothing to do go on for its building, and batter it once near; a brood
    /// whose building is down goes for the next of its player's.
    /// </summary>
    private void Drive()
    {
        Prune();
        var u = _sim.Units;
        var walk = new List<int>();
        var batter = new List<int>();
        foreach (var brood in _broods)
        {
            var goal = brood.Building != 0 ? _sim.Buildings.Get(brood.Building) : null;
            if (goal == null || goal.Owner != brood.Player || goal.State == BuildingState.Planned)
            {
                int first = u.IndexOf(brood.BeastIds[0]);
                goal = Goal(brood.Player, u.X[first].FloorToInt(), u.Z[first].FloorToInt());
                brood.Building = goal?.Id ?? 0;
                if (goal == null) continue;
            }
            var (gx, gz) = Middle(goal);
            walk.Clear();
            batter.Clear();
            foreach (int id in brood.BeastIds)
            {
                int i = u.IndexOf(id);
                if (u.Target[i] != 0 || u.TargetBuilding[i] != 0 || u.Order[i] != UnitOrder.Idle) continue;
                if (goal.ColumnDistance(u.X[i].FloorToInt(), u.Z[i].FloorToInt()) <= BatterRange) batter.Add(id);
                else walk.Add(id);
            }
            if (batter.Count > 0) _sim.Issue(new AttackBuildingCommand(Simulation.WildPlayer, batter.ToArray(), goal.Id));
            if (walk.Count > 0) _sim.Issue(new AttackMoveCommand(Simulation.WildPlayer, walk.ToArray(), gx, gz));
        }
    }

    /// <summary>Tells every player in the match (everyone with a town hall, with no match) about the dark power.</summary>
    private void Tell(GameEventKind kind, int stage)
    {
        foreach (int p in Targets()) _sim.Events.Awakening(p, kind, HeartX, ScarY, HeartZ, stage);
    }
}
