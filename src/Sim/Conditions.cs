using System;
using Rts.Sim.Economy;
using Rts.Sim.World;

namespace Rts.Sim;

/// <summary>The weather over the whole map (see <see cref="Conditions"/>).</summary>
public enum Weather : byte
{
    Clear,
    Cloudy,
    Rain,
    Storm,
    /// <summary>A morning fog: it only comes on in the early hours.</summary>
    Fog,
}

/// <summary>How light it is (see <see cref="Conditions"/>).</summary>
public enum DayPart : byte
{
    Day,
    /// <summary>Dawn or dusk.</summary>
    Twilight,
    Night,
}

/// <summary>
/// The time of day, the weather and the light, and what they do to how far units see and how well they
/// shoot. Played only when <see cref="Simulation.DayNight"/> is set; otherwise it is always a clear day and
/// nothing here changes anything.
///
/// The clock follows the tick: a day lasts <see cref="DayTicks"/> and a match starts at
/// <see cref="StartMinute"/> (or <see cref="Simulation.DayStart"/>).
///
/// The weather rolls in and out as the match goes on: each spell lasts one to five hours of the day, and
/// when one begins the next is already decided, so the sky can be seen to change ahead of it
/// (<see cref="Next"/>, <see cref="ChangesAt"/>). What comes next leans on what there is now: clear skies
/// cloud over before it rains, rain can build to a storm, storms die down to rain. A fog only rolls in during
/// the early morning. The rolls use the simulation's own random stream, stirred by how many units and
/// buildings there are, so they differ from match to match but are the same on every machine and in a
/// replay. A match starts with a clear spell.
///
/// Sight (<see cref="SightPercent"/>): units and buildings see less far at dusk, dawn and night, in rain, a
/// storm or fog, and in the swamp's mist at night and in the morning. Light beats darkness: a column within
/// the light of a lantern, a fire or lava (see <see cref="BuildingDef.Light"/>) is seen from the daytime
/// distance (the weather still counts). A storm puts out the open flames of torches and campfires until it
/// passes; lanterns on buildings are sheltered and stay lit. The lit columns are worked out every
/// <see cref="Lights.UpdateTicks"/>.
///
/// Accuracy (<see cref="HitChance"/>): a ranged shot at a unit can miss at night (unless the target stands
/// in the light), in rain, a storm or fog. Melee blows and shots at buildings always land. Whether a shot
/// lands is a hash of the tick, the shooter and the match seed, the same on every machine.
///
/// Wet ground (<see cref="Wetness"/>): rain soaks the ground and a storm soaks it faster; it dries out again
/// under clear or cloudy skies. Damp ground turns to mud: units walking on soft ground (grass, soil, swamp mud,
/// gloom moss) off the roads go slower (<see cref="MudPace"/>), wheels and machines more so; roads, stone and
/// planks keep their pace. Wet fields grow more: farm work goes faster on wet ground (<see cref="FieldPercent"/>).
/// In a storm, felling trees out in the wind takes longer (<see cref="StormFelling"/>).
///
/// All numbers are starting guesses to be tuned in playtests.
/// </summary>
public sealed class Conditions
{
    /// <summary>Ticks in a whole day and night: 20 minutes.</summary>
    public const int DayTicks = 20 * 60 * Simulation.TicksPerSecond;
    /// <summary>Minutes into the day a match starts at: 8:00 in the morning.</summary>
    public const int StartMinute = 8 * 60;
    /// <summary>Ticks in an hour of the day.</summary>
    public const int HourTicks = DayTicks / 24;
    /// <summary>The shortest and longest spell of weather, in hours of the day.</summary>
    public const int MinSpellHours = 1, MaxSpellHours = 5;
    /// <summary>How long the clear spell a match starts with lasts, in hours of the day.</summary>
    public const int FirstSpellHours = 3;

