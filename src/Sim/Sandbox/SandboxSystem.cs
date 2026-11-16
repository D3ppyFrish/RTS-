using System;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.Sandbox;

/// <summary>
/// Test mode (issue #89): tools for building a scenario to try something out. Put any unit (of any player,
/// or a wild monster), any finished building, any kind of tree, any block (rock, ore, water, lava, or air to
/// clear it), a monster lair or blood where you like; remove units and buildings; heal, kill, wound or
/// season units; hand out goods and techs; set the weather and the time of day; and turn on cheats for a
/// player (instant building, training and research, and invulnerability).
///
/// Everything here is done by commands (the Sandbox* commands), so a test match is recorded and replays
/// like any other, and a saved scenario is just its command log. They are carried out only when the match
/// was started in test mode (<see cref="Simulation.TestMode"/>, set before the first tick), so they can never
/// change a normal match, a computer players' match or the determinism check, and are ignored there.
/// </summary>
public sealed class SandboxSystem
{
    /// <summary>The most blocks one <see cref="SandboxBlocksCommand"/> changes (a box 32 on a side).</summary>
    public const int MaxBlocks = 32 * 32 * 32;
    /// <summary>The most units one <see cref="SandboxUnitCommand"/> places.</summary>
    public const int MaxUnits = 100;
    /// <summary>The widest radius of blood or clearing.</summary>
    public const int MaxRadius = 16;
    /// <summary>How long a <see cref="SandboxAction.Bleed"/> wound bleeds (see <see cref="UnitStore.BleedLeft"/>).</summary>
    public const int BleedSeconds = 12;

    private readonly Simulation _sim;
    // Cheats on, per player: a bit for each SandboxCheat.
    private readonly byte[] _cheats = new byte[Simulation.WildPlayer + 1];

    public SandboxSystem(Simulation sim)
    {
        _sim = sim;
    }

    private bool On => _sim.TestMode;
    private UnitStore U => _sim.Units;

    /// <summary>True if the cheat is on for the player (always false outside test mode).</summary>
    public bool Has(int player, SandboxCheat cheat) =>
        On && player >= 0 && player < _cheats.Length && (_cheats[player] & (1 << (int)cheat)) != 0;

    /// <summary>The player's units and buildings take no damage (<see cref="SandboxCheat.Invulnerable"/>).</summary>
    public bool Invulnerable(int player) => Has(player, SandboxCheat.Invulnerable);

    public ulong ComputeHash()
    {
        ulong h = 0xA0761D6478BD642FUL;
        foreach (byte c in _cheats) h = (h ^ c) * 0x100000001B3UL;
        return h;
    }

    /// <summary>Carries out a test mode command; false if it is not one (outside test mode, they do nothing).</summary>
    internal bool Apply(Command command)
    {
        switch (command)
        {
            case SandboxUnitCommand c: if (On) Units(c); return true;
            case SandboxBuildingCommand c: if (On) Building(c); return true;
            case SandboxTreeCommand c: if (On) Tree(c); return true;
            case SandboxBlocksCommand c: if (On) Blocks(c); return true;
            case SandboxLairCommand c: if (On) Lair(c); return true;
            case SandboxBloodCommand c: if (On) Blood(c); return true;
            case SandboxWeatherCommand c:
                if (On && _sim.DayNight && Enum.IsDefined(c.Weather))
                    _sim.Conditions.SetWeather(c.Weather, (long)Math.Clamp(c.Hours, 1, 24 * 30) * Conditions.HourTicks, c.Weather);
                return true;
            case SandboxTimeCommand c: if (On && _sim.DayNight) _sim.SetClock(c.Minute); return true;
            case SandboxGoodsCommand c: if (On) Goods(c); return true;
            case SandboxTechCommand c: if (On) Tech(c); return true;
            case SandboxCheatCommand c:
                if (On && c.Owner >= 0 && c.Owner < _cheats.Length && Enum.IsDefined(c.Cheat))
                {
                    byte bit = (byte)(1 << (int)c.Cheat);
                    _cheats[c.Owner] = (byte)(c.On ? _cheats[c.Owner] | bit : _cheats[c.Owner] & ~bit);
                }
                return true;
            case SandboxUnitsCommand c: if (On) Act(c); return true;
            case SandboxClearCommand c: if (On) Clear(c); return true;
            default: return false;
        }
    }

