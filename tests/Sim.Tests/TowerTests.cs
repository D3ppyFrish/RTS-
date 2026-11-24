using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Towers: put up alone or onto the player's own walls, posts along the edge of the deck, and what they give:
/// they see far, and those posted on them aim better.
/// </summary>
public class TowerTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4

    private static Simulation Flat(bool fog = false, bool dayNight = false)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        sim.Fog = fog;
        sim.DayNight = dayNight;
        return sim;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"no room for a {def} at {x}, {z}");

    private static Building[] Line(Simulation sim, int owner, string def, int x0, int x1, int z) =>
        Enumerable.Range(x0, x1 - x0 + 1).Select(x => Place(sim, owner, def, x, z)).ToArray();

    private static Building Store(Simulation sim, int owner, int stone, int wood)
    {
        var store = Place(sim, owner, "storehouse", 40, 10);
        store.Stock[(int)Good.Stone] = stone;
        store.Stock[(int)Good.Wood] = wood;
        return store;
    }

    private static void AssertSolid(Simulation sim, Building tower, BlockType block)
    {
        for (int x = tower.X; x <= tower.MaxX; x++)
        for (int z = tower.Z; z <= tower.MaxZ; z++)
        for (int y = 0; y < tower.Def.Height; y++)
            Assert.Equal(block, sim.World.Get(x, tower.Y + y, z));
    }

    [Fact]
    public void AWatchtowerIsBuiltOnOpenGroundSolidToItsDeck()
    {
        var sim = Flat();
        var store = Store(sim, 0, stone: 0, wood: 100);
        var def = sim.Content.Building("watchtower");
        int worker = sim.SpawnUnit(0, "worker", 24, 24);
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, def, 20, 20, out _));
        sim.Schedule(0, new BuildCommand(0, new[] { worker }, "watchtower", 20, 20));
        sim.Step();
        var tower = sim.Buildings.AtColumn(21, 21)!;
        Assert.Equal((def, BuildingState.Site, 20, 20), (tower.Def, tower.State, tower.X, tower.Z));
        Assert.Equal(100 - 30, store.Stock[(int)Good.Wood]);
        RunUntil(sim, () => tower.Active, 60 * Tps, "the worker to build the tower");
        // Three by three and seven high, timber all through.
        Assert.Equal((3, 3, 7), (def.SizeX, def.SizeZ, def.Height));
        AssertSolid(sim, tower, BlockType.Trestle);
        Assert.Equal(BlockType.Air, sim.World.Get(21, tower.Y + 7, 21));
        // It keeps no distance from walls, which run right up to it, but does from other buildings.
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, sim.Content.Building("barrier"), 23, 21, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, sim.Content.Building("house"), 23, 20, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, def, 20, 20, out _, checkPlayer: false));
        // The stone tower needs masonry.
        Assert.Equal(PlaceResult.Locked, sim.Economy.CanPlace(0, sim.Content.Building("stone_tower"), 40, 40, out _));
    }

    [Fact]
    public void ATowerIsPutOntoAWallWhichStandsUntilTheTowerIsDoneAndIsThenPartOfIt()
    {
        var sim = Flat();
        var store = Store(sim, 0, stone: 0, wood: 100);
        var def = sim.Content.Building("watchtower");
        var walls = Line(sim, 0, "wooden_wall", 20, 30, 30);
        var gate = Place(sim, 0, "wooden_gate", 31, 30);
        var enemys = Line(sim, 1, "barrier", 40, 42, 40);
        // An archer stands on the wall where the tower goes.
        int archer = sim.SpawnUnit(0, "archer", 25, 33);
        sim.Schedule(0, new PostCommand(0, new[] { archer }, walls[5].Id));
        RunUntil(sim, () => sim.Economy.WallUnder(Index(sim, archer)) == walls[5], 30 * Tps, "the archer to climb up");

        // On its owner's finished wall it fits, anywhere it covers some of it; not over a gate (nobody walks
        // through a tower), somebody else's wall, or a wall that is still going up.
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, def, 24, 29, out int level));
        Assert.Equal(walls[0].Y, level);
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, def, 24, 30, out _)); // to one side of the line
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, def, 30, 29, out _));
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, def, 40, 39, out _));
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(1, def, 24, 29, out _, checkPlayer: false));
        sim.Schedule(sim.Tick, new WallCommand(0, Array.Empty<int>(), "barrier", 50, 30, 52, 30));
        sim.Step();
        Assert.Equal(BuildingState.Site, sim.Buildings.AtColumn(51, 30)!.State);
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, def, 50, 29, out _));
        Assert.True(EconomySystem.TakesPlaceOf(0, def, 24, 29, walls[5]));
        Assert.False(EconomySystem.TakesPlaceOf(0, def, 24, 29, walls[7]));
        Assert.False(EconomySystem.TakesPlaceOf(0, def, 30, 29, gate));

        int wood = store.Stock[(int)Good.Wood];
        int worker = sim.SpawnUnit(0, "worker", 25, 26);
        sim.Schedule(sim.Tick, new BuildCommand(0, new[] { worker }, "watchtower", 24, 29));
        sim.Step();
        var tower = sim.Buildings.All.Single(b => b.Def == def);
        Assert.Equal(wood - 30, store.Stock[(int)Good.Wood]);
        Assert.Equal(BuildingState.Site, tower.State);
        // The wall under it stands as it was: the same buildings, the same blocks, no way through.
        sim.Run(3 * Tps);
        for (int k = 4; k <= 6; k++)
        {
            Assert.Same(walls[k], sim.Buildings.AtColumn(20 + k, 30));
            Assert.True(walls[k].Active);
            for (int y = 0; y < 4; y++) Assert.Equal(BlockType.Planks, sim.World.Get(20 + k, walls[k].Y + y, 30));
        }
        RunUntil(sim, () => tower.Active, 90 * Tps, "the worker to build the tower over the wall");
        // The archer was sent down so the tower could go up through where it stood.
        Assert.Equal(Ground + 1, sim.Units.Y[Index(sim, archer)]);
        // Now those three columns are the tower's: the walls are gone as buildings, and the tower is whole.
        for (int k = 4; k <= 6; k++)
        {
            Assert.Null(sim.Buildings.Get(walls[k].Id));
            Assert.Same(tower, sim.Buildings.AtColumn(20 + k, 30));
        }
        AssertSolid(sim, tower, BlockType.Trestle);
        Assert.Equal(tower.MaxHealth, tower.Health);
        // The wall runs into it on both sides with no gap.
        Assert.Same(walls[3], sim.Buildings.AtColumn(23, 30));
        Assert.Same(walls[7], sim.Buildings.AtColumn(27, 30));
        Assert.Equal(8, sim.Buildings.All.Count(b => b.Owner == 0 && b.Def.Kind == BuildingKind.Wall && b.Active));
        int enemy = sim.SpawnUnit(1, "spearman", 25, 40);
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { enemy }, 25, 20));
        // (The line is open at its west end; what matters is that nothing goes through where the tower stands.)
        for (int t = 0; t < 10 * Tps; t++)
        {
            sim.Step();
            int i = Index(sim, enemy);
            Assert.False(tower.Covers(sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt()) && sim.Units.Y[i] <= Ground + 2, "an enemy inside the tower");
        }
    }

    [Fact]
    public void ATowerPlannedOverAWallIsStartedAndBuiltLikeAnyPlan()
    {
        var sim = Flat();
        var store = Store(sim, 0, stone: 0, wood: 0);
        var walls = Line(sim, 0, "wooden_wall", 20, 30, 30);
        int worker = sim.SpawnUnit(0, "worker", 25, 26);
        sim.Schedule(0, new PlanCommand(0, "watchtower", 24, 29));
        sim.Step();
        var plan = sim.Buildings.All.Single(b => b.Def.IsTower);
        Assert.Equal(BuildingState.Planned, plan.State);
        // A plan changes nothing: the wall is the building on those columns, and whole.
        Assert.Same(walls[5], sim.Buildings.AtColumn(25, 30));
        Assert.Equal(PlaceResult.CannotAfford, sim.Economy.CanStart(plan));
        store.Stock[(int)Good.Wood] = 30;
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanStart(plan));
        sim.Schedule(sim.Tick, new WorkCommand(0, new[] { worker }, plan.Id));
        sim.Step();
        Assert.Equal(BuildingState.Site, plan.State);
        Assert.Equal(0, store.Stock[(int)Good.Wood]);
        for (int y = 0; y < 4; y++) Assert.Equal(BlockType.Planks, sim.World.Get(25, walls[5].Y + y, 30));
        RunUntil(sim, () => plan.Active, 90 * Tps, "the worker to build the planned tower over the wall");
        AssertSolid(sim, plan, BlockType.Trestle);
        Assert.Null(sim.Buildings.Get(walls[5].Id));
        Assert.Same(plan, sim.Buildings.AtColumn(25, 30));
    }

    [Fact]
    public void AStoneTowerTakesAWatchtowersPlaceAndStandsAcrossAThickWall()
    {
        var sim = Flat();
        var wood = Place(sim, 0, "watchtower", 20, 20);
        var stone = sim.Content.Building("stone_tower");
        Assert.Equal((5, 5, 9), (stone.SizeX, stone.SizeZ, stone.Height));
        // A tower takes the place of a smaller one of its owner's that is wholly inside it; never of one as big.
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, stone, 19, 19, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, stone, 18, 18, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, stone, 17, 19, out _, checkPlayer: false)); // not wholly inside
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, wood.Def, 20, 20, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(1, stone, 19, 19, out _, checkPlayer: false));
        var tower = Place(sim, 0, "stone_tower", 19, 19);
        Assert.Null(sim.Buildings.Get(wood.Id));
        Assert.Same(tower, sim.Buildings.AtColumn(21, 21));
        AssertSolid(sim, tower, BlockType.Rampart);
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, wood.Def, 20, 20, out _, checkPlayer: false));

        // Across a stone wall three columns thick it stands out a column on each side, and three levels over it.
        var line = Line(sim, 0, "stone_wall", 40, 50, 40);
        var faces = Line(sim, 0, "stone_facing", 40, 50, 39).Concat(Line(sim, 0, "stone_facing", 40, 50, 41)).ToArray();
        int before = sim.Buildings.All.Count();
        var across = Place(sim, 0, "stone_tower", 43, 38);
        Assert.Equal(before - 15 + 1, sim.Buildings.All.Count());
        Assert.All(line.Concat(faces), w => Assert.Equal(w.X is >= 43 and <= 47, sim.Buildings.Get(w.Id) == null));
        AssertSolid(sim, across, BlockType.Rampart);
        Assert.Equal(line[0].Y + 6 + 3, across.Y + across.Def.Height);
        Assert.Equal(BlockType.Rampart, sim.World.Get(42, line[0].Y + 5, 40));
        Assert.Equal(BlockType.Air, sim.World.Get(42, line[0].Y + 6, 40));
    }

    [Fact]
    public void ATowerOnAStepInTheGroundTakesInWallsALevelUpOrDown()
    {
        // East of x = 25 the ground is a level higher.
        Simulation Stepped()
        {
            var w = TestWorlds.Flat(96, 16, 96, Ground);
            for (int x = 25; x < 96; x++)
            for (int z = 0; z < 96; z++)
            {
                w.Set(x, Ground, z, BlockType.Soil);
                w.Set(x, Ground + 1, z, BlockType.Grass);
            }
            return TestWorlds.Sim(w);
        }
        var sim = Stepped();
        var walls = Line(sim, 0, "wooden_wall", 20, 30, 30);
        Assert.Equal(Ground + 1, walls[4].Y);
        Assert.Equal(Ground + 2, walls[5].Y);
        // Its middle on the low side: the high columns are cut down to its level, wall and all.
        var low = Place(sim, 0, "watchtower", 23, 29);
        Assert.Equal(Ground + 1, low.Y);
        AssertSolid(sim, low, BlockType.Trestle);
        Assert.Equal(BlockType.Air, sim.World.Get(25, low.Y + 7, 30)); // what the higher wall had above the tower's top is gone
        Assert.Null(sim.Buildings.Get(walls[5].Id));
        // Its middle on the high side: the low column of wall under it is ground now.
        var high = Place(sim, 0, "watchtower", 33, 29);
        Assert.Equal(Ground + 2, high.Y);
        var sim2 = Stepped();
        var walls2 = Line(sim2, 0, "wooden_wall", 20, 30, 30);
        var over = Place(sim2, 0, "watchtower", 24, 29);
        Assert.Equal(Ground + 2, over.Y);
        AssertSolid(sim2, over, BlockType.Trestle);
        Assert.Null(sim2.Buildings.Get(walls2[4].Id));
        Assert.True(BlockInfo.IsSolid(sim2.World.Get(24, Ground + 1, 30)));
        Assert.False(BlockInfo.IsBuilt(sim2.World.Get(24, Ground + 1, 30)));
    }

    [Fact]
    public void ArchersArePostedAlongTheEdgeOfATowersDeck()
    {
        var sim = Flat();
        var tower = Place(sim, 0, "watchtower", 20, 20);
        Assert.Equal(8, sim.Economy.FreePosts(tower));
        Assert.False(sim.Economy.PostFree(tower, 21, 21)); // the middle of the deck
        var archers = Enumerable.Range(0, 9).Select(k => sim.SpawnUnit(0, "archer", 18 + k % 5 * 2, 25 + k / 5)).ToArray();
        int spear = sim.SpawnUnit(0, "spearman", 21, 27);
        sim.Schedule(0, new PostCommand(0, archers.Append(spear).ToArray(), tower.Id));
        sim.Step();
        Assert.Equal(8, archers.Count(id => sim.Units.Job[Index(sim, id)] == UnitJob.Post));
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, spear)]);
        RunUntil(sim, () => archers.All(id => sim.Units.Job[Index(sim, id)] == UnitJob.None), 60 * Tps, "the archers to climb the tower");
        var up = archers.Where(id => sim.Economy.WallUnder(Index(sim, id)) == tower).ToArray();
        Assert.Equal(8, up.Length);
        var columns = up.Select(id => (sim.Units.X[Index(sim, id)].FloorToInt(), sim.Units.Z[Index(sim, id)].FloorToInt())).ToArray();
        Assert.Equal(8, columns.Distinct().Count());
        Assert.DoesNotContain((21, 21), columns);
        Assert.All(up, id =>
        {
            int i = Index(sim, id);
            Assert.Equal(tower.Y + 7, sim.Units.Y[i]);
            Assert.True(sim.Combat.Holding(i));
            Assert.Equal(50, sim.Combat.Aim(i));
        });
        Assert.Equal(0, sim.Economy.FreePosts(tower));
        // Sent somewhere else, they climb down and go.
        sim.Schedule(sim.Tick, new MoveCommand(0, up, 21, 40));
        RunUntil(sim, () => up.All(id => sim.Units.Y[Index(sim, id)] == Ground + 1 && sim.Units.Z[Index(sim, id)].FloorToInt() >= 34), 40 * Tps,
            "the archers to come down and walk off");
        Assert.Equal(0, sim.Combat.Aim(Index(sim, up[0])));
    }

    [Fact]
    public void SentUpATowerThoseOnTheWallBesideItGoTooAndSentToTheWallTheyComeBack()
    {
        var sim = Flat();
        for (int x = 20; x <= 27; x++)
        {
            Place(sim, 0, "stone_wall", x, 30);
            Place(sim, 0, "stone_facing", x, 29);
            Place(sim, 0, "stone_facing", x, 31);
        }
        var tower = Place(sim, 0, "stone_tower", 28, 28);
        var wall = sim.Buildings.AtColumn(26, 30)!;
        var ids = Enumerable.Range(0, 7).Select(k => sim.SpawnUnit(0, k < 2 ? "archer" : "mage", 22 + k, 35)).ToArray();
        Building? On(int id) => sim.Economy.WallUnder(Index(sim, id));
        // Three are posted on the wall, on the edges of its deck nearest the column named.
        sim.Schedule(0, new PostCommand(0, ids.Take(3).ToArray(), wall.Id));
        RunUntil(sim, () => ids.Take(3).All(id => On(id) is { Def.IsWall: true }), 30 * Tps, "three to climb the wall");
        // An order onto the wall again leaves them where they are: they stay anywhere along it.
        var before = ids.Take(3).Select(id => (sim.Units.X[Index(sim, id)], sim.Units.Z[Index(sim, id)])).ToArray();
        sim.Schedule(sim.Tick, new PostCommand(0, ids.Take(3).ToArray(), sim.Buildings.AtColumn(24, 30)!.Id));
        sim.Run(2 * Tps);
        Assert.Equal(before, ids.Take(3).Select(id => (sim.Units.X[Index(sim, id)], sim.Units.Z[Index(sim, id)])).ToArray());
        // All seven sent up the tower: those on the wall climb down and go up it with the rest.
        sim.Schedule(sim.Tick, new PostCommand(0, ids, tower.Id));
        RunUntil(sim, () => ids.All(id => On(id) == tower), 40 * Tps, "all seven to stand on the tower");
        Assert.All(ids, id => Assert.Equal(tower.Y + 9, sim.Units.Y[Index(sim, id)]));
        // Sent up it again they stay, and sent to the wall they come down to it.
        sim.Schedule(sim.Tick, new PostCommand(0, ids, tower.Id));
        sim.Run(2 * Tps);
        Assert.All(ids, id => Assert.Same(tower, On(id)));
        sim.Schedule(sim.Tick, new PostCommand(0, ids, wall.Id));
        RunUntil(sim, () => ids.All(id => On(id) is { Def.IsWall: true }), 60 * Tps, "all seven to stand on the wall");
    }

    [Fact]
    public void ATowerSeesFar()
    {
        var sim = Flat(fog: true);
        var tower = Place(sim, 0, "watchtower", 40, 40);
        var hall = sim.Content.Building("town_hall");
        Assert.Equal(14, tower.Def.Sight);
        Assert.Equal(18, sim.Content.Building("stone_tower").Sight);
        Assert.True(tower.Def.Sight > hall.Sight);
        sim.Run(VisionSystem.UpdateTicks);
        // 14 columns beyond its walls; a house sees 6.
        Assert.True(sim.Vision.Sees(0, 42 + 14, 41));
        Assert.False(sim.Vision.Sees(0, 42 + 15, 41));
        Assert.True(sim.Vision.Sees(0, 41, 40 - 14));
        var house = Place(sim, 0, "house", 70, 70);
        sim.Run(VisionSystem.UpdateTicks);
        Assert.True(sim.Vision.Sees(0, house.MaxX + 6, 71));
        Assert.False(sim.Vision.Sees(0, house.MaxX + 7, 71));
    }

    [Fact]
    public void ShotsFromATowerMissLessInTheDarkAndLandOnTheHeadMoreOften()
    {
        // In a storm at night nearly half the shots miss; a tower's aim lets that share of the misses land.
        var sim = Flat(dayNight: true);
        sim.Conditions.SetWeather(Weather.Storm, 4L * Conditions.DayTicks);
        long midnight = (long)(1440 - Conditions.StartMinute) * Conditions.DayTicks / 1440;
        while (sim.Tick < midnight) sim.Step();
        int chance = sim.Conditions.HitChance(20, 20);
        Assert.True(chance < 100);
        int plain = 0, wood = 0, stone = 0, sure = 0;
        const int Shots = 4000;
        for (int id = 1; id <= Shots; id++)
        {
            if (sim.Conditions.ShotLands(id, 20, 20)) plain++;
            if (sim.Conditions.ShotLands(id, 20, 20, 50)) wood++;
            if (sim.Conditions.ShotLands(id, 20, 20, 75)) stone++;
            if (sim.Conditions.ShotLands(id, 20, 20, 100)) sure++;
            // A shot that lands anyway still lands with aim.
            Assert.True(!sim.Conditions.ShotLands(id, 20, 20) || sim.Conditions.ShotLands(id, 20, 20, 50));
        }
        int miss = 100 - chance;
        Assert.InRange(plain, Shots * (chance - 3) / 100, Shots * (chance + 3) / 100);
        Assert.InRange(wood, Shots * (100 - miss / 2 - 3) / 100, Shots * (100 - miss / 2 + 3) / 100);
        Assert.InRange(stone, Shots * (100 - miss / 4 - 3) / 100, Shots * (100 - miss / 4 + 3) / 100);
        Assert.Equal(Shots, sure);

        // Where a hit lands: a fifth of the aim in points more on the head, off the legs first and then the arms.
        int Heads(Body.Angle angle, int aimed) => Enumerable.Range(0, 100).Count(roll => Body.Pick(BodyPlan.Humanoid, angle, roll, aimed) == BodyPart.Head);
        int Part(BodyPart part, int aimed) => Enumerable.Range(0, 100).Count(roll => Body.Pick(BodyPlan.Humanoid, Body.Angle.FromAbove, roll, aimed) == part);
        Assert.Equal(Body.Odds(BodyPlan.Humanoid, BodyPart.Head) + Body.HighShift, Heads(Body.Angle.FromAbove, 0));
        Assert.Equal(Heads(Body.Angle.FromAbove, 0) + 10, Heads(Body.Angle.FromAbove, 50 / Body.AimShare));
        Assert.Equal(Heads(Body.Angle.FromAbove, 0) + 15, Heads(Body.Angle.FromAbove, 75 / Body.AimShare));
        Assert.Equal(0, Part(BodyPart.Legs, 15));
        Assert.Equal(Body.Odds(BodyPlan.Humanoid, BodyPart.Arms) - 5, Part(BodyPart.Arms, 15));
        Assert.Equal(Body.Odds(BodyPlan.Humanoid, BodyPart.Torso), Part(BodyPart.Torso, 15));
        // A beast has no arms to take from: never more than its legs' share.
        Assert.Equal(100, Enum.GetValues<BodyPart>().Sum(p => Enumerable.Range(0, 100).Count(roll => Body.Pick(BodyPlan.Beast, Body.Angle.FromAbove, roll, 40) == p)));
    }

    [Fact]
    public void AnArcherOnATowerHitsTheHeadMoreOftenThanOneOnAWall()
    {
        // The same archer and target, over the same ticks: from a tower and from a wall of the same height away.
        int Heads(string def)
        {
            var sim = Flat();
            var b = Place(sim, 0, def, 20, 20);
            int archer = sim.SpawnUnit(0, "archer", 21, 24);
            sim.Schedule(0, new PostCommand(0, new[] { archer }, b.Id));
            RunUntil(sim, () => sim.Economy.WallUnder(Index(sim, archer)) != null, 40 * Tps, "the archer to climb up");
            int target = sim.SpawnUnit(1, "spearman", 21, 30);
            int a = Index(sim, archer), t = Index(sim, target);
            int heads = 0;
            for (int k = 0; k < 2000; k++)
            {
                if (sim.Combat.Location(a, t) == BodyPart.Head) heads++;
                sim.Step();
                if (!sim.Units.Exists(target)) { target = sim.SpawnUnit(1, "spearman", 21, 30); }
                a = Index(sim, archer);
                t = Index(sim, target);
            }
            return heads;
        }
        int tower = Heads("watchtower"), wall = Heads("stone_wall");
        // 20 in a hundred from the high ground; 30 from the watchtower.
        Assert.InRange(wall, 2000 * 16 / 100, 2000 * 24 / 100);
        Assert.InRange(tower, 2000 * 26 / 100, 2000 * 34 / 100);
    }

    [Fact]
    public void TowersAreLoggedAndReplayTheSame()
    {
        var log = new CommandLog { Seed = 1, SizeX = 96, SizeY = 16, SizeZ = 96 };
        Command[] commands = { new BuildCommand(0, new[] { 3 }, "watchtower", 24, 29), new PostCommand(0, new[] { 5 }, 8) };
        foreach (var c in commands) log.Add(5, c);
        log.Ticks = 10;
        Assert.Equal(commands, CommandLog.Parse(log.ToString()).Commands.Select(c => c.Command));

        ulong Play()
        {
            var sim = Flat();
            Store(sim, 0, stone: 100, wood: 200);
            var walls = Line(sim, 0, "wooden_wall", 20, 30, 30);
            var workers = new[] { sim.SpawnUnit(0, "worker", 22, 26), sim.SpawnUnit(0, "worker", 26, 26) };
            var archers = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(0, "archer", 22 + 2 * k, 34)).ToArray();
            sim.Schedule(0, new BuildCommand(0, new[] { workers[0] }, "watchtower", 24, 29));
            sim.Schedule(0, new BuildCommand(0, new[] { workers[1] }, "watchtower", 40, 40));
            sim.Schedule(0, new PostCommand(0, archers, walls[5].Id));
            sim.Run(60 * Tps);
            var tower = sim.Buildings.AtColumn(25, 30)!;
            Assert.True(tower.Def.IsTower && tower.Active);
            sim.Schedule(sim.Tick, new PostCommand(0, archers, tower.Id));
            sim.Run(20 * Tps);
            Assert.Contains(archers, id => sim.Economy.WallUnder(Index(sim, id)) == tower);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