    /// <summary>Night runs from <see cref="DuskEnd"/> to <see cref="DawnStart"/>; twilight is the hour and a half either side of the day.</summary>
    public const int DawnStart = 5 * 60, DawnEnd = 6 * 60 + 30, DuskStart = 19 * 60 + 30, DuskEnd = 21 * 60;
    /// <summary>The swamp's mist lies until this minute of the morning (and all night).</summary>
    public const int MistEnd = 9 * 60;

    /// <summary>Sight, in percent of the usual, at twilight and at night.</summary>
    public const int TwilightSight = 85, NightSight = 60;
    /// <summary>Sight, in percent, in rain, a storm and fog.</summary>
    public const int RainSight = 85, StormSight = 70, FogSight = 60;
    /// <summary>Sight, in percent, from inside the swamp's mist.</summary>
    public const int MistSight = 75;
    /// <summary>Sight, in percent, from the gloom of the shadowed wilds, away from light, at any hour.</summary>
    public const int GloomSight = 60;
    /// <summary>In a hundred, the ranged shots at a target in the shadowed wilds, away from light, that miss for the gloom.</summary>
    public const int GloomMiss = 20;
    /// <summary>The least sight, in percent, whatever the conditions.</summary>
    public const int MinSight = 40;

    /// <summary>Chance, in percent, a ranged shot loses at night (twilight half), unless the target is lit.</summary>
    public const int NightMiss = 20;
    /// <summary>Chance, in percent, a ranged shot loses in rain, a storm and fog.</summary>
    public const int RainMiss = 10, StormMiss = 25, FogMiss = 15;
    /// <summary>The least chance, in percent, a ranged shot has to land.</summary>
    public const int MinHit = 50;

    /// <summary>Wet ground is worked out this often: ten times an hour of the day.</summary>
    public const int WetStepTicks = HourTicks / 10;
    /// <summary>
    /// How much wetter the ground gets in an hour of each weather, in tenths of a percent (1000 is soaked
    /// through): rain and storms soak it, clear and cloudy skies dry it, fog barely does.
    /// </summary>
    public const int RainSoak = 250, StormSoak = 400, ClearDry = -120, CloudyDry = -60, FogDry = -20;
    /// <summary>Wetness (percent) from which the ground is damp: soft ground starts to turn to mud.</summary>
    public const int DampFrom = 30;
    /// <summary>Wetness (percent) from which the ground is muddy, as the HUD says.</summary>
    public const int MuddyFrom = 65;
    /// <summary>Percent of their pace units lose on soaked soft ground (at 100% wet), less as it is less wet.</summary>
    public const int MudSlow = 30;
    /// <summary>Percent of their pace wheels and machines (carts, catapults, rams) lose on soaked soft ground.</summary>
    public const int WheelMudSlow = 50;
    /// <summary>Percent faster farm work goes on soaked ground (at 100% wet), less as it is less wet.</summary>
    public const int FieldBonus = 25;
    /// <summary>Percent longer felling a tree takes in a storm.</summary>
    public const int StormFelling = 25;

    private readonly Simulation _sim;
    private DeterministicRandom? _rng;
    private Weather _weather = Weather.Clear, _next = Weather.Clear;
    private long _changesAt = FirstSpellHours * HourTicks;
    private int _wet; // tenths of a percent

    public Conditions(Simulation sim)
    {
        _sim = sim;
        Lights = new Lights(sim);
    }

    /// <summary>The columns in the light of lanterns and fires.</summary>
    public Lights Lights { get; }

    private bool On => _sim.DayNight;

    /// <summary>Minutes into the day (0 to 1439) at <paramref name="tick"/> of a match that started at minute <paramref name="start"/>.</summary>
    public static int MinuteAt(long tick, int start = StartMinute) => (int)((start + tick * 1440 / DayTicks) % 1440);

    /// <summary>Minutes into the day now; with day and night off, always <see cref="StartMinute"/>.</summary>
    public int Minute => On ? MinuteAt(_sim.Tick, _sim.DayStart) : StartMinute;

