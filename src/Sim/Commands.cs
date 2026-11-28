using System;
using System.Linq;

namespace Rts.Sim;

/// <summary>
/// Player orders. In lockstep multiplayer only commands are sent over the network;
/// every machine applies them on the same tick and must end in the same state.
/// </summary>
public abstract record Command(int PlayerId);

public sealed record DigCommand(int PlayerId, int X, int Y, int Z) : Command(PlayerId);

public sealed record PlaceCommand(int PlayerId, int X, int Y, int Z, BlockType Block) : Command(PlayerId);

/// <summary>Base for commands that act on a set of the player's units (by id).</summary>
public abstract record UnitsCommand(int PlayerId, int[] Units) : Command(PlayerId)
{
    public virtual bool Equals(UnitsCommand? other) =>
        other is not null && other.GetType() == GetType() && PlayerId == other.PlayerId && Units.AsSpan().SequenceEqual(other.Units);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(GetType());
        h.Add(PlayerId);
        foreach (var u in Units) h.Add(u);
        return h.ToHashCode();
    }
}

/// <summary>
/// Walk the units to block column (X, Z), standing at level Y (feet height; -1 means the top of the
/// column, which is what a click on the surface gives).
/// </summary>
public sealed record MoveCommand(int PlayerId, int[] Units, int X, int Z, int Y = -1) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(MoveCommand? other) => base.Equals(other) && X == other!.X && Z == other.Z && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X, Z, Y);
}

/// <summary>
/// Move the units in formation (see <see cref="Units.Formations"/>). With two different ends, they spread out
/// along the line from column (X1, Z1) to (X2, Z2), facing forward from it, melee troops in front and ranged
/// troops behind; with both ends the same, they move to that column keeping their shape. Either way they go
/// at the speed of the slowest of them, so the formation holds together. With <paramref name="Attack"/> it
/// is an attack-move: they fight what they meet on the way, then take their places. Y is the level to stand
/// at (-1 for the top of each column).
/// </summary>
public sealed record FormationMoveCommand(int PlayerId, int[] Units, int X1, int Z1, int X2, int Z2, bool Attack = false, int Y = -1)
    : UnitsCommand(PlayerId, Units)
{
    public bool Equals(FormationMoveCommand? other) =>
        base.Equals(other) && X1 == other!.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2 && Attack == other.Attack && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X1, Z1, X2, Z2, Attack, Y);
}

/// <summary>Cancel the units' orders.</summary>
public sealed record StopCommand(int PlayerId, int[] Units) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(StopCommand? other) => base.Equals(other);
    public override int GetHashCode() => base.GetHashCode();
}

/// <summary>
/// Workers dig, chop or pick block (X, Y, Z), carry what it gives to the nearest storage, and keep
/// gathering that kind of block nearby. Other units ignore it.
/// </summary>
public sealed record GatherCommand(int PlayerId, int[] Units, int X, int Y, int Z) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(GatherCommand? other) => base.Equals(other) && X == other!.X && Y == other.Y && Z == other.Z;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X, Y, Z);
}

/// <summary>
/// Workers clean the blood off the ground within <see cref="Economy.EconomySystem.CleanRadius"/> columns of
/// (X, Z), nearest stain first, until none is left there (see <see cref="World.BloodSystem"/>). Other units ignore it.
/// </summary>
public sealed record CleanCommand(int PlayerId, int[] Units, int X, int Z) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(CleanCommand? other) => base.Equals(other) && X == other!.X && Z == other.Z;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X, Z);
}

/// <summary>
/// Workers work at one of the player's buildings: build it if it is a site, farm a farm, supply a
/// smelter, or drop off what they carry at a storehouse.
/// </summary>
public sealed record WorkCommand(int PlayerId, int[] Units, int Building) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(WorkCommand? other) => base.Equals(other) && Building == other!.Building;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building);
}

/// <summary>
/// Place a building (by definition id) with its footprint's corner at column (X, Z), pay for it, and send
/// the workers among the units to build it. Y (optional) is the level to stand it at, for ground under other ground
/// (a great work in the deep caves): the standing level nearest it there; -1 for the top of the ground.
/// </summary>
public sealed record BuildCommand(int PlayerId, int[] Units, string Building, int X, int Z, int Y = -1) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(BuildCommand? other) =>
        base.Equals(other) && Building == other!.Building && X == other.X && Z == other.Z && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building, X, Z, Y);
}

/// <summary>
/// Plan a building (by definition id) with its footprint's corner at column (X, Z): it is marked out but not
/// paid for or built, so it may be placed before the player can afford it. A <see cref="WorkCommand"/> on the
/// plan starts it, once the player can pay (see <see cref="Economy.BuildingState.Planned"/>). Y (optional) is the
/// level to stand it at, as for <see cref="BuildCommand"/>.
/// </summary>
public sealed record PlanCommand(int PlayerId, string Building, int X, int Z, int Y = -1) : Command(PlayerId);

/// <summary>Remove one of the player's plans (a building not yet started).</summary>
public sealed record CancelPlanCommand(int PlayerId, int Building) : Command(PlayerId);

