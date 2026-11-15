using Xunit;

namespace Rts.Sim.Tests;

public class SimulationTests
{
    private static Simulation RunScript(ulong seed)
    {
        var sim = new Simulation(seed, 64, 32, 64);
        var rng = new DeterministicRandom(seed * 7 + 1);
        for (int i = 0; i < 500; i++)
        {
            int x = rng.Range(0, 64), z = rng.Range(0, 64);
            int y = sim.World.SurfaceHeight(x, z);
            long tick = i / 5;
            if (i % 3 == 0) sim.Schedule(tick, new PlaceCommand(1, x, y + 1, z, BlockType.Stone));
            else sim.Schedule(tick, new DigCommand(0, x, y, z));
        }
        sim.Run(200);
        return sim;
    }

    [Fact]
    public void SameSeedAndCommandsGiveIdenticalState()
    {
        Assert.Equal(RunScript(42).StateHash(), RunScript(42).StateHash());
    }

    [Fact]
    public void DifferentSeedsGiveDifferentWorlds()
    {
        Assert.NotEqual(RunScript(1).StateHash(), RunScript(2).StateHash());
    }

    [Fact]
    public void DigRemovesBlockButNotBedrock()
    {
        var sim = new Simulation(5, 32, 32, 32);
        int top = sim.World.SurfaceHeight(10, 10);
        sim.Schedule(0, new DigCommand(0, 10, top, 10));
        sim.Schedule(0, new DigCommand(0, 10, 0, 10));
        sim.Step();
        Assert.Equal(BlockType.Air, sim.World.Get(10, top, 10));
        Assert.Equal(BlockType.Bedrock, sim.World.Get(10, 0, 10));
    }

    [Fact]
    public void PlaceOnlyIntoAir()
    {
        var sim = new Simulation(5, 32, 32, 32);
        int top = sim.World.SurfaceHeight(4, 4);
        var before = sim.World.Get(4, top, 4);
        sim.Schedule(0, new PlaceCommand(0, 4, top, 4, BlockType.Wood));
        sim.Schedule(0, new PlaceCommand(0, 4, 31, 4, BlockType.Wood));
        sim.Step();
        Assert.Equal(before, sim.World.Get(4, top, 4));
        Assert.Equal(BlockType.Wood, sim.World.Get(4, 31, 4));
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        var a = new Simulation(99, 48, 32, 48);
        var b = new Simulation(99, 48, 32, 48);
        Assert.Equal(a.World.ComputeHash(), b.World.ComputeHash());
    }
}