    /// <summary>How light it is at a minute of the day.</summary>
    public static DayPart PartAt(int minute) =>
        minute < DawnStart || minute >= DuskEnd ? DayPart.Night
        : minute < DawnEnd || minute >= DuskStart ? DayPart.Twilight
        : DayPart.Day;

    /// <summary>How light it is now.</summary>
    public DayPart Part => PartAt(Minute);

    /// <summary>
    /// True if (x, y, z), where a unit stands with its feet at y, is in the daylight: it is day (not dawn, dusk or
    /// night) and nothing solid is over it. With day and night off, never (nothing here changes anything).
    /// </summary>
    public bool InDaylight(int x, int y, int z) => On && Part == DayPart.Day && _sim.World.SurfaceHeight(x, z) < y;

    /// <summary>The weather now; with day and night off, always clear.</summary>
    public Weather Weather => On ? _weather : Weather.Clear;
    /// <summary>The weather that comes after this spell.</summary>
    public Weather Next => On ? _next : Weather.Clear;
    /// <summary>The tick the weather turns to <see cref="Next"/>.</summary>
    public long ChangesAt => _changesAt;

    /// <summary>How wet the ground is, 0 (dry) to 100 (soaked through); with day and night off, always dry.</summary>
    public int Wetness => On ? _wet / 10 : 0;

    /// <summary>True if the ground is wet enough for soft ground to turn to mud (<see cref="DampFrom"/>).</summary>
    public bool Damp => Wetness >= DampFrom;

    /// <summary>Tests and test mode: sets how wet the ground is (percent).</summary>
    public void SetWetness(int percent) => _wet = Math.Clamp(percent, 0, 100) * 10;

    /// <summary>True for ground that turns to mud when wet: grass, soil, swamp mud and gloom moss.</summary>
    public static bool Soft(BlockType b) => b is BlockType.Grass or BlockType.Soil or BlockType.Mud or BlockType.Gloom;

    /// <summary>
    /// Percent of its pace a unit keeps standing on <paramref name="underfoot"/> now: 100 on dry ground, on a road,
    /// stone or planks; less on wet soft ground, the wetter the slower, and slower again for wheels and machines.
    /// </summary>
    public int MudPace(BlockType underfoot, bool wheels)
    {
        int wet = Wetness;
        if (wet < DampFrom || !Soft(underfoot)) return 100;
        return 100 - wet * (wheels ? WheelMudSlow : MudSlow) / 100;
    }

    /// <summary>Percent of the usual pace farm work goes at now: faster the wetter the fields.</summary>
    public int FieldPercent => 100 + Wetness * FieldBonus / 100;

    /// <summary>
    /// Match setup and tests: the weather is <paramref name="weather"/> for <paramref name="ticks"/> from now,
    /// then turns to <paramref name="next"/> (clear if not given).
    /// </summary>
    public void SetWeather(Weather weather, long ticks, Weather next = Weather.Clear)
    {
        _weather = weather;
        _next = next;
        _changesAt = _sim.Tick + Math.Max(1, ticks);
    }

    /// <summary>
    /// What follows <paramref name="now"/>, from a roll of 0 to 99: the weather leans on what there is.
    /// A fog only comes at <paramref name="minute"/> in the early morning; otherwise that roll is clear.
    /// </summary>
    public static Weather Follow(Weather now, int roll, int minute)
    {
        // Chances, in percent, of clear, cloudy, rain, storm and fog after each weather.
        ReadOnlySpan<int> odds = now switch
        {
            Weather.Clear => stackalloc[] { 45, 35, 5, 0, 15 },
            Weather.Cloudy => stackalloc[] { 30, 25, 30, 5, 10 },
            Weather.Rain => stackalloc[] { 10, 35, 25, 25, 5 },
            Weather.Storm => stackalloc[] { 5, 15, 70, 10, 0 },
            _ => stackalloc[] { 55, 35, 10, 0, 0 }, // fog lifts
        };
        int k = 0;
        for (int sum = odds[0]; k < 4 && roll >= sum; sum += odds[++k]) { }
        var w = (Weather)k;
        return w == Weather.Fog && (minute < 3 * 60 || minute >= 8 * 60) ? Weather.Clear : w;
    }

