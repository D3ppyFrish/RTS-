using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Clan Kage's hidden villages (see "Clan Kage" in docs/rules.md, <c>VisionSystem.Villages.cs</c>): with fog of war on,
/// a rival sees their buildings only once one of its units or buildings has come close to one.
/// </summary>
public class HiddenVillageTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>A flat world with fog of war, player 0 playing Clan Kage and player 1 the Settlers.</summary>
    private static Simulation Foggy(int size = 96, string kage = "kage")
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.Fog = true;
        sim.SetFaction(0, kage);
        sim.SetFaction(1, "settlers");
        return sim;
    }

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static void Run(Simulation sim, int ticks)
    {
        for (int t = 0; t < ticks; t++) sim.Step();
    }

    private static (int X, int Z) Middle(Building b) => ((b.X + b.MaxX) / 2, (b.Z + b.MaxZ) / 2);

    [Fact]
    public void HiddenVillagesAreClanKagesAlone()
    {
        var content = ContentDb.Default;
        Assert.Equal(5, content.Faction("kage").HiddenVillage);
        foreach (var f in content.Factions)
            if (f.Id != "kage") Assert.Equal(0, f.HiddenVillage);
        // Not a tech: Clan Kage stay at six.
        Assert.Equal(6, content.Techs.Count(t => t.Faction?.Id == "kage"));
    }

    [Fact]
    public void ARivalSeesAKageBuildingOnlyOnceItsUnitsComeClose()
    {
        var sim = Foggy();
        var house = Place(sim, 0, "house", 40, 40);
        var (mx, mz) = Middle(house);
        // A rider 9 columns off the house: its side is within the rider's sight (10), but the house is not seen.
        int rider = sim.SpawnUnit(1, "rider", house.MaxX + 9, mz, Stand);
        Run(sim, VisionSystem.UpdateTicks * 2);
        Assert.True(sim.Vision.Sees(1, house.MaxX, mz));
        Assert.True(sim.Vision.HiddenFrom(1, house));
        Assert.False(sim.Vision.Found(1, house.Id));
        Assert.False(sim.Vision.HiddenFrom(0, house)); // its own

        // Within 5 columns of it: found, and it stays found once the rider has gone.
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { rider }, house.MaxX + 4, mz));
        for (int t = 0; t < 20 * Tps && sim.Vision.HiddenFrom(1, house); t++) sim.Step();
        Assert.False(sim.Vision.HiddenFrom(1, house));
        Assert.True(sim.Vision.Found(1, house.Id));
        Assert.Equal(1, sim.Vision.FoundBy(house));
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { rider }, house.MaxX + 30, mz));
        Run(sim, 10 * Tps);
        Assert.False(sim.Vision.HiddenFrom(1, house));
    }

    [Fact]
    public void WallsTowersAndOthersBuildingsAreNotHidden()
    {
        var sim = Foggy();
        var wall = Place(sim, 0, "wooden_wall", 40, 40);
        var tower = Place(sim, 0, "watchtower", 50, 40);
        var house = Place(sim, 1, "house", 40, 60); // a settler's, and player 0 is Clan Kage
        Assert.False(VisionSystem.Concealable(wall.Def));
        Assert.False(VisionSystem.Concealable(tower.Def));
        Assert.False(sim.Vision.HiddenFrom(1, wall));
        Assert.False(sim.Vision.HiddenFrom(1, tower));
        Assert.False(sim.Vision.HiddenFrom(0, house));

        // With fog of war off nothing is hidden.
        var open = Foggy();
        open.Fog = false;
        Assert.False(open.Vision.HiddenFrom(1, Place(open, 0, "house", 40, 40)));
    }

    [Fact]
    public void ARivalsBuildingCloseByFindsIt()
    {
        var sim = Foggy();
        var house = Place(sim, 0, "house", 40, 40);
        var far = Place(sim, 1, "house", house.MaxX + 8, house.Z);
        Run(sim, VisionSystem.UpdateTicks * 2);
        Assert.True(sim.Vision.HiddenFrom(1, house));
        Place(sim, 1, "house", house.X, house.MaxZ + 4);
        Run(sim, VisionSystem.UpdateTicks * 2);
        Assert.False(sim.Vision.HiddenFrom(1, house));
        Assert.NotNull(far);
    }

    [Fact]
    public void TheWorldARivalKnowsShowsTheGroundWithoutItUntilFound()
    {
        var sim = Foggy();
        var hall = Place(sim, 0, "town_hall", 40, 40);
        var memory = new WorldMemory(sim, 1);
        var block = hall.Shape.Blocks.Where(t => t.Y >= 0).OrderByDescending(t => t.X).First();
        int bx = hall.X + block.X, by = hall.Y + block.Y, bz = hall.Z + block.Z;
        Assert.Equal(block.Block, sim.World.Get(bx, by, bz));
        Assert.Equal(BlockType.Air, memory.World.Get(bx, by, bz));

        // In sight but not found: still not there.
        int scout = sim.SpawnUnit(1, "rider", hall.MaxX + 8, (hall.Z + hall.MaxZ) / 2, Stand);
        Run(sim, VisionSystem.UpdateTicks * 2);
        memory.Update();
        Assert.True(sim.Vision.Sees(1, bx, bz));
        Assert.Equal(BlockType.Air, memory.World.Get(bx, by, bz));

        // Found: it shows.
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { scout }, hall.MaxX + 3, (hall.Z + hall.MaxZ) / 2));
        for (int t = 0; t < 20 * Tps && sim.Vision.HiddenFrom(1, hall); t++) sim.Step();
        Run(sim, VisionSystem.UpdateTicks);
        memory.Update();
        Assert.Equal(block.Block, memory.World.Get(bx, by, bz));

        // A Kage house raised in the rider's sight but out of its reach does not show either.
        var house = Place(sim, 0, "house", hall.MaxX + 12, hall.Z);
        var hb = house.Shape.Blocks.Where(t => t.Y >= 0).OrderBy(t => t.X).First();
        Run(sim, VisionSystem.UpdateTicks * 2);
        memory.Update();
        Assert.True(sim.Vision.Sees(1, house.X + hb.X, house.Z + hb.Z));
        Assert.Equal(BlockType.Air, memory.World.Get(house.X + hb.X, house.Y + hb.Y, house.Z + hb.Z));
        memory.Detach();
    }

    [Fact]
    public void AnOrderToAttackAHiddenBuildingIsLost()
    {
        var sim = Foggy();
        var house = Place(sim, 0, "house", 40, 40);
        int spear = sim.SpawnUnit(1, "spearman", house.MaxX + 8, house.Z, Stand);
        Run(sim, VisionSystem.UpdateTicks * 2);
        sim.Schedule(sim.Tick, new AttackBuildingCommand(1, new[] { spear }, house.Id));
        Run(sim, 2);
        Assert.Equal(0, sim.Units.TargetBuilding[sim.Units.IndexOf(spear)]);

        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { spear }, house.MaxX + 3, house.Z));
        for (int t = 0; t < 20 * Tps && sim.Vision.HiddenFrom(1, house); t++) sim.Step();
        sim.Schedule(sim.Tick, new AttackBuildingCommand(1, new[] { spear }, house.Id));
        Run(sim, 2);
        Assert.Equal(house.Id, sim.Units.TargetBuilding[sim.Units.IndexOf(spear)]);
    }

    [Fact]
    public void WhoHasFoundWhatIsInTheStateHash()
    {
        // The same rider by the same house: of Clan Kage's, it finds it; of the Elves', there is nothing to find.
        ulong Hash(string faction)
        {
            var sim = Foggy(kage: faction);
            var house = Place(sim, 0, "house", 40, 40);
            sim.SpawnUnit(1, "rider", house.MaxX + 3, house.Z, Stand);
            Run(sim, VisionSystem.UpdateTicks * 2);
            Assert.Equal(faction == "kage", sim.Vision.Found(1, house.Id));
            return sim.Vision.ComputeHash();
        }
        Assert.NotEqual(Hash("kage"), Hash("elves"));
    }

    // --- Computer players ------------------------------------------------------------------------------

    private static readonly AiLevel[] FairLevels = { AiLevel.Easy, AiLevel.Normal, AiLevel.Hard };

    private static bool Play(Simulation sim, IReadOnlyList<AiPlayer> players, Func<bool> done, int maxTicks)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            foreach (var ai in players) ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
        return done();
    }

    [Fact]
    public void ComputerPlayersKnowOfAKageBuildingOnlyOnceTheyHaveFoundIt()
    {
        foreach (var level in FairLevels)
        {
            var sim = TestWorlds.Sim(TestWorlds.Flat(192, 16, 192, Ground));
            sim.SetFaction(0, "settlers");
            sim.SetFaction(1, "kage");
            sim.Map.Starts.Add(new StartArea(0, 20, Stand, 96));
            sim.Map.Starts.Add(new StartArea(1, 170, Stand, 96));
            sim.PlaceStartingBuildings();
            sim.Fog = true;
            sim.TestMode = true;
            var store = Place(sim, 1, "storehouse", 80, 96);
            // A house of its own 6 columns from the storehouse sees the storehouse's side, but not it.
            Place(sim, 0, "house", store.X - 9, store.Z);
            var ai = new AiPlayer(0, 1, level);
            var players = new[] { ai };
            Play(sim, players, () => false, 3 * Tps);
            Assert.True(sim.Vision.Sees(0, store.X, store.Z + 1), $"{level}: the storehouse's side is in sight");
            Assert.DoesNotContain(store.Id, ai.KnownBuildings);

            // Another raised within 5 columns of it: now it knows.
            Place(sim, 0, "house", store.X - 8, store.Z + 6);
            Assert.True(Play(sim, players, () => ai.KnownBuildings.Contains(store.Id), 5 * Tps), $"{level} to find the storehouse");
        }
    }

    [Fact]
    public void AScoutLookingForAHiddenVillageGoesRightUpToWhereItGuessesItIs()
    {
        // Easy and Normal scout from the start; Hard when it means to attack.
        foreach (var level in new[] { AiLevel.Easy, AiLevel.Normal })
        {
            var sim = TestWorlds.Sim(TestWorlds.Flat(192, 16, 192, Ground));
            sim.SetFaction(0, "settlers");
            sim.SetFaction(1, "kage");
            sim.Map.Starts.Add(new StartArea(0, 20, Stand, 96));
            // Not where it guesses (the mirror of its own start): it has to look round there, going up to each place.
            sim.Map.Starts.Add(new StartArea(1, 166, Stand, 130));
            sim.PlaceStartingBuildings();
            sim.Fog = true;
            for (int k = 0; k < 3; k++) sim.SpawnUnit(0, "rider", 14 + k, 106, Stand);
            var ai = new AiPlayer(0, 1, level);
            Assert.False(ai.KnowsEnemyHall);
            Assert.True(Play(sim, new[] { ai }, () => ai.KnowsEnemyHall, 90 * Tps), $"{level} to find the hidden town hall");
        }
    }
}
