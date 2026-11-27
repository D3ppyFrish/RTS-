using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players and field hospitals: a smart computer player that knows Field hospital and has a barracks keeps
/// one field hospital out from its town hall toward the enemy; Classic puts up none.
/// </summary>
public class AiHospitalTests
{
    private const int Ground = 6;
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>A town in the west of a flat map with a barracks, and a rival's town hall in the east.</summary>
    private static (Simulation Sim, Building Hall) Town()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(192, 24, 192, Ground));
        sim.Fog = false;
        sim.Map.Starts.Add(new StartArea(0, 40, Ground + 1, 96));
        sim.Map.Starts.Add(new StartArea(1, 160, Ground + 1, 96));
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(0)!;
        for (int k = 0; k < 4; k++) Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 14 + 6 * k, 60));
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), 24, 130));
        for (int k = 0; k < 6; k++) sim.SpawnUnit(0, "worker", 30 + k, 110);
        foreach (var id in new[] { "farming", "field_dressings", "field_hospital" })
            sim.Economy.Grant(0, sim.Content.Techs.First(t => t.Id == id));
        return (sim, hall);
    }

    private static void Play(Simulation sim, AiPlayer ai, int seconds, Building hall)
    {
        for (int s = 0; s < seconds; s++)
        {
            foreach (var (good, n) in new[] { (Good.Food, 1500), (Good.Wood, 800), (Good.Stone, 600) })
                hall.Stock[(int)good] = Math.Max(hall.Stock[(int)good], n);
            for (int t = 0; t < Tps; t++)
            {
                ai.Think(sim, c => sim.Schedule(sim.Tick, c));
                sim.Step();
            }
        }
    }

    private static Building[] Hospitals(Simulation sim) =>
        sim.Buildings.All.Where(b => b.Owner == 0 && b.Def.Kind == BuildingKind.Infirmary).ToArray();

    [Theory]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ASmartComputerKeepsAFieldHospitalTowardTheEnemy(AiLevel level)
    {
        var (sim, hall) = Town();
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 120, hall);
        var hospital = Assert.Single(Hospitals(sim));
        Assert.True(hospital.Active, "the hospital is built");
        int hx = hall.X + hall.Def.SizeX / 2, hz = hall.Z + hall.Def.SizeZ / 2;
        int px = hospital.X + hospital.Def.SizeX / 2;
        Assert.True(px > hx + AiPlayer.HospitalReach / 2, $"hospital at {hospital.X},{hospital.Z} is east of the hall at {hx},{hz}, toward the enemy");
        Assert.True(Math.Abs(px - hx) <= AiPlayer.HospitalReach + AiPlayer.HospitalRoom + 4);

        Play(sim, ai, 30, hall); // one is enough
        Assert.Single(Hospitals(sim));
    }

    [Fact]
    public void ClassicPutsUpNoFieldHospital()
    {
        var (sim, hall) = Town();
        var ai = new AiPlayer(0, 1, AiLevel.Classic);
        Play(sim, ai, 60, hall);
        Assert.Empty(Hospitals(sim));
    }
}