    /// <summary>Hash of the weather and its random stream.</summary>
    public ulong ComputeHash()
    {
        if (!On) return 0;
        ulong h = (ulong)_weather * 31 + (ulong)_next * 7 + (ulong)_changesAt * 0x9E3779B97F4A7C15UL + (ulong)_wet * 0xC2B2AE3D27D4EB4FUL;
        return h ^ (_rng?.State ?? 0);
    }

    /// <summary>True if column (x, z) is in the swamp's mist now.</summary>
    public bool InMist(int x, int z)
    {
        if (!On) return false;
        int m = Minute;
        return (m < MistEnd || PartAt(m) == DayPart.Night) && _sim.Map.BiomeAt(x, z) == Biome.Swamp;
    }

    /// <summary>
    /// How far, in percent of the usual, something standing at column (x, z) sees now: what it sees in the
    /// dark (<paramref name="lit"/> false), or what it sees standing in the light (true).
    /// </summary>
    public int SightPercent(int x, int z, bool lit)
    {
        int gloom = !lit && InGloom(x, z) ? GloomSight : 100;
        if (!On) return gloom;
        int p = WeatherSight(Weather);
        if (!lit) p = p * TimeSight(Part) / 100;
        if (InMist(x, z)) p = p * MistSight / 100;
        p = p * gloom / 100;
        return Math.Max(MinSight, p);
    }

    /// <summary>
    /// True if column (x, z) is in the gloom of the shadowed wilds (<see cref="Biome.Wilds"/>): at every hour and in
    /// every weather, and even with day, night and weather off, since it is the ground and not the sky.
    /// </summary>
    public bool InGloom(int x, int z) => _sim.Map.BiomeAt(x, z) == Biome.Wilds;

    /// <summary>Sight, in percent, in the weather <paramref name="w"/>.</summary>
    public static int WeatherSight(Weather w) => w switch
    {
        Weather.Rain => RainSight,
        Weather.Storm => StormSight,
        Weather.Fog => FogSight,
        _ => 100,
    };

    /// <summary>Sight, in percent, at a time of day, away from light.</summary>
    public static int TimeSight(DayPart part) => part switch
    {
        DayPart.Night => NightSight,
        DayPart.Twilight => TwilightSight,
        _ => 100,
    };

    /// <summary>Chance, in percent, that a ranged shot at a target in column (x, z) lands now.</summary>
    public int HitChance(int x, int z)
    {
        int gloom = InGloom(x, z) && !Lights.Lit(x, z) ? GloomMiss : 0;
        if (!On) return 100 - gloom;
        int miss = Weather switch
        {
            Weather.Rain => RainMiss,
            Weather.Storm => StormMiss,
            Weather.Fog => FogMiss,
            _ => 0,
        };
        if (!Lights.Lit(x, z))
            miss += Part switch
            {
                DayPart.Night => NightMiss,
                DayPart.Twilight => NightMiss / 2,
                _ => 0,
            };
        return Math.Max(MinHit, 100 - miss - gloom);
    }

    /// <summary>
    /// True if unit <paramref name="shooterId"/>'s shot this tick at a target in column (x, z) lands. With
    /// <paramref name="aim"/> (a tower's, see <see cref="Economy.BuildingDef.Aim"/>), that many in a hundred of
    /// the shots that would miss land after all.
    /// </summary>
    public bool ShotLands(int shooterId, int x, int z, int aim = 0)
    {
        int chance = HitChance(x, z);
        chance += (100 - chance) * aim / 100;
        if (chance >= 100) return true;
        return DeterministicRandom.Hash((int)(_sim.Tick & 0x7FFFFFFF), shooterId, _sim.Seed ^ 0xAC0FUL) % 100 < (uint)chance;
    }