    /// <summary>
    /// Each tick, before the economy: the cheats' work. Sites of players with instant building are all
    /// worked, training and research of players with instant training or research are done.
    /// </summary>
    internal void Step()
    {
        if (!On) return;
        bool any = false;
        foreach (byte c in _cheats) any |= c != 0;
        if (!any) return;
        foreach (var b in _sim.Buildings.All)
        {
            if (b.State == BuildingState.Site && Has(b.Owner, SandboxCheat.InstantBuild)) b.Work = b.Def.BuildTicks;
            else if (b.Upgrade != null && Has(b.Owner, SandboxCheat.InstantBuild)) b.UpgradeWork = b.Upgrade.BuildTicks;
            else if (b.Active && b.Queue.Count > 0 && Has(b.Owner, SandboxCheat.InstantTrain))
                b.TrainTicks = Math.Max(b.TrainTicks, _sim.Content.Units[b.Queue[0]].TrainTicks);
        }
        for (int p = 0; p < _cheats.Length; p++)
        {
            if (!Has(p, SandboxCheat.InstantResearch)) continue;
            var pe = _sim.Economy.Player(p);
            if (pe.Researching >= 0)
                pe.ResearchTicks = Math.Max(pe.ResearchTicks, _sim.Content.Techs[pe.Researching].ResearchTicks - 1);
        }
    }

    private static bool ValidOwner(int owner) => owner >= 0 && owner <= Simulation.WildPlayer;