/// <summary>The units go to an enemy storehouse and take it over, goods and all, once nobody defends it.</summary>
public sealed record CaptureCommand(int PlayerId, int[] Units, int Building) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(CaptureCommand? other) => base.Equals(other) && Building == other!.Building;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building);
}

/// <summary>
/// Sabotage (Clan Kage's saboteurs, see <see cref="Content.TechEffectType.Sabotage"/>): those of the units that can and
/// are ready go to the enemy building and wreck its works once no soldier of its owner's is by it; then it does no work
/// for a while and goods in it spoil (see <c>Economy/EconomySystem.Sabotage.cs</c>).
/// </summary>
public sealed record SabotageCommand(int PlayerId, int[] Units, int Building) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(SabotageCommand? other) => base.Equals(other) && Building == other!.Building;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building);
}

/// <summary>The units go to an enemy storehouse and burn it with its goods, once nobody defends it.</summary>
public sealed record BurnCommand(int PlayerId, int[] Units, int Building) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(BurnCommand? other) => base.Equals(other) && Building == other!.Building;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building);
}

/// <summary>
/// Lay a wall (a building of kind wall or gate, by definition id) along the straight line from column
/// (X1, Z1) to (X2, Z2): a site on every column of the line that has room for one, each paid for as it is
/// placed, until the player runs out of goods. Columns with a building already on them are left as they
/// are, so a wall drawn through a gate keeps the gate. The workers among the units build the sites, a
/// column at a time, moving on along the wall as each is finished (see <see cref="Economy.EconomySystem"/>).
/// </summary>
public sealed record WallCommand(int PlayerId, int[] Units, string Building, int X1, int Z1, int X2, int Z2) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(WallCommand? other) =>
        base.Equals(other) && Building == other!.Building && X1 == other.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building, X1, Z1, X2, Z2);
}

/// <summary>
/// Upgrade one of the player's walls or gates (by id) to the building its type is upgraded to (see
/// <see cref="Economy.BuildingDef.Upgrade"/>), paying that building's cost; with <paramref name="Stretch"/>, every
/// wall and gate of the player's joined to it that is of the same tier too, nearest first, each paid for in
/// turn until the goods run out. A wall stands as it is until its builders have done the upgrade's work, then
/// it is the new building, whole. The workers among the units go and do the work (see <see cref="Economy.EconomySystem"/>).
/// </summary>
public sealed record UpgradeCommand(int PlayerId, int[] Units, int Building, bool Stretch = true) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(UpgradeCommand? other) => base.Equals(other) && Building == other!.Building && Stretch == other.Stretch;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building, Stretch);
}

/// <summary>
/// Post the ranged units among the units on top of one of the player's walls or gates (by id) that has a
/// walkway (see <see cref="Economy.BuildingDef.Walkway"/>): each takes the free column of walkway nearest to
/// that one, walks to its foot, climbs up and stands on top, one unit to a column. Up there it holds its
/// ground and shoots from the height; any other order brings it down again (see <see cref="Economy.EconomySystem"/>).
/// </summary>
public sealed record PostCommand(int PlayerId, int[] Units, int Building) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(PostCommand? other) => base.Equals(other) && Building == other!.Building;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building);
}

/// <summary>
/// Lay a bridge (a building of kind bridge, by definition id) along the straight line from column (X1, Z1)
/// to (X2, Z2): a level deck at the height of the higher end, on every column of the line too low or too
/// wet to walk at that height (see <see cref="Economy.EconomySystem.PlanBridge"/>), each paid for as it is
/// placed, until the player runs out of goods. The workers among the units build it from the ends inwards,
/// each deck column from ground (or deck) beside it (see <see cref="Economy.EconomySystem"/>).
/// </summary>
public sealed record BridgeCommand(int PlayerId, int[] Units, string Building, int X1, int Z1, int X2, int Z2) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(BridgeCommand? other) =>
        base.Equals(other) && Building == other!.Building && X1 == other.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building, X1, Z1, X2, Z2);
}

/// <summary>Lay road along the straight line from column (X1, Z1) to (X2, Z2), paying stone per block.</summary>
public sealed record RoadCommand(int PlayerId, int X1, int Z1, int X2, int Z2) : Command(PlayerId);

/// <summary>Queue a unit (by definition id) for training at one of the player's buildings, paying for it now.</summary>
public sealed record TrainCommand(int PlayerId, int Building, string Unit) : Command(PlayerId);

/// <summary>
/// Start researching a tech (by definition id) at the player's town hall, paying for it now. A player
/// researches one tech at a time, and only those whose prerequisites they have.
/// </summary>
public sealed record ResearchCommand(int PlayerId, string Tech) : Command(PlayerId);

/// <summary>The units attack an enemy unit (by id), following it wherever it goes, until it dies.</summary>
public sealed record AttackCommand(int PlayerId, int[] Units, int Target) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(AttackCommand? other) => base.Equals(other) && Target == other!.Target;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Target);
}

/// <summary>The units attack an enemy building (by id), knocking out its blocks until it collapses.</summary>
public sealed record AttackBuildingCommand(int PlayerId, int[] Units, int Building) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(AttackBuildingCommand? other) => base.Equals(other) && Building == other!.Building;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building);
}