    /// <summary>Once a tick: the weather turns when its spell is over, and the light is kept up to date.</summary>
    internal void Step()
    {
        // The wilds are dark at every hour, so lights there count by day too (and with day and night off).
        bool gloom = _sim.Map.HasWilds;
        if (!On)
        {
            if (gloom) Lights.Step(true, false);
            return;
        }
        if (_sim.Tick >= _changesAt)
        {
            _rng ??= new DeterministicRandom(_sim.Seed ^ 0x3EA7E5UL);
            // Stirred by the state of the match, so no two matches on a map get the same weather.
            ulong stir = (ulong)_sim.Units.Count * 0x9E3779B1UL + (ulong)_sim.Buildings.Count * 0x85EBCA77UL;
            int roll = (int)((_rng.NextULong() ^ stir) % 100);
            int hours = _rng.Range(MinSpellHours, MaxSpellHours + 1);
            _weather = _next;
            _changesAt = _sim.Tick + (long)hours * HourTicks;
            _next = Follow(_weather, roll, MinuteAt(_changesAt, _sim.DayStart));
            // A fog is a morning thing: one that rolled in burns off by the late morning.
            if (_weather == Weather.Fog)
            {
                int end = MinuteAt(_changesAt, _sim.DayStart);
                if (end > 11 * 60) _changesAt -= (long)(end - 11 * 60) * DayTicks / 1440;
            }
        }
        if (_sim.Tick % WetStepTicks == 0)
        {
            int soak = _weather switch
            {
                Weather.Rain => RainSoak,
                Weather.Storm => StormSoak,
                Weather.Cloudy => CloudyDry,
                Weather.Fog => FogDry,
                _ => ClearDry,
            };
            _wet = Math.Clamp(_wet + soak * WetStepTicks / HourTicks, 0, 1000);
        }
        Lights.Step(Part != DayPart.Day || gloom, Weather == Weather.Storm);
    }
}

/// <summary>
/// The columns lit by lanterns and fires (see <see cref="BuildingDef.Light"/>) of anyone's finished
/// buildings, and by lava (<see cref="LavaLight"/> columns around it): they shine at dusk, at night and at
/// dawn, and not by day. A storm puts out the open flames of torches and campfires (buildings of kind
/// <see cref="BuildingKind.Light"/>). Worked out every <see cref="UpdateTicks"/>, before the units look
/// about (<see cref="VisionSystem"/>).
/// </summary>
public sealed class Lights
{
    /// <summary>Ticks between updates; the same as the looks of <see cref="VisionSystem"/>.</summary>
    public const int UpdateTicks = VisionSystem.UpdateTicks;
    /// <summary>How far (columns) lava lights the ground around it.</summary>
    public const int LavaLight = 3;

    private readonly Simulation _sim;
    private readonly int _sizeX, _sizeZ;
    private byte[]? _lit;
    private bool _any;
    // The top lava block of each column that has one, found once (lava cannot be dug or placed).
    private System.Collections.Generic.List<(int X, int Z)>? _lava;
    // The light of the lava alone (stamped once), whether it is in _lit now, and the boxes the buildings lit
    // into _lit since, so an update puts back only those.
    private byte[]? _lavaLit;
    private bool _lavaAny;
    private readonly System.Collections.Generic.List<(int X0, int Z0, int X1, int Z1)> _stamped = new();

    public Lights(Simulation sim)
    {
        _sim = sim;
        _sizeX = sim.World.SizeX;
        _sizeZ = sim.World.SizeZ;
    }

    /// <summary>True if any column is lit now.</summary>
    public bool Any => _any;

    /// <summary>True if column (x, z) is in the light now.</summary>
    public bool Lit(int x, int z) =>
        _any && (uint)x < (uint)_sizeX && (uint)z < (uint)_sizeZ && _lit![x + z * _sizeX] != 0;

