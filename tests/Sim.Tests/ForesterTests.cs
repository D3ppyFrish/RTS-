using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>The forester's lodge: workers there plant saplings round it, which grow into trees.</summary>
public class ForesterTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    private static (Simulation Sim, Building Lodge, int[] Workers) Lodge(int treeScale = 1, Action<VoxelWorld>? setup = null)
    {
        var w = TestWorlds.Flat(64, treeScale == 1 ? 16 : 32, 64, Ground);
        w.TreeScale = treeScale;
        setup?.Invoke(w);
        var sim = TestWorlds.Sim(w);
        sim.Economy.PlaceFinished(0, sim.Content.Building("town_hall"), 4, 4);
        var lodge = sim.Economy.PlaceFinished(0, sim.Content.Building("forester_lodge"), 30, 30)!;
        var workers = Enumerable.Range(0, 2).Select(k => sim.SpawnUnit(0, "worker", 28 + k, 28)).ToArray();
        sim.Schedule(0, new WorkCommand(0, workers, lodge.Id));
        return (sim, lodge, workers);
    }

    private static int Trunks(VoxelWorld w) =>
        Enumerable.Range(0, w.SizeX).Sum(x => Enumerable.Range(0, w.SizeZ).Count(z => BlockInfo.IsTrunk(w.Get(x, Ground + 1, z))));

    [Fact]
    public void TheDataHasAForestersLodgeThatPlants()
    {
        var lodge = ContentDb.Default.Building("forester_lodge");
        Assert.Equal(BuildingKind.Quarry, lodge.Kind);
        Assert.Equal(10, lodge.Plants);
        Assert.Empty(lodge.Works);
        Assert.Empty(lodge.Requires);
        Assert.Equal(0, ContentDb.Default.Building("quarry").Plants);
    }

    [Fact]
    public void ForestersPlantSaplingsThatGrowIntoTrees()
    {
        var (sim, lodge, workers) = Lodge();
        Assert.Equal(0, Trunks(sim.World));
        sim.Run(2 * Tps);
        Assert.All(workers, id => Assert.Equal(UnitJob.Quarry, sim.Units.Job[sim.Units.IndexOf(id)]));
        sim.Run(60 * Tps);
        // Two foresters, a round of 20 seconds each: a few saplings, each a leaf on open grass near the lodge.
        var saplings = sim.Economy.Saplings.ToList();
        Assert.InRange(saplings.Count, 3, 6);
        Assert.Equal(saplings.Count, sim.Economy.SaplingsOf(lodge));
        foreach (var p in saplings)
        {
            Assert.Equal(BlockType.Leaves, sim.World.Get(p.X, p.Y, p.Z));
            Assert.Equal(Ground + 1, p.Y);
            int gap = EconomySystem.BuildingSpacing;
            Assert.False(p.X >= lodge.X - gap && p.X <= lodge.MaxX + gap && p.Z >= lodge.Z - gap && p.Z <= lodge.MaxZ + gap, "its crown kept off the lodge");
            Assert.True(p.X >= lodge.X - lodge.Def.Plants && p.X <= lodge.MaxX + lodge.Def.Plants, "within its reach");
            foreach (var q in saplings.Where(q => q != p))
                Assert.True(Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Z - q.Z)) > EconomySystem.TreeSpacing, "spaced apart");
        }
        // Grown: trees stand where the first saplings were.
        sim.Run(EconomySystem.SaplingGrowTicks);
        var first = saplings[0];
        Assert.Equal(BlockType.Wood, sim.World.Get(first.X, Ground + 1, first.Z));
        Assert.DoesNotContain(sim.Economy.Saplings, p => p == first);
        Assert.True(sim.Economy.TreesGrown >= 1);
        foreach (var p in saplings.Where(p => p.Planted + EconomySystem.SaplingGrowTicks < sim.Tick - Tps))
            Assert.True(BlockInfo.IsTrunk(sim.World.Get(p.X, Ground + 1, p.Z)), $"grown: {p}");
    }

    [Fact]
    public void AGrownTreeIsFelledForWood()
    {
        var (sim, _, workers) = Lodge();
        sim.Run(25 * Tps + EconomySystem.SaplingGrowTicks + 2 * Tps);
        var w = sim.World;
        var tree = Enumerable.Range(0, w.SizeX).SelectMany(x => Enumerable.Range(0, w.SizeZ).Select(z => (x, z)))
            .First(c => w.Get(c.x, Ground + 1, c.z) == BlockType.Wood);
        var hall = sim.Buildings.All.First(b => b.Def.Id == "town_hall");
        int wood = hall.Stock[(int)Good.Wood];
        int cutter = sim.SpawnUnit(0, "worker", 20, 20);
        sim.Schedule(sim.Tick, new GatherCommand(0, new[] { cutter }, tree.x, Ground + 1, tree.z));
        sim.Run(90 * Tps);
        Assert.True(hall.Stock[(int)Good.Wood] > wood, "the planted tree gave wood");
    }

    [Fact]
    public void SaplingsKeepClearOfTreesAndTakeTheirKind()
    {
        // A pine stands just east of the lodge.
        var (sim, lodge, _) = Lodge(setup: w => TestWorlds.Tree(w, 37, 31, 6, Ground, BlockType.PineWood, BlockType.PineNeedles));
        sim.Run(62 * Tps);
        Assert.NotEmpty(sim.Economy.Saplings);
        foreach (var p in sim.Economy.Saplings)
        {
            Assert.Equal(TreeKind.Pine, p.Kind);
            Assert.Equal(BlockType.PineNeedles, sim.World.Get(p.X, p.Y, p.Z));
            Assert.True(Math.Max(Math.Abs(p.X - 37), Math.Abs(p.Z - 31)) > EconomySystem.TreeSpacing, $"clear of the pine: {p}");
        }
        sim.Run(EconomySystem.SaplingGrowTicks);
        Assert.Contains(Enumerable.Range(0, 64).SelectMany(x => Enumerable.Range(0, 64).Select(z => sim.World.Get(x, Ground + 1, z))),
            b => b == BlockType.PineWood);
    }

    [Fact]
    public void ASaplingDugUpIsGoneAndOneTrodOnWaits()
    {
        // Big trees: a sapling is one leaf in a corner of its trunk's square, and a unit can stand on the rest of it.
        var (sim, _, workers) = Lodge(treeScale: 2);
        sim.Run(25 * Tps);
        Assert.True(sim.Economy.Saplings.Count >= 2);
        var dug = sim.Economy.Saplings[0];
        var trod = sim.Economy.Saplings[1];
        // The first is pulled up; on the second's square a soldier stands. The foresters stop, so none takes their place.
        sim.World.Set(dug.X, dug.Y, dug.Z, BlockType.Air);
        sim.Schedule(sim.Tick, new StopCommand(0, workers));
        int guard = sim.SpawnUnit(0, "spearman", trod.X + 1, trod.Z + 1);
        Assert.True(guard > 0);
        sim.Run(2 * Tps);
        Assert.DoesNotContain(sim.Economy.Saplings, p => p == dug);
        sim.Run((int)(trod.Planted + EconomySystem.SaplingGrowTicks - sim.Tick) + 3 * Tps);
        Assert.Contains(sim.Economy.Saplings, p => p == trod);
        Assert.False(BlockInfo.IsTrunk(sim.World.Get(trod.X, Ground + 1, trod.Z)));
        // Once it steps off, the tree grows.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { guard }, 10, 50));
        sim.Run(15 * Tps);
        Assert.DoesNotContain(sim.Economy.Saplings, p => p == trod);
        Assert.True(BlockInfo.IsTrunk(sim.World.Get(trod.X, Ground + 1, trod.Z)));
        Assert.False(BlockInfo.IsTrunk(sim.World.Get(dug.X, Ground + 1, dug.Z)));
    }

    [Fact]
    public void NoSaplingIsPlantedUnderAUnit()
    {
        var (sim, lodge, _) = Lodge();
        // Workers stand on every column round the lodge, out to its reach, but one far corner.
        var w = sim.World;
        int r = lodge.Def.Plants;
        for (int z = lodge.Z - r; z <= lodge.MaxZ + r; z++)
        for (int x = lodge.X - r; x <= lodge.MaxX + r; x++)
            if (!(x == lodge.MaxX + r && z == lodge.MaxZ + r) && sim.Buildings.AtColumn(x, z) == null)
                sim.SpawnUnit(0, "worker", x, z);
        sim.Run(25 * Tps);
        foreach (var p in sim.Economy.Saplings)
            Assert.False(Enumerable.Range(0, sim.Units.Count).Any(i => sim.Units.X[i].FloorToInt() == p.X && sim.Units.Z[i].FloorToInt() == p.Z && sim.Units.Y[i] == p.Y),
                $"nobody stands in the sapling: {p}");
    }

    [Fact]
    public void OnBigTreeMapsSaplingsStandOnTheTreeGridAndGrowBig()
    {
        var (sim, _, _) = Lodge(treeScale: 2);
        sim.Run(25 * Tps);
        Assert.NotEmpty(sim.Economy.Saplings);
        var p = sim.Economy.Saplings[0];
        Assert.Equal(0, p.X % 2);
        Assert.Equal(0, p.Z % 2);
        sim.Run(EconomySystem.SaplingGrowTicks + 2 * Tps);
        // A trunk two columns square.
        for (int dz = 0; dz < 2; dz++)
        for (int dx = 0; dx < 2; dx++)
            Assert.True(BlockInfo.IsTrunk(sim.World.Get(p.X + dx, Ground + 1, p.Z + dz)));
    }

    [Fact]
    public void ForestersStopWhenThereIsNoRoomLeft()
    {
        // Stone all round: nowhere to plant.
        var (sim, lodge, workers) = Lodge(setup: w =>
        {
            for (int z = 0; z < w.SizeZ; z++)
            for (int x = 0; x < w.SizeX; x++)
                w.Set(x, Ground, z, BlockType.Stone);
        });
        sim.Run(90 * Tps);
        Assert.Empty(sim.Economy.Saplings);
        Assert.Equal(0, Trunks(sim.World));
    }

    [Fact]
    public void OnlyAQuarryPlantsAndThenGivesNoGoods()
    {
        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        string Edited(string old, string with)
        {
            var text = files["buildings.json"];
            int at = text.IndexOf(old, StringComparison.Ordinal);
            Assert.True(at >= 0);
            return text[..at] + with + text[(at + old.Length)..];
        }
        var orig = files["buildings.json"];
        files["buildings.json"] = Edited("{ \"id\": \"barrier\",", "{ \"id\": \"barrier\", \"plants\": 5,");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.StartsWith("buildings.json: building 'barrier': only a quarry \"plants\" trees"));
        files["buildings.json"] = orig;
        files["buildings.json"] = Edited("\"plants\": 10,", "\"plants\": 10, \"works\": { \"wood\": 2 },");
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("a quarry that \"plants\" trees gives no \"works\""));
    }
}
