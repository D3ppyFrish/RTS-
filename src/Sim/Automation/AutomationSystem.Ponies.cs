using Rts.Sim.Economy;

namespace Rts.Sim.Automation;

/// <summary>
/// Automatic pack ponies (Handcarts, #149; see EconomySystem.Ponies.cs): an idle one takes the nearest site of its
/// player's with goods due that nobody is bringing yet, and with none, the nearest workshop, smelter, quarry, mine
/// or orchard with goods to run and no pony at it.
/// </summary>
public sealed partial class AutomationSystem
{
    private Command? PonyJob(int i, int[] me)
    {
        var u = U;
        int owner = u.Owner[i];
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        Building? site = null, run = null;
        long siteD = long.MaxValue, runD = long.MaxValue;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != owner) continue;
            long d = b.CentreDistance2(x, z);
            if (b.State == BuildingState.Site)
            {
                if (d < siteD && Economy.WantsGoods(b)) { site = b; siteD = d; }
            }
            else if (d < runD && Economy.WantsPony(b)) { run = b; runD = d; }
        }
        var to = site ?? run;
        return to != null ? new WorkCommand(owner, me, to.Id) : null;
    }
}