    /// <summary>True if the building's light is shining now: <paramref name="storm"/> puts out open flames.</summary>
    public static bool Shines(Building b, bool storm) =>
        b.Def.Light > 0 && b.State == BuildingState.Active && !(storm && b.Def.Kind == BuildingKind.Light);

    internal void Step(bool dark, bool storm)
    {
        if (_sim.Tick % UpdateTicks != 0) return;
        // Only what the buildings lit last time is put out (lava never changes, so its light is stamped once);
        // the whole map is never cleared, which on a big map would be a million columns every update.
        if (_any)
        {
            foreach (var (x0, z0, x1, z1) in _stamped)
                for (int z = z0; z <= z1; z++)
                    Array.Copy(_lavaLit!, x0 + z * _sizeX, _lit!, x0 + z * _sizeX, x1 - x0 + 1);
            _stamped.Clear();
            if (!dark && _lavaAny) { Array.Clear(_lit!); _lavaAny = false; }
        }
        _any = false;
        if (!dark) return;
        if (_lava == null)
        {
            _lava = FindLava();
            if (_lava.Count > 0)
            {
                _lavaLit = new byte[_sizeX * _sizeZ];
                foreach (var (lx, lz) in _lava)
                    for (int z = Math.Max(0, lz - LavaLight); z <= Math.Min(_sizeZ - 1, lz + LavaLight); z++)
                    for (int x = Math.Max(0, lx - LavaLight); x <= Math.Min(_sizeX - 1, lx + LavaLight); x++)
                        if ((x - lx) * (x - lx) + (z - lz) * (z - lz) <= LavaLight * LavaLight) _lavaLit[x + z * _sizeX] = 1;
            }
        }
        if (_lava.Count > 0)
        {
            // Back from the dark-free spell (by day outside the wilds): the lava's light comes on again.
            if (_lit == null) _lit = (byte[])_lavaLit!.Clone();
            else if (!_lavaAny) Array.Copy(_lavaLit!, _lit, _lit.Length);
            _lavaAny = true;
            _any = true;
        }
        else _lavaAny = false;
        _lavaLit ??= new byte[_sizeX * _sizeZ];
        foreach (var b in _sim.Buildings.All)
        {
            if (!Shines(b, storm)) continue;
            int r = b.Def.Light;
            _lit ??= new byte[_sizeX * _sizeZ];
            _any = true;
            // A disc of r columns beyond the footprint's half-width, around its middle. In half columns, so
            // the middle of an even footprint is exact.
            int cx2 = b.X + b.MaxX + 1, cz2 = b.Z + b.MaxZ + 1;
            int R = r + Math.Max(b.Def.SizeX, b.Def.SizeZ) / 2;
            long r2 = 4L * R * R;
            int x0 = Math.Max(0, cx2 / 2 - R - 1), x1 = Math.Min(_sizeX - 1, cx2 / 2 + R);
            int z0 = Math.Max(0, cz2 / 2 - R - 1), z1 = Math.Min(_sizeZ - 1, cz2 / 2 + R);
            if (x0 > x1 || z0 > z1) continue;
            _stamped.Add((x0, z0, x1, z1));
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                long dx = 2L * x + 1 - cx2, dz = 2L * z + 1 - cz2;
                if (dx * dx + dz * dz <= r2) _lit[x + z * _sizeX] = 1;
            }
        }
    }

    private System.Collections.Generic.List<(int X, int Z)> FindLava()
    {
        var list = new System.Collections.Generic.List<(int X, int Z)>();
        var w = _sim.World;
        for (int z = 0; z < _sizeZ; z++)
        for (int x = 0; x < _sizeX; x++)
            for (int y = w.SizeY - 1; y >= 1; y--)
            {
                var t = w.Get(x, y, z);
                if (t == BlockType.Air) continue;
                if (t == BlockType.Lava) list.Add((x, z));
                break;
            }
        return list;
    }
}