/// <summary>
/// Like a move to column (X, Z) at level Y (-1 for the top), but the units fight the enemies they meet on
/// the way, then carry on.
/// </summary>
public sealed record AttackMoveCommand(int PlayerId, int[] Units, int X, int Z, int Y = -1) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(AttackMoveCommand? other) => base.Equals(other) && X == other!.X && Z == other.Z && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X, Z, Y);
}

/// <summary>
/// The units patrol between where each of them stands and column (X, Z) at level Y (-1 for the top): they
/// walk there, fighting the enemies they meet on the way as on an attack-move, then walk back to where they
/// started, and so on until they are given another order.
/// </summary>
public sealed record PatrolCommand(int PlayerId, int[] Units, int X, int Z, int Y = -1) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(PatrolCommand? other) => base.Equals(other) && X == other!.X && Z == other.Z && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X, Z, Y);
}

/// <summary>
/// The units guard one of their player's units (<paramref name="Unit"/>, by id) or buildings
/// (<paramref name="Building"/>, by id; the other is 0): they keep near it, follow it where it goes, and fight
/// the enemies that come near it or hit it, until they are given another order or it is gone. Units that
/// cannot fight ignore it.
/// </summary>
public sealed record GuardCommand(int PlayerId, int[] Units, int Unit, int Building = 0) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(GuardCommand? other) => base.Equals(other) && Unit == other!.Unit && Building == other.Building;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Unit, Building);
}

/// <summary>
/// Sappers dig a tunnel two blocks high from where they stand to column (X, Z), ending with their feet at
/// level Y, going at most one level up or down per column. Other units ignore it.
/// </summary>
public sealed record TunnelCommand(int PlayerId, int[] Units, int X, int Y, int Z) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(TunnelCommand? other) => base.Equals(other) && X == other!.X && Y == other.Y && Z == other.Z;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X, Y, Z);
}

/// <summary>
/// The workers and sappers among the units dig out the area of columns (X1, Z1) to (X2, Z2) down to level Y:
/// once it is done, units there stand at level Y. It is dug a layer at a time from the top, leaving a stair
/// along one edge (see <see cref="Economy.DigArea"/>). Workers carry what they dig to storage; sappers dig
/// faster and leave it. Other units ignore it.
/// </summary>
public sealed record DigAreaCommand(int PlayerId, int[] Units, int X1, int Z1, int X2, int Z2, int Y) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(DigAreaCommand? other) =>
        base.Equals(other) && X1 == other!.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2 && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X1, Z1, X2, Z2, Y);
}

/// <summary>
/// The workers and sappers among the units heap a ramp of earth from its foot, column (X1, Z1), up to the column
/// it leans on at the far end of the line toward (X2, Z2): a cliff, a hill or the top of a wall (see
/// <see cref="Economy.EconomySystem.PlanRamp"/>). Each block costs stone as it goes in. Other units ignore it.
/// </summary>
public sealed record RampCommand(int PlayerId, int[] Units, int X1, int Z1, int X2, int Z2) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(RampCommand? other) =>
        base.Equals(other) && X1 == other!.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X1, Z1, X2, Z2);
}

/// <summary>
/// The workers and sappers among the units raise a siege ladder with its foot in column (X1, Z1), leaning on the
/// column beside it toward (X2, Z2): a wall, a tower or a cliff 2 to <see cref="Units.Walk.MaxLadder"/> levels high
/// (see <see cref="Economy.EconomySystem.PlanLadder"/>). Each rung costs wood as it goes in. Other units ignore it.
/// </summary>
public sealed record LadderCommand(int PlayerId, int[] Units, int X1, int Z1, int X2, int Z2) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(LadderCommand? other) =>
        base.Equals(other) && X1 == other!.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X1, Z1, X2, Z2);
}

/// <summary>
/// The player's workers and sappers among <paramref name="Units"/> build a dam from bank column (X1, Z1) to bank
/// column (X2, Z2), across water or a dug channel (see <see cref="Economy.Dam"/>, <see cref="Economy.EconomySystem.PlanDam"/>).
/// </summary>
public sealed record DamCommand(int PlayerId, int[] Units, int X1, int Z1, int X2, int Z2) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(DamCommand? other) =>
        base.Equals(other) && X1 == other!.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X1, Z1, X2, Z2);
}

/// <summary>Opens (or shuts) the sluice of the player's finished dam <paramref name="Dam"/> (see <see cref="Economy.Dam"/>).</summary>
public sealed record SluiceCommand(int PlayerId, int Dam, bool Open) : Command(PlayerId);

/// <summary>
/// The workers among the units take the nearest needed job on their own whenever they have nothing to do
/// (<paramref name="On"/>), or wait for orders (off). See <see cref="Automation.AutomationSystem"/>.
/// </summary>
public sealed record AutomateCommand(int PlayerId, int[] Units, bool On) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(AutomateCommand? other) => base.Equals(other) && On == other!.On;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), On);
}

/// <summary>Like <see cref="AutomateCommand"/> for every worker the player has, and for every worker trained from now on.</summary>
public sealed record AutomateAllCommand(int PlayerId, bool On) : Command(PlayerId);