    // Spots for a group of units: the column itself, then rings around it, every other column so they have room.
    private void Units(SandboxUnitCommand c)
    {
        if (!ValidOwner(c.Owner) || !_sim.Content.TryGetUnit(c.Unit, out var def)) return;
        int count = Math.Clamp(c.Count, 1, MaxUnits), placed = 0;
        for (int r = 0; placed < count && r <= 2 * MaxRadius; r += 1)
        for (int dz = -r; dz <= r && placed < count; dz++)
        for (int dx = -r; dx <= r && placed < count; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || (dx + dz) % 2 != 0) continue;
            int x = c.X + dx, z = c.Z + dz;
            if (!_sim.World.InBounds(x, 0, z)) continue;
            if (_sim.SpawnUnit(c.Owner, def.Id, x, z) > 0) placed++;
        }
    }

    private void Building(SandboxBuildingCommand c)
    {
        if (!ValidOwner(c.Owner) || !_sim.Content.TryGetBuilding(c.Building, out var def)) return;
        var b = _sim.Economy.PlaceFinished(c.Owner, def, c.X, c.Z);
        // A town hall for a player who has none brings them into the match.
        if (b != null && def.Kind == BuildingKind.TownHall) _sim.Match.Join(c.Owner);
    }

    private void Tree(SandboxTreeCommand c)
    {
        var w = _sim.World;
        if (!Enum.IsDefined(c.Kind) || !w.InBounds(c.X, 0, c.Z)) return;
        int g = w.SurfaceHeight(c.X, c.Z);
        if (g < 0 || !BlockInfo.CanStandOn(w.Get(c.X, g, c.Z)) || w.Get(c.X, g + 1, c.Z) != BlockType.Air) return;
        if (_sim.Buildings.AtColumn(c.X, c.Z) != null) return;
        uint h = DeterministicRandom.Hash(c.X, c.Z, _sim.Seed + 33);
        Trees.Grow(w, c.X, g, c.Z, 4 + (int)(h & 1), (int)(h >> 1 & 3), c.Kind, _sim.Seed);
    }

    private void Blocks(SandboxBlocksCommand c)
    {
        var w = _sim.World;
        if (!Enum.IsDefined(c.Block) || c.Block == BlockType.Bedrock || BlockInfo.IsBuilt(c.Block)) return;
        int x1 = Math.Max(0, Math.Min(c.X1, c.X2)), x2 = Math.Min(w.SizeX - 1, Math.Max(c.X1, c.X2));
        int y1 = Math.Max(1, Math.Min(c.Y1, c.Y2)), y2 = Math.Min(w.SizeY - 1, Math.Max(c.Y1, c.Y2));
        int z1 = Math.Max(0, Math.Min(c.Z1, c.Z2)), z2 = Math.Min(w.SizeZ - 1, Math.Max(c.Z1, c.Z2));
        if (x1 > x2 || y1 > y2 || z1 > z2 || (long)(x2 - x1 + 1) * (y2 - y1 + 1) * (z2 - z1 + 1) > MaxBlocks) return;
        // Water at or under the sea's level runs into the open ground next to it, up to that level; higher up it
        // stays where it is put (it would flood the land around for ever, since water never runs out).
        int sea = RegionGenerator.WaterLevel(w.SizeY);
        for (int y = y1; y <= y2; y++)
        for (int z = z1; z <= z2; z++)
        for (int x = x1; x <= x2; x++)
        {
            if (w.Get(x, y, z) == BlockType.Bedrock || _sim.Buildings.At(x, y, z) != null) continue;
            w.Set(x, y, z, c.Block);
            if (c.Block == BlockType.Water && y <= sea) _sim.Physics.WakeWater(x, y, z);
        }
    }

    private void Lair(SandboxLairCommand c)
    {
        if (!_sim.Content.TryGetUnit(c.Monster, out var def) || def.Lair == null) return;
        _sim.Monsters.Place(def, c.X, c.Z);
    }

    private void Blood(SandboxBloodCommand c)
    {
        var w = _sim.World;
        int r = Math.Clamp(c.Radius, 0, MaxRadius);
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dz * dz > r * r) continue;
            int x = c.X + dx, z = c.Z + dz;
            if (!w.InBounds(x, 0, z)) continue;
            int top = w.SurfaceHeight(x, z);
            if (top >= 0) _sim.Blood.Spill(x, top, z, c.Amount, 0);
        }
    }

    private void Goods(SandboxGoodsCommand c)
    {
        if (!ValidOwner(c.Owner) || c.Amount <= 0) return;
        Building? store = _sim.Economy.TownHall(c.Owner);
        if (store == null)
            foreach (var b in _sim.Buildings.All)
                if (b.Owner == c.Owner && b.Active && b.Def.Storage)
                {
                    store = b;
                    break;
                }
        if (store == null) return;
        int amount = Math.Min(c.Amount, 1_000_000);
        for (int g = 0; g < store.Stock.Length; g++) store.Stock[g] = (int)Math.Min(int.MaxValue / 2, (long)store.Stock[g] + amount);
    }

    private void Tech(SandboxTechCommand c)
    {
        if (!ValidOwner(c.Owner)) return;
        foreach (var t in _sim.Content.Techs)
            if (c.Tech == "all" || t.Id == c.Tech) _sim.Economy.Grant(c.Owner, t);
    }

    private void Act(SandboxUnitsCommand c)
    {
        var u = U;
        foreach (int id in c.Units)
        {
            int i = u.IndexOf(id);
            if (i < 0) continue;
            var def = _sim.Content.Units[u.Def[i]];
            switch (c.Action)
            {
                case SandboxAction.Heal:
                    u.Hp[i] = Math.Max(u.Hp[i], _sim.Economy.MaxHp(u.Owner[i], def));
                    Array.Clear(u.Wounds, i * Body.Parts, Body.Parts);
                    u.StopBleeding(i);
                    u.Grievous[i] = 0;
                    u.Morale[i] = CombatSystem.MaxMorale;
                    break;
                case SandboxAction.Kill:
                    _sim.Combat.Smite(i);
                    break;
                case SandboxAction.Remove:
                    u.RemoveAt(i);
                    break;
                case SandboxAction.Bleed:
                    if (!def.Bleeds) break;
                    u.Bleeding[i] |= (byte)(1 << (int)BodyPart.Legs);
                    u.BleedLeft[i * Body.Parts + (int)BodyPart.Legs] = BleedSeconds;
                    break;
                case SandboxAction.Veteran:
                    u.Xp[i] = EvolutionSystem.MaxXp;
                    break;
            }
        }
    }

    private void Clear(SandboxClearCommand c)
    {
        int r = Math.Clamp(c.Radius, 0, MaxRadius);
        long r2 = (long)r * r;
        var u = U;
        for (int i = u.Count - 1; i >= 0; i--)
        {
            long dx = u.X[i].FloorToInt() - c.X, dz = u.Z[i].FloorToInt() - c.Z;
            if (dx * dx + dz * dz <= r2) u.RemoveAt(i);
        }
        var all = _sim.Buildings.All;
        for (int k = all.Count - 1; k >= 0; k--)
        {
            var b = all[k];
            if (b.Def.Kind == BuildingKind.Totem) continue;
            int d = b.ColumnDistance(c.X, c.Z);
            if ((long)d * d > r2) continue;
            _sim.Economy.Demolish(b);
        }
    }
}
