using System;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Units underground are not seen from the surface; a player hears them close by, from their own units underground,
/// or with a listening post (see <see cref="VisionSystem"/>, <see cref="BuildingKind.ListeningPost"/>).
/// </summary>
public class ListeningPostTests
{
    private const int Ground = 10; // grass at 10, units on the surface stand at 11
    private const int Tunnel = 4;  // the tunnel's floor: units in it stand at 4, under rock from 6 to 10

    /// <summary>A flat world of deep rock with a tunnel two high dug along z = 40 from x = 20 to x = 60.</summary>
    private static Simulation Dug(bool fog = true)
    {
        var w = TestWorlds.Flat(96, 24, 96, Ground);
        TestWorlds.Box(w, 20, Tunnel, 40, 60, Tunnel + 1, 40, BlockType.Air);
        var sim = TestWorlds.Sim(w);
        sim.Fog = fog;
        return sim;
    }

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    [Fact]
    public void AUnitInATunnelIsNotSeenFromTheSurfaceUnlessSomeoneIsRightOverIt()
    {
        var sim = Dug();
        var vision = sim.Vision;
        int sapper = sim.SpawnUnit(1, "sapper", 40, 40, Tunnel);
        int above = sim.SpawnUnit(1, "spearman", 30, 30); // on the surface, as anyone's is
        int watcher = sim.SpawnUnit(0, "spearman", 40, 44);
        sim.Step();

        int s = sim.Units.IndexOf(sapper), a = sim.Units.IndexOf(above), w = sim.Units.IndexOf(watcher);
        Assert.True(vision.Underground(s));
        Assert.False(vision.Underground(a) || vision.Underground(w));
        Assert.True(vision.Sees(0, 40, 40)); // the column is in sight...
        Assert.False(vision.SeesUnit(0, s)); // ...but what is under it is not
        Assert.True(vision.SeesUnit(0, a) == vision.Sees(0, 30, 30));
        Assert.True(vision.SeesUnit(1, s)); // its own player always sees it

        // Standing right over it (within two columns), the watcher hears it digging.
        int close = sim.SpawnUnit(0, "spearman", 41, 41);
        sim.Run(VisionSystem.UpdateTicks);
        Assert.True(vision.Hears(0, 40, 40));
        Assert.True(vision.SeesUnit(0, sim.Units.IndexOf(sapper)));
        Assert.False(vision.Hears(0, 40, 37)); // three columns from it, and four from the watcher, is too far
        Assert.True(sim.Units.IndexOf(close) >= 0);
    }

    [Fact]
    public void UnitsUndergroundSeeEachOtherAsOnTheSurface()
    {
        var sim = Dug();
        int sapper = sim.SpawnUnit(1, "sapper", 40, 40, Tunnel);
        int digger = sim.SpawnUnit(0, "sapper", 34, 40, Tunnel); // six columns along the tunnel, within its sight of 8
        sim.Step();
        Assert.True(sim.Vision.Underground(sim.Units.IndexOf(digger)));
        Assert.True(sim.Vision.SeesUnit(0, sim.Units.IndexOf(sapper)));
        Assert.True(sim.Vision.SeesUnit(1, sim.Units.IndexOf(digger)));
    }

    [Fact]
    public void AListeningPostHearsUnitsUndergroundBeyondItsSight()
    {
        var sim = Dug();
        var vision = sim.Vision;
        var post = Place(sim, 0, "listening_post", 50, 50); // columns 50..51, hearing 20 beyond them
        Assert.Equal((BuildingKind.ListeningPost, 20, 3), (post.Def.Kind, post.Def.Hearing, post.Def.Sight));
        int near = sim.SpawnUnit(1, "sapper", 40, 40, Tunnel); // 10 and 10 from the post: 200 is within 400
        int far = sim.SpawnUnit(1, "sapper", 21, 40, Tunnel);  // 29 columns west of it: too far
        int walker = sim.SpawnUnit(1, "spearman", 40, 36);     // on the surface, out of the post's sight
        sim.Step();

        Assert.False(vision.Sees(0, 40, 40)); // the post sees only 3 columns round it...
        Assert.True(vision.Hears(0, 40, 40)); // ...but hears much further under the ground
        Assert.True(vision.SeesUnit(0, sim.Units.IndexOf(near)));
        Assert.False(vision.SeesUnit(0, sim.Units.IndexOf(far)));
        Assert.False(vision.SeesUnit(0, sim.Units.IndexOf(walker))); // hearing is only for what is underground

        // A post still being built hears nothing.
        var sim2 = Dug();
        var store = Place(sim2, 0, "storehouse", 80, 10);
        store.Stock[(int)Good.Wood] = 100;
        store.Stock[(int)Good.Stone] = 100;
        int builder = sim2.SpawnUnit(0, "worker", 84, 84);
        int near2 = sim2.SpawnUnit(1, "sapper", 40, 40, Tunnel);
        sim2.Schedule(0, new BuildCommand(0, new[] { builder }, "listening_post", 50, 50));
        sim2.Run(VisionSystem.UpdateTicks + 1);
        var site = Assert.Single(sim2.Buildings.All, b => b.Def.Kind == BuildingKind.ListeningPost);
        Assert.False(site.Active);
        Assert.False(sim2.Vision.SeesUnit(0, sim2.Units.IndexOf(near2)));
    }

    [Fact]
    public void TreesBridgesAndOpenSkyAreNotUnderground()
    {
        var w = TestWorlds.Flat(64, 24, 64);
        TestWorlds.Tree(w, 20, 20, 4);
        w.Set(30, 6, 30, BlockType.Planks); // a building's floor or a bridge deck over it
        w.Set(40, 7, 40, BlockType.Ice);    // an ice bridge
        w.Set(50, 8, 50, BlockType.Stone);  // an overhang of rock
        Assert.False(VisionSystem.Covered(w, 21, 4, 20)); // under the tree's crown
        Assert.False(VisionSystem.Covered(w, 20, 4, 21));
        Assert.False(VisionSystem.Covered(w, 30, 4, 30));
        Assert.False(VisionSystem.Covered(w, 40, 4, 40));
        Assert.False(VisionSystem.Covered(w, 10, 4, 10));
        Assert.True(VisionSystem.Covered(w, 50, 4, 50));
        Assert.False(VisionSystem.Covered(w, 50, 7, 50)); // a block right over the head's level is not over the head
    }

    [Fact]
    public void WithoutFogOfWarUnitsUndergroundAreSeen()
    {
        var sim = Dug(fog: false);
        int sapper = sim.SpawnUnit(1, "sapper", 40, 40, Tunnel);
        sim.Step();
        int s = sim.Units.IndexOf(sapper);
        Assert.False(sim.Vision.Underground(s));
        Assert.True(sim.Vision.SeesUnit(0, s));
    }

    [Fact]
    public void OnlyAListeningPostHasHearing()
    {
        var files = ContentTests.Files();
        files["buildings.json"] = files["buildings.json"].Replace("\"sight\": 3, \"hearing\": 20", "\"sight\": 3");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("a listening post needs \"hearing\"", e.Message);

        files = ContentTests.Files();
        files["buildings.json"] = files["buildings.json"].Replace("\"buildTime\": 25, \"sight\": 14,", "\"buildTime\": 25, \"sight\": 14, \"hearing\": 8,");
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("only a listening post has \"hearing\"", e.Message);
    }
}