/// <summary>
/// Sets the priority list of the workers among the units: the kinds of work they take on their own when
/// automatic, in order (see <see cref="Automation.WorkPriorities"/>). <see cref="Automation.WorkPriorities.UseDefault"/>
/// puts them back on the player's default list. Their current job goes on; the list counts from their next one.
/// </summary>
public sealed record PriorityCommand(int PlayerId, int[] Units, ulong List) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(PriorityCommand? other) => base.Equals(other) && List == other!.List;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), List);
}

/// <summary>
/// Sets the player's default priority list, which every worker of theirs without a list of its own follows,
/// those trained later included. <see cref="Automation.WorkPriorities.UseDefault"/> puts back the standard one.
/// </summary>
public sealed record DefaultPriorityCommand(int PlayerId, ulong List) : Command(PlayerId);

/// <summary>
/// The building repeats its training (<paramref name="On"/>): each unit it finishes is queued again as soon
/// as the player can pay for it. Off stops that.
/// </summary>
public sealed record RepeatCommand(int PlayerId, int Building, bool On) : Command(PlayerId);

/// <summary>
/// Supply rules: sets how much of a good the player's storage building <paramref name="Building"/> should keep
/// (0 to <see cref="Economy.EconomySystem.MaxSupply"/>; 0 clears it). Workers on supply duty there carry the
/// good to it from the player's other storage, where it is not wanted as badly (see <see cref="Units.UnitJob.Supply"/>).
/// </summary>
public sealed record SupplyCommand(int PlayerId, int Building, Economy.Good Good, int Amount) : Command(PlayerId);

/// <summary>
/// Turns the player's rebuild orders on or off: with them on, each of their finished buildings that is
/// destroyed is put back where it stood, once it is safe and they can pay (see <see cref="Automation.AutomationSystem"/>).
/// </summary>
public sealed record RebuildCommand(int PlayerId, bool On) : Command(PlayerId);

/// <summary>
/// Puts a governor in charge of the player's town hall <paramref name="Building"/>, with a focus, or takes it
/// away (<see cref="Economy.GovernorFocus.None"/>). The governor runs the settlement around the hall: it makes
/// the idle workers there automatic, trains workers, puts up houses and farms as they are needed, and then
/// builds and trains for its focus (see <see cref="Automation.AutomationSystem"/>). It acts only through the
/// commands a player could give.
/// </summary>
public sealed record GovernCommand(int PlayerId, int Building, Economy.GovernorFocus Focus) : Command(PlayerId);

/// <summary>
/// Sets where the building's trained units walk when they step out: level Y (-1 for the top) of column
/// (X, Z). An X below 0, or a spot on the building itself, clears it (they stay at the door).
/// </summary>
public sealed record RallyCommand(int PlayerId, int Building, int X, int Z, int Y = -1) : Command(PlayerId);

/// <summary>Sets the units' stance (see <see cref="Units.UnitStance"/>). Workers, who fight only when ordered to, keep theirs.</summary>
public sealed record StanceCommand(int PlayerId, int[] Units, Units.UnitStance Stance) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(StanceCommand? other) => base.Equals(other) && Stance == other!.Stance;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Stance);
}

/// <summary>
/// The units fell the tree standing in column (<paramref name="X"/>, <paramref name="Z"/>), or the nearest one within
/// two columns of it, so that it falls the way (<paramref name="DirX"/>, <paramref name="DirZ"/>) (the larger of the two
/// picks the side): units whose player has the tech for it (see <see cref="Content.TechEffectType.Felling"/>). The tree
/// comes down on whatever is there, and a trunk that lands across a narrow gap or stream lies there as a log bridge.
/// </summary>
public sealed record FellCommand(int PlayerId, int[] Units, int X, int Z, int DirX, int DirZ) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(FellCommand? other) =>
        base.Equals(other) && X == other!.X && Z == other.Z && DirX == other.DirX && DirZ == other.DirZ;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X, Z, DirX, DirZ);
}

/// <summary>
/// The units set the tree standing in column (<paramref name="X"/>, <paramref name="Z"/>), or the nearest one within two
/// columns of it, alight: they walk to it and work at it a moment, then it burns and the fire spreads (see
/// <see cref="World.FireSystem"/>). Workers and soldiers on foot or horseback can; machines, boats, carts and caravans
/// cannot, and nobody can in rain or a storm.
/// </summary>
public sealed record KindleCommand(int PlayerId, int[] Units, int X, int Z) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(KindleCommand? other) => base.Equals(other) && X == other!.X && Z == other.Z;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X, Z);
}

/// <summary>
/// The player marks their finished building <paramref name="Building"/> for salvage and sends the command's workers (if
/// any) to take it down; automatic workers take it as building work. Once it is down, half its cost (in proportion to
/// the hit points it has left) and the goods it held go to the player's nearest other storage. With
/// <paramref name="Stop"/>, the mark is taken off instead and the work done so far is kept. Only for a player with the
/// tech for it (see <see cref="Content.TechEffectType.Salvage"/>, Craft guilds).
/// </summary>
public sealed record SalvageCommand(int PlayerId, int[] Units, int Building, bool Stop = false) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(SalvageCommand? other) => base.Equals(other) && Building == other!.Building && Stop == other.Stop;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building, Stop);
}

/// <summary>
/// The command's workers mend the player's damaged siege machine <paramref name="Target"/> (a unit id: a catapult or a
/// battering ram) in the field, for a share of its cost in materials. Only for a player with the tech for it (see
/// <see cref="Content.TechEffectType.FieldRepairs"/>, Craft guilds).
/// </summary>
public sealed record MendCommand(int PlayerId, int[] Units, int Target) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(MendCommand? other) => base.Equals(other) && Target == other!.Target;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Target);
}

/// <summary>
/// The command's workers repair the player's damaged building <paramref name="Building"/>: they mend it a block at a time,
/// putting back lost hit points and knocked-out blocks, for half its cost over all of its hit points, taken from storage.
/// Every player can repair; no tech is needed.
/// </summary>
public sealed record RepairCommand(int PlayerId, int[] Units, int Building) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(RepairCommand? other) => base.Equals(other) && Building == other!.Building;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building);
}

/// <summary>
/// The units sound the ground at column (<paramref name="X"/>, <paramref name="Z"/>): units whose player has the tech
/// for it (see <see cref="Content.TechEffectType.Prospecting"/>). After a while there, every ore, coal, gold and crystal
/// block round the spot, at any depth, shows to their player through the ground.
/// </summary>
public sealed record ProspectCommand(int PlayerId, int[] Units, int X, int Z) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(ProspectCommand? other) => base.Equals(other) && X == other!.X && Z == other.Z;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X, Z);
}

/// <summary>
/// The units take up a shield wall (<paramref name="Raise"/>) or lower it: line infantry whose player has the tech for
/// it (see <see cref="Content.TechEffectType.ShieldWall"/>). In the wall a unit stands still facing one way; shots from
/// the front do much less and riders charging its front are thrown back (see <see cref="Units.CombatSystem"/>). Any
/// order to go somewhere or fight drops it, and the unit needs a moment before it can walk.
/// </summary>
public sealed record ShieldWallCommand(int PlayerId, int[] Units, bool Raise) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(ShieldWallCommand? other) => base.Equals(other) && Raise == other!.Raise;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Raise);
}

/// <summary>
/// The badly hurt units molt (see <see cref="Content.TechEffectType.Molt"/>, the Deepfolk's Molting): each stops,
/// sheds its shell and heals back to full over a short time, then stays soft for a while, slower and with no armour
/// (see <see cref="Units.CombatSystem"/>). Units that cannot molt, or are not hurt enough, ignore it.
/// </summary>
public sealed record MoltCommand(int PlayerId, int[] Units) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(MoltCommand? other) => base.Equals(other);
    public override int GetHashCode() => base.GetHashCode();
}

/// <summary>
/// The war drummers among the units (see <see cref="Units.UnitDef.Drums"/>) beat the charge, those that are ready to:
/// for a few seconds their own side's units within earshot walk faster and the first blow each strikes deals more
/// (see <c>Units.CombatSystem.Drums.cs</c>). Then each drummer needs a while before it can beat the next.
/// </summary>
public sealed record ChargeCommand(int PlayerId, int[] Units) : UnitsCommand(PlayerId, Units);

/// <summary>
/// The units that can throw smoke bombs (see <see cref="Content.TechEffectType.SmokeBombs"/>, Clan Kage's) and are ready
/// to throw one at their feet: for a few seconds a cloud of smoke hangs there, and nobody in it is seen through the fog
/// of war by its enemies, nor fights or is fought unit to unit, so they can slip out of a losing fight
/// (see <c>Units/CombatSystem.Smoke.cs</c>). Then each thrower waits a while before it can throw the next. A thrower
/// already standing in a cloud of its side thrown this tick keeps its bomb.
/// </summary>
public sealed record SmokeCommand(int PlayerId, int[] Units) : UnitsCommand(PlayerId, Units);

/// <summary>
/// Shadow step (Clan Kage's shadow casters, see <see cref="Content.TechEffectType.ShadowStep"/>): the first ready caster
/// among the units takes itself and the others of them standing round it through the shadows to column
/// (<paramref name="X"/>, <paramref name="Z"/>), a spot its side has seen, out of the daylight (see
/// <c>Units/CombatSystem.ShadowStep.cs</c>). They come out of the shadows there at once, with no orders.
/// </summary>
public sealed record ShadowStepCommand(int PlayerId, int[] Units, int X, int Z) : UnitsCommand(PlayerId, Units);

/// <summary>
/// The player's tamed beasts among the units (<see cref="Units.UnitDef.Pennable"/>) walk into their beast pen
/// <paramref name="Building"/> and are kept there, as many as it has room for (see <c>Units/CombatSystem.Stampede.cs</c>).
/// </summary>
public sealed record PenCommand(int PlayerId, int[] Units, int Building) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(PenCommand? other) => base.Equals(other) && Building == other!.Building;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Building);
}

/// <summary>
/// The beasts kept in the player's beast pen <paramref name="Building"/> burst out as a stampede: they run to column
/// (X1, Z1) and on along the line to (X2, Z2), trampling the enemies in their way (see <c>Units/CombatSystem.Stampede.cs</c>).
/// </summary>
public sealed record StampedeCommand(int PlayerId, int Building, int X1, int Z1, int X2, int Z2) : Command(PlayerId);

/// <summary>
/// The player's crystal lens <paramref name="Building"/> gazes at column (X, Z): the land round it is in their sight for a
/// while (see <c>Economy/EconomySystem.Lens.cs</c>).
/// </summary>
public sealed record GazeCommand(int PlayerId, int Building, int X, int Z) : Command(PlayerId);

/// <summary>The beasts kept in the player's beast pen <paramref name="Building"/> are let out, free to take orders again.</summary>
public sealed record UnpenCommand(int PlayerId, int Building) : Command(PlayerId);

/// <summary>
/// The units that can take the form (a unit type id that evolves from theirs) do, now. See
/// <see cref="Units.EvolutionSystem"/>.
/// </summary>
public sealed record EvolveCommand(int PlayerId, int[] Units, string Form) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(EvolveCommand? other) => base.Equals(other) && Form == other!.Form;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Form);
}

/// <summary>
/// From now on the player's units of type <paramref name="Unit"/> take the form <paramref name="Form"/> on
/// their own as soon as they can (automation evolves them); null: the player picks each time.
/// </summary>
public sealed record AutoEvolveCommand(int PlayerId, string Unit, string? Form) : Command(PlayerId);

/// <summary>What a player whose faction tames monsters does with a monster their units beat (see <see cref="Units.MonsterSystem"/>).</summary>
public enum TameChoice : byte
{
    /// <summary>It joins the player's army as it is, back at full health.</summary>
    Recruit,
    /// <summary>The nearest of the player's units that can ride it climbs on and becomes its rider; the monster is gone.</summary>
    Ride,
}

/// <summary>The player's beaten monsters among the units are recruited or ridden (see <see cref="TameChoice"/>).</summary>
public sealed record TameCommand(int PlayerId, int[] Units, TameChoice Choice) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(TameCommand? other) => base.Equals(other) && Choice == other!.Choice;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Choice);
}

/// <summary>
/// The player uses a cleared monster den (a lair, by id) as their faction's den use says (see
/// <see cref="Content.DenUse"/> and <see cref="Units.MonsterSystem"/>): one of the units has to be near it.
/// </summary>
public sealed record DenCommand(int PlayerId, int[] Units, int Lair) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(DenCommand? other) => base.Equals(other) && Lair == other!.Lair;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Lair);
}

/// <summary>
/// The units work on the map's dusk tree as the player's faction does (see <see cref="Units.DuskTreeSystem"/>):
/// those that heal tend it until it is healed, or fighting units hack it down. The warden has to be gone.
/// </summary>
public sealed record DuskTreeCommand(int PlayerId, int[] Units) : UnitsCommand(PlayerId, Units);

/// <summary>
/// The units search one of the map's old ruins (by its index in <see cref="World.RuinSystem.Ruins"/>) for what was
/// left in it (see <see cref="World.RuinSystem"/>): they walk into it and search together until it is found.
/// </summary>
public sealed record SearchRuinCommand(int PlayerId, int[] Units, int Ruin) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(SearchRuinCommand? other) => base.Equals(other) && Ruin == other!.Ruin;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Ruin);
}

/// <summary>The Dusk Staff's bearer, if among the units, uses Dusk Bloom (see <see cref="Units.DuskTreeSystem"/>).</summary>
public sealed record BloomCommand(int PlayerId, int[] Units) : UnitsCommand(PlayerId, Units);

/// <summary>
/// One of the units that can (see <see cref="Units.SpellSystem"/>) raises a wall of earth along the line from
/// column (X1, Z1) to (X2, Z2), walking into range first if it has to.
/// </summary>
public sealed record EarthWallCommand(int PlayerId, int[] Units, int X1, int Z1, int X2, int Z2) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(EarthWallCommand? other) =>
        base.Equals(other) && X1 == other!.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X1, Z1, X2, Z2);
}

/// <summary>
/// One of the units that can (see <see cref="Units.SpellSystem"/>) freezes the water along the line from column
/// (X1, Z1) to (X2, Z2) into an ice bridge, walking into range first if it has to.
/// </summary>
public sealed record IceBridgeCommand(int PlayerId, int[] Units, int X1, int Z1, int X2, int Z2) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(IceBridgeCommand? other) =>
        base.Equals(other) && X1 == other!.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X1, Z1, X2, Z2);
}

/// <summary>
/// One of the units that can (see <see cref="Units.SpellSystem"/>) opens a chasm in the ground along the line
/// from column (X1, Z1) to (X2, Z2), walking into range first if it has to.
/// </summary>
public sealed record ChasmCommand(int PlayerId, int[] Units, int X1, int Z1, int X2, int Z2) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(ChasmCommand? other) =>
        base.Equals(other) && X1 == other!.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X1, Z1, X2, Z2);
}

/// <summary>
/// The units that can (see <see cref="Units.SpellSystem"/>) flood the ground along the line from column (X1, Z1) to
/// (X2, Z2): its top block of soft ground turns to shallow water for a while, walking into range first if they have to.
/// </summary>
public sealed record FloodCommand(int PlayerId, int[] Units, int X1, int Z1, int X2, int Z2) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(FloodCommand? other) =>
        base.Equals(other) && X1 == other!.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X1, Z1, X2, Z2);
}

/// <summary>
/// The units that can (see <see cref="Units.SpellSystem"/>) grow a forest along the line from column (X1, Z1) to
/// (X2, Z2): full-grown trees spring up on every other square of it, walking into range first if they have to.
/// </summary>
public sealed record GrowForestCommand(int PlayerId, int[] Units, int X1, int Z1, int X2, int Z2) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(GrowForestCommand? other) =>
        base.Equals(other) && X1 == other!.X1 && Z1 == other.Z1 && X2 == other.X2 && Z2 == other.Z2;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X1, Z1, X2, Z2);
}

/// <summary>
/// The caravans among the units trade with a city-state's town (by id): they carry loads of the good it
/// buys there from the player's storage and bring back the good it sells, until given another order.
/// </summary>
public sealed record TradeCommand(int PlayerId, int[] Units, int City) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(TradeCommand? other) => base.Equals(other) && City == other!.City;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), City);
}

/// <summary>
/// The player, if they hold the city-state's town (see <see cref="Economy.CityStateSystem"/>), hires one of its mercenaries (a unit type id whose
/// <see cref="Units.UnitDef.HiredFrom"/> is the city-state) at its town (by id), paying the unit's cost.
/// </summary>
public sealed record HireCommand(int PlayerId, int City, string Unit) : Command(PlayerId);

/// <summary>
/// The player offers a short truce to the rival player <paramref name="Other"/>, or accepts the rival's standing
/// offer, which starts it (see <see cref="TruceSystem"/>).
/// </summary>
public sealed record TruceCommand(int PlayerId, int Other) : Command(PlayerId);

/// <summary>
/// The player breaks off the truce with <paramref name="Other"/>, which then ends after a warning, or withdraws
/// their standing offer of one (see <see cref="TruceSystem"/>).
/// </summary>
public sealed record EndTruceCommand(int PlayerId, int Other) : Command(PlayerId);

/// <summary>
/// The player offers a trade deal to the rival player <paramref name="Other"/>, or accepts the rival's standing
/// offer, which starts it (see <see cref="TradeDealSystem"/>).
/// </summary>
public sealed record TradeDealCommand(int PlayerId, int Other) : Command(PlayerId);

/// <summary>
/// The player ends the trade deal with <paramref name="Other"/> at once, or withdraws their standing offer of one
/// (see <see cref="TradeDealSystem"/>).
/// </summary>
public sealed record EndTradeDealCommand(int PlayerId, int Other) : Command(PlayerId);

/// <summary>
/// The caravans among the units trade at a trade partner's town hall (building id <paramref name="Hall"/>; see
/// <see cref="TradeDealSystem"/>): they carry loads of food there from the player's storage and bring back gold,
/// until given another order or the deal ends.
/// </summary>
public sealed record MarketCommand(int PlayerId, int[] Units, int Hall) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(MarketCommand? other) => base.Equals(other) && Hall == other!.Hall;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Hall);
}

/// <summary>
/// The player gives up the match: they are out at once, as if their last town hall had fallen (see
/// <see cref="MatchSystem"/>). Their units and buildings stay where they are.
/// </summary>
public sealed record SurrenderCommand(int PlayerId) : Command(PlayerId);

// ---- Test mode (see Sandbox.SandboxSystem): carried out only in a match started in test mode ----

/// <summary>Test mode: <paramref name="Count"/> units of type <paramref name="Unit"/> for <paramref name="Owner"/> (a player, or <see cref="Simulation.WildPlayer"/>) on the ground around column (X, Z).</summary>
public sealed record SandboxUnitCommand(int PlayerId, int Owner, string Unit, int X, int Z, int Count = 1) : Command(PlayerId);

/// <summary>Test mode: a finished building (by definition id) of <paramref name="Owner"/>'s with its corner at column (X, Z), free, if it fits there.</summary>
public sealed record SandboxBuildingCommand(int PlayerId, int Owner, string Building, int X, int Z) : Command(PlayerId);

/// <summary>Test mode: a tree of the kind on the ground at column (X, Z).</summary>
public sealed record SandboxTreeCommand(int PlayerId, World.TreeKind Kind, int X, int Z) : Command(PlayerId);

/// <summary>
/// Test mode: every block in the box from (X1, Y1, Z1) to (X2, Y2, Z2) becomes <paramref name="Block"/> (air
/// clears it), except the bedrock floor and buildings' blocks. Water put down at or under the sea's level runs
/// into the open ground beside and under it (see <see cref="World.PhysicsSystem"/>); higher up it stays where it is put.
/// </summary>
public sealed record SandboxBlocksCommand(int PlayerId, BlockType Block, int X1, int Y1, int Z1, int X2, int Y2, int Z2) : Command(PlayerId);

/// <summary>Test mode: a lair of the monster (a unit type id that keeps a lair) at column (X, Z), with its pack.</summary>
public sealed record SandboxLairCommand(int PlayerId, string Monster, int X, int Z) : Command(PlayerId);

/// <summary>Test mode: blood spilled on every column within <paramref name="Radius"/> of (X, Z), <paramref name="Amount"/> on each.</summary>
public sealed record SandboxBloodCommand(int PlayerId, int X, int Z, int Radius, int Amount) : Command(PlayerId);

/// <summary>Test mode: the weather turns to <paramref name="Weather"/> now and stays for <paramref name="Hours"/> hours of the day.</summary>
public sealed record SandboxWeatherCommand(int PlayerId, Weather Weather, int Hours) : Command(PlayerId);

/// <summary>Test mode: the clock jumps to <paramref name="Minute"/> minutes into the day (0 to 1439), and the day goes on from there.</summary>
public sealed record SandboxTimeCommand(int PlayerId, int Minute) : Command(PlayerId);

/// <summary>Test mode: <paramref name="Owner"/>'s town hall (or first storage) gets <paramref name="Amount"/> more of every good.</summary>
public sealed record SandboxGoodsCommand(int PlayerId, int Owner, int Amount) : Command(PlayerId);

/// <summary>
/// Test mode: the building standing on column (X, Z) takes <paramref name="Damage"/> as from a blow there, knocking out
/// blocks as a siege would (not counted as being under fire), for trying out repairs.
/// </summary>
public sealed record SandboxDamageCommand(int PlayerId, int X, int Z, int Damage) : Command(PlayerId);

/// <summary>Test mode: <paramref name="Owner"/> has the tech (by id) at once, or every tech with "all".</summary>
public sealed record SandboxTechCommand(int PlayerId, int Owner, string Tech) : Command(PlayerId);

/// <summary>Test mode cheats a player can have on (see <see cref="SandboxCheatCommand"/>).</summary>
public enum SandboxCheat : byte
{
    /// <summary>The player's building sites go up at once, workers or not.</summary>
    InstantBuild,
    /// <summary>The player's buildings train each queued unit at once (room and food still count).</summary>
    InstantTrain,
    /// <summary>The tech the player researches is done at once.</summary>
    InstantResearch,
    /// <summary>The player's units and buildings take no damage and do not bleed.</summary>
    Invulnerable,
}

/// <summary>Test mode: turns a cheat on or off for <paramref name="Owner"/>.</summary>
public sealed record SandboxCheatCommand(int PlayerId, int Owner, SandboxCheat Cheat, bool On) : Command(PlayerId);

/// <summary>What test mode does to the units of a <see cref="SandboxUnitsCommand"/>.</summary>
public enum SandboxAction : byte
{
    /// <summary>Back to full health, wounds and bleeding gone, morale restored.</summary>
    Heal,
    /// <summary>Struck dead this tick, as if by falling ground: they fall and leave blood, and nobody gets the kill.</summary>
    Kill,
    /// <summary>Gone, without a trace.</summary>
    Remove,
    /// <summary>Wounded in the legs and bleeding for a while (never to death on its own).</summary>
    Bleed,
    /// <summary>The most experience a unit can have (enough to evolve).</summary>
    Veteran,
    /// <summary>Down to a fifth of its most hit points (if it had more), not bleeding: badly hurt, as after a hard fight.</summary>
    Wound,
}

/// <summary>Test mode: does the action to the units (by id), whoever owns them.</summary>
public sealed record SandboxUnitsCommand(int PlayerId, int[] Units, SandboxAction Action) : Command(PlayerId)
{
    public bool Equals(SandboxUnitsCommand? other) =>
        other is not null && PlayerId == other.PlayerId && Action == other.Action && Units.AsSpan().SequenceEqual(other.Units);
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(PlayerId);
        h.Add(Action);
        foreach (var u in Units) h.Add(u);
        return h.ToHashCode();
    }
}

/// <summary>Test mode: every unit and building (but a city-state's totem) within <paramref name="Radius"/> columns of (X, Z) is removed.</summary>
public sealed record SandboxClearCommand(int PlayerId, int X, int Z, int Radius) : Command(PlayerId);

/// <summary>
/// The units walk aboard the player's boat <see cref="Boat"/> (see <see cref="Units.BoatSystem"/>): each walks to the
/// shore beside it and steps aboard while it has a seat free. Boats and units that cannot reach it ignore it.
/// </summary>
public sealed record BoardCommand(int PlayerId, int[] Units, int Boat) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(BoardCommand? other) => base.Equals(other) && Boat == other!.Boat;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Boat);
}

/// <summary>
/// The boats among the units put whoever is aboard ashore: each sails to the nearest water by land it can be put
/// ashore on, within <see cref="Units.BoatSystem.UnloadReach"/> columns, and lands them there (see <see cref="Units.BoatSystem"/>).
/// </summary>
public sealed record UnloadCommand(int PlayerId, int[] Units) : UnitsCommand(PlayerId, Units);

/// <summary>
/// The fishing boats among the units go fishing (see <see cref="Units.UnitDef.Catch"/>): from the player's dock
/// <see cref="Dock"/>, or with 0 from their nearest dock. Other units, and boats with no dock of theirs to fish from,
/// ignore it.
/// </summary>
public sealed record FishCommand(int PlayerId, int[] Units, int Dock = 0) : UnitsCommand(PlayerId, Units)
{
    public bool Equals(FishCommand? other) => base.Equals(other) && Dock == other!.Dock;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Dock);
}
