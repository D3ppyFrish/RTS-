# Game rules

How each game system works, for players and for anyone changing the rules. Controls are in
[playing.md](playing.md); where each system lives in the code is in the map in `CLAUDE.md`.

## Economy

`src/Sim/Economy` holds the slice economy: food, wood, stone, iron ore, coal and iron ingots, every
unit of it carried by a worker or held in a building. A player's stock is what their storage buildings
(town hall and storehouses) hold, and costs are taken from the storage nearest the work.

- **Gathering** (`GatherCommand`): workers dig stone, ore and coal a block at a time, fell trees and
  pick berry bushes (5 food each). A felled tree topples over away from the worker (0.8 seconds) and
  lands as a line of tree rubble along the ground, one block for each block of its trunk (following
  steps of one up or drops of up to three down; logs with no room heap up over the stump). Its crown
  goes with it. The worker then clears the rubble, 2 wood a block, before felling the next tree, so a
  tree gives twice the wood its trunk did before (8 or 10 for most trees). Units walk over tree rubble
  at half speed, like other rubble, and any worker sent to it clears it as wood. Every kind of tree
  (broad-leaved trees, birches, pines, willows and giant mushrooms; not the dusk tree) is felled and cleared
  the same way, and a wood cutter moves on from one kind to the next. Pines stand two blocks taller, so
  they give more (12 to 16 wood); mushrooms are a block shorter. Generated maps grow every tree twice
  that size (`RegionGenerator.DefaultTreeScale`, `VoxelWorld.TreeScale`; `--trees=1` for the first
  size): each block a cube two across, a trunk two blocks square and twice as tall, on level ground,
  and forests' trees about one and a half times as far apart. Cutting any column of a big trunk fells the whole
  tree, which takes twice as long and lays a log for every two blocks of trunk: four times the wood of
  a first-size tree (32 to 40 for most), so wood comes in about as fast as with the first trees. They carry
  up to 5 goods to the nearest storage, then come back for more of the same nearby; a group spreads
  over a resource instead of crowding one block. A worker that cannot get to its block tries the next
  one nearby, and gives the job up after three in a row (stone at the bottom of a pit too deep to climb
  down into, say). Stone, iron ore and coal are only worked from a spot beside the block the worker can
  walk out of again once it is dug: never from on top of it, nor from down in a hole it could not climb
  out of. As the block is taken it checks again, since others may have dug nearby meanwhile: a block
  whose digging would leave it, or anyone standing close by, in a hole with no way out is left, and
  the worker moves on (#37).
- **Buildings** (`BuildCommand`, from `data/buildings.json`): town hall, storehouse, house, farm, barracks,
  smelter, quarry and mine. Farms and smelters need a tech first (see [Game data](../README.md#game-data)).
  A site needs fairly level ground (columns may differ by one block, which is levelled), a one-column
  gap from other buildings, and its cost; its voxel blocks go up layer by layer as workers build it.
- **Building upgrades** (`UpgradeCommand`, `"upgrade"` and `"speed"` in `data/buildings.json`, #47): a
  finished building is upgraded where it stands into a bigger one of its kind, for that building's cost and
  build time. A house (4 by 4, houses 5) becomes a large house (6 by 6, houses 12; 45 wood, 10 stone) and then a
  manor (8 by 8, houses 25; 60 wood, 60 stone, needs Masonry); a town hall (7 by 7, houses 25) a great hall
  (9 by 9, houses 40, trains at 150%, sees 12; 150 wood, 150 stone) and then a castle keep (11 by 11, stone,
  houses 60, trains at 200%, sees 15, twice as sturdy; 300 stone, 100 wood, 10 iron, needs Masonry); a
  storehouse a warehouse (7 by 7, sturdier, and cannot be captured or burned; 50 wood, 40 stone); a barracks a
  great barracks (8 by 7, trains at 175%; 80 wood, 60 stone); a smelter a foundry (5 by 5, smelts at 200%, two
  workers; 60 stone, 20 wood); and a farm a large farm (9 by 9, four farmers; 30 wood). An upgrade trains what
  the building it came from trains. It grows from its middle, half the extra columns on each side (the odd
  one on the far side), so it needs room round it: the new footprint must be on the map, keep the one-column
  gap from other buildings and from city-states, and the new ground must be within a block of the
  building's level with nothing solid over it (the same as putting it down there). It is paid for when it is
  ordered, and is builders' work; meanwhile the building works as it is, and nothing can be put on the ground
  it is to grow onto. Once the work is done, idle units on that ground are sent off it (it waits up to 10
  seconds for them), then the old building comes down and the new one stands whole on the levelled ground.
- **Plans** (`PlanCommand`, issue #92): a building marked out but not paid for or built, so it can be
  placed before the player can afford it. It needs the same ground and gap as a site (and the tech),
  keeps its footprint free of other buildings, and leaves the ground as it is. A `WorkCommand` on a plan
  starts it once the player can pay: the cost is taken, the ground levelled and it becomes a site the
  command's workers build (with none, automatic workers may take it on). Until then nothing works on it:
  automatic workers leave it alone, it gives no housing, sight or production, cannot be attacked or
  raided, and other players do not see it. `CancelPlanCommand` removes one.
- **Farms and the smelter** (`WorkCommand`): two farmers each bring 5 food every 15 seconds. The
  smelter's worker keeps it supplied with ore and coal from storage and carries its ingots back
  (2 ore + 1 coal = 1 iron in 10 seconds).
- **Quarries and mines** (`WorkCommand`, kind `quarry` in `data/buildings.json`, #50): buildings workers work, like
  a farm, for goods that never run out, wherever they stand. A quarry (5 by 5, 60 wood) takes three workers, each
  bringing 5 stone every 12 seconds of work; a mine (5 by 5, 60 wood and 40 stone, needs Smelting) takes two, each
  round of 15 seconds giving 4 iron ore and then 2 coal, in turn, which is what a smelter uses. Each load goes to
  the nearest storage, like any gathered goods. Gathering techs (Better tools) make the rounds quicker, and a
  worker's work speed counts. Automatic workers work one when the good it gives is the one the player needs most
  and it has a free place (a mine's ore and coal, like dug ore and coal, only once there is a smelter); otherwise
  they gather block by block as before.
- **Fishing huts** (kind `quarry` with `"shore": true` in `data/buildings.json`): a 4 by 4 hut of planks and thatch
  (40 wood, no tech) that must stand at the water's edge: a column right beside its footprint must have open water
  on top, at most 4 blocks below where the hut stands (water under ground, or far down a cliff, does not count).
  Elsewhere it does not fit (`PlaceResult.NoWater`; the ghost is red and says it must stand right beside water).
  Two workers fish there, each bringing 4 food every 15 seconds of work, like a quarry that gives food: it never runs
  out, gathering techs make the rounds quicker, and automatic workers fish there when food is the good needed most.
  It gives a town by a lake or the sea food before Farming, a little slower than a farm.
- **Forester's lodges** (kind `quarry` with `"plants"` in `data/buildings.json`; `EconomySystem.Forest.cs`): a 4 by 4
  lodge (30 wood, 10 stone, no tech) whose two foresters grow the woods back. Each round of 20 seconds of work plants a
  sapling, a single leaf block, on open grass or soil within 10 columns of the lodge: the free spot nearest to it,
  far enough from every building (3 columns) that the grown crown does not hang over it, clear of city-states, off
  hazards, with nobody standing there and no trunk or other sapling within 4 columns (twice that, and on the tree
  grid, where trees grow big). It takes the kind of the nearest
  tree round the lodge (a pine, a birch, otherwise broad-leaved). Two minutes later it grows into a full tree, once
  nobody stands on its trunk's columns, and wood cutters fell it like any other. A sapling dug up, built over or
  knocked off by a tree felled beside it is gone. When there is no room left the foresters wait and try again. The
  rounds give no goods, so automatic workers leave the lodge alone.
- **Population**: the town hall houses 25 (every town hall, including one built later) and each house 5; the town hall trains workers for 30 food
  and the barracks trains soldiers (`TrainCommand`) while there is room. A building that trains units can have a
  rally point (`RallyCommand`, snapped to ground a unit can stand on): each unit it trains walks there
  when it steps out, stopping within 1.5 blocks. Setting it on the building itself clears it, and a
  captured building loses it. Every minute each unit eats its
  upkeep (1 food, more for some forms: see [Evolution](#evolution)); a player who cannot pay is hungry and cannot train.
- **Roads** (`RoadCommand`, after the Roads tech): a line of road up to 64 blocks, 1 stone per block,
  laid at once on open ground. Units on a road move twice as fast, and paths prefer roads when they save time.
- **Raids** (`CaptureCommand`, `BurnCommand`): units at an enemy storehouse capture it with its goods
  in 10 seconds or burn it and its goods in 5, unless one of the owner's units is within 8 blocks (and
  a soldier among them will fight the raiders).

## Moving

`src/Sim/Units/UnitMovement.cs` moves units along flow fields (`FlowField.cs`) over the walk graph
(`WalkGraph.cs`): a unit steps up one block and drops up to three, and wades water one block deep.

- **No way there** (#37): a unit sent somewhere it cannot walk to (a cliff top, across deep water, up a
  tree's crown, onto a roof) walks to the nearest place it can get to instead (nearest across, then in
  height), and stops there.
- **Climbing down**: a unit on top of a wall, gate or tower, or on the roof of any other building, sent
  somewhere it cannot walk to from up there, climbs down on the side nearest to where it is going.
- **Digging out** (#37): a worker or sapper sent somewhere from a pit, hole or cave it cannot walk out of
  (64 places to stand or fewer) digs a stair out instead (as a sapper tunnels, at its own digging pace),
  to the nearest ground outside, and stops once it can walk on. An automatic worker then takes up work
  again. Other units stay in the pit.
- **Formations** (a plain right-click on a group, `FormationMoveCommand`): every unit keeps its place
  relative to the formation's middle across open ground, at the pace of the slowest. A unit only keeps its
  place while that also takes it nearer along its own way (#37); where the straight way is blocked, the
  ground narrows (a gap, a corridor, a bridge) or the path turns sharply, it follows its own path at its own
  full speed instead and takes its place again once it can. Near the end it walks straight to its place if
  nothing is in the way, else to the middle first, and stops there if its place still cannot be reached.
  A formation gets anywhere about as soon as the same units sent with a plain move.
- **Hazards** (#66, #78): a step onto ground that hurts or traps (poison bog, quicksand, beside lava, a sinkhole's crust)
  costs eight times plain ground, so a unit on a move order walks up to seven blocks out of its way for
  each such block it keeps off, and crosses when there is no other way.
- **Crowds**: units push each other apart, but a unit with no order is never pushed off a drop of more
  than a level, so a crowd does not shove an idle worker into the hole beside it.

## Combat

`src/Sim/Units/CombatSystem*.cs` runs the fighting each tick, after the economy.

- **Orders**: an attack order (`AttackCommand`, `AttackBuildingCommand`) sends units after their
  target wherever it goes; an attack-move (`AttackMoveCommand`) walks somewhere and fights what it meets
  on the way, then carries on; a plain move walks past enemies. A patrol (`PatrolCommand`) is an
  attack-move that never ends: each unit walks to the goal fighting what it meets, then back to where it
  stood when it was given the order, and so on, until it is given another order. A unit that stops to
  fight carries on to the same end afterwards. A walk that ends more than 2 columns past the arrival
  radius from its end counts as a miss, and two misses in a row end the patrol (there is no way between
  the ends); a goal less than 3 columns away is only an attack-move. A guard order (`GuardCommand`) puts
  units in charge of one of their player's units or buildings: each keeps within 3 columns of it, walking
  after it when it moves (it looks every 8 ticks), and fights enemies within 8 columns of it, first whoever
  hits it; it lets an enemy go once that enemy is more than 12 columns from the charge, and walks back. A
  guard ignores enemies further from its charge even when it can see them. The order ends when the charge
  dies, burns, is captured or the unit is given another order. Soldiers standing still fight the
  nearest enemy they see and answer anyone who hits them; workers fight only when ordered to. A soldier
  on a raid that is attacked drops the raid and fights back.
- **Damage**: each unit's damage, attack time, range, sight and splash are in `data/units.json`. The role
  table in `data/roles.json` (from the design doc) doubles an attack on a role it is strong against: line
  infantry beats cavalry, ranged beats line infantry and workers, cavalry beats ranged units and casters,
  and every combat role beats sappers and workers. All of a tick's hits land together, so neither side
  strikes first, and units do not waste blows on an enemy that is already dying.
- **Terrain**: ranged units (range over 2) need a clear line to their target (leaves do not block it), and
  two or more levels above it they reach and see 25% further. Height also changes where hits land (see
  armour below): arrows from the high ground and blows from above find more heads, blows struck uphill
  more legs.
- **Armour and hit locations** (issue #71, `src/Sim/Units/Body.cs`): every hit lands on a part of the
  body, picked with a deterministic roll (match seed, tick, attacker and target). A humanoid is hit on the
  head 10% of the time, the torso 50%, the arms 20% and the legs 20%; a beast (`"body": "beast"`, such as
  the wolf) on the head 15%, the body 60% and the legs 25%. A ranged shot from two or more levels above, or
  a melee blow from a level above, moves 10 points from the legs to the head; a melee blow uphill moves 20
  points to the legs (the head's first, then the torso's). The part multiplies the damage: head 150%,
  torso 100%, legs 85% (a beast's 70%), arms 70%. Then the target's armour on that part (`"armor"` in
  `data/units.json`, per part) takes points off, by the attack's kind (`"attack"`): slash (swords, axes,
  claws) is stopped by one and a half times the armour, pierce (spears, arrows, lances) by the armour,
  crush (hammers, clubs, stones, blasts) by half of it. A hit always does at least a quarter of what it
  would do with no armour, and at least 1. Armour has no upkeep. A unit can carry armour of its own on
  top of its type's (`UnitStore.ExtraArmour`, for pieces found later); it keeps it when it evolves.
- **Shields** (`"shield"`, a percent chance): a shield stops a whole hit from in front of its bearer
  (within a quarter turn of the way it faces: towards its target, the way its formation faces, or where it
  walks; a unit standing idle turns to whoever hits it). Hits from the side or behind and blasts' splash
  are never stopped, so flanking a shield wall pays. The knight carries one (30%).
- **Wounds**: each hit's damage after armour is kept as a wound on its part (`UnitStore.Wounds`, at most
  255 a part), and slash and pierce wounds bleed (`UnitStore.Bleeding`, one bit a part; crush does not).
  Healing, from healers or over time, mends the worst wound first with the hit points it gives back, and
  a part mended whole stops bleeding; a unit back at full health has no wounds. Experience for damage
  counts the damage after armour.
  The unit panel draws the selected unit's body, shaded by its wounds, with each part's armour and wound.
- **Bleeding** (issue #72): a slash or pierce wound bleeds away 40% of the hit's damage again
  (`UnitStore.BleedLeft`, per part), a hit point a second per bleeding wound and a point more for every
  20 it has left, then clots. Bleeding lands with the tick's blows but does not count as being in the
  fight, so a bleeding unit still starts to heal on its own after 10 quiet seconds. It can kill (decided
  by the designer, 14 Nov: "Yes, wounds clot with rest"); the unit whose blow last cut it (`UnitStore.Cutter`)
  gets the kill and the experience for the blood. Three ways to stop it sooner: a healer (the battle
  priest) stems all of a unit's bleeding at once when it heals it, and goes to a bleeding ally before any
  that is only hurt; a unit healing at home (out of the fight 10 seconds, near its own town hall or house)
  has its wounds bound at once; and away from home it just has to clot. A **grievous wound**
  (`CombatSystem.OpenGrievousWound`, for amputations in #73; the designer, 14 Nov: "grievous wounds like
  amputations will kill a unit likely before it clots as long as they don't receive treatment") bleeds 4
  hit points a second and never clots: only a healer or getting home stops it. Units whose type has
  `"bleeds": false` never bleed. A beaten monster's wounds are bound so it lives to be tamed. The numbers
  are `"bleeding"` in `data/roles.json` (`"kills": false` makes bleeding stop at 1 hit point).
- **Blood on the ground** (`World/BloodSystem.cs`): a bleeding unit drips on the top block of the column it
  stands on every quarter second and leaves more where it loses hit points, a blow that draws blood
  splashes it, and a death leaves a pool, so a wounded unit leaves a trail and a battle a stained field.
  Each column holds one stain (up to 255 blood) with an age; it is gone after a day of game time (20
  minutes, `"bloodDays"`), three times sooner in rain and six times in a storm, and fresh blood on it makes
  it younger. Digging out or changing the stained block, or water (or anything) over it, takes it away, and
  blood never falls on water. At most 8,192 stains: when full, the faintest eighth (amount by time left)
  goes. Blood is game state, in the state hash. The game draws it as splats on the block tops, darker and
  wider for more blood, drying brown and fading as it ages, and a bleeding unit with a wet patch and
  falling drops; there is no setting to turn it off (the designer, 14 Nov).
- **Cleaning blood** (`CleanCommand`, `EconomySystem.Clean.cs`; asked for by the designer, 14 Nov): workers sent
  to blood (right click on it) scrub the nearest stain within 8 columns of that spot that no other cleaner
  has, a second plus a tick per blood in it (at their work speed), and move on until none is left there. A
  clean town gives wolves no trail to follow.
- **Wolves follow blood**: a wild monster with `"tracksBlood"` (the wolf) that has nothing to do picks up
  the freshest trail (blood fallen in the last minute) within its sight and goes after the unit that left
  it, up to 48 blocks from its den, well past its usual leash; it gives up and goes home when the quarry
  dies or gets past that. Trails of wild monsters are never followed. (Eating the kill and leaving bones is
  #74 and #75.)
- **Casters**: the mage's blast hits every enemy within 1.5 blocks of its target, so it breaks packed groups.
- **Morale**: a death costs each ally near it a share of 150 morale (a bigger share the fewer they are)
  and gives each enemy near it a share of 60. At 25 a unit breaks: it runs to its town hall, or away from
  the enemy, for 8 seconds, ignoring orders, then rallies. Morale comes back a point a second; a hungry
  army has at most 50. Units near a hero hold longer (see [Evolution](#evolution), "Heroes").
- **Healing**: a unit that has not hit anything or been hit for 10 seconds gets back a quarter of a
  percent of its hit points a second (from half health, full in 3 minutes 20 seconds), or 2% a second
  within 12 columns of a town hall or house of its own, or near a goblin town it holds (full in 25 seconds), so a wounded army is
  worth bringing home. Healers heal on top of this. The numbers are in `data/roles.json`.
- **Healers** (issue #42): every faction trains the healer at the barracks once it knows Basic magic
  (40 food and 20 wood, 15 seconds); matches start without any. Every 2 seconds it gives 5 hit points back to the most hurt ally
  within 6 blocks (a bleeding one first, and it stems the bleeding), never to itself or an enemy. It has
  45 hit points and hits for only 3, so keep it behind the line. The Settlers' battle priest, a spearman's
  form, heals the same way while it fights in the line.
- **Buildings** are made of real blocks. Damage knocks out the top block of the column nearest the
  attacker (a sapper knocks out the bottom one, so the whole column falls), and a building that has lost
  half of itself (by block toughness) collapses. Sappers deal full damage to buildings, everyone else a
  quarter.
- **Hollow buildings** (houses, halls, barracks and every other roofed building; walls, gates and towers
  stay solid): walls round the edge, a floor (`BlockType.Floor`, which nobody stands on, so no unit ends
  up shut inside) and a stepped roof over a room that goes up under it. Their blocks are tougher by what
  is missing inside, so a hollow building takes as much to bring down as a solid one did. A building that
  collapses **caves in**: its chunks are pulled toward its middle, and its rubble only slides inward off
  the heap, so it all comes down on its own ground. Falling pieces crush whoever they land straight on;
  rubble sliding off a heap only buries those beside it, and they climb out, so units hacking at a
  building are not killed by it coming down.
- **Tunnels** (`TunnelCommand`): sappers dig toward a block column by column, a level up or down each
  column, twice as fast as a worker digs, and walk in. They stop at bedrock, water, buildings and fields.
  Digging just under a building takes away what holds it up, and that part of it falls in.
- **Dig areas** (`DigAreaCommand`, `DigArea`): workers and sappers dig out an area of up to 64 x 64
  columns down to a level (at most 16 below its highest ground), a layer at a time from the top, so
  nobody digs out the block under a neighbour first. A stair one column wide is left along the longer
  side, from the X1 (or Z1) end, one level lower per column, so the diggers can always climb out (a
  one-wide trench is its own stair). Workers carry stone, ore, coal, wood and berries to storage as when
  gathering; plain ground is just dug away. Sappers dig twice as fast and leave everything. Blocks under
  buildings and roads are left. Units following a path no longer drop more than a level into a column
  that is not on their way, so they don't slip into a pit when cutting a corner along its rim.
- **Walls and gates** (`WallCommand`, `EconomySystem.Walls.cs`): a wall is laid along a straight line, one
  building per column, paid for column by column until the goods run out. Workers build it a column at a
  time and move on to the next unfinished column nearby. A block of
  a site waits while a unit stands where it goes: idle units are sent off the site, and so is a builder
  at work on the column next to it, who works on from beside its own (two builders each standing on the
  other's column would otherwise wait on each other for good). Walls and
  gates join up without the gap other buildings keep, and a line that steps diagonally still shuts the way
  (nobody cuts a corner between two blocked columns). A gate is a wall column whose two bottom blocks are
  gate leaves (`BlockType.Gate`): its owner's units walk through, everyone else finds it shut. Flow fields
  are per player for this, and movement checks it too. Walls are ordinary building blocks, so blows knock
  their tops off, sappers knock out the bottom block and the rest of the column comes down as a falling
  piece that lands as rubble, and tunnels dug under a stretch bring all of it down. The rubble of a wall
  spreads: a block of it slides off any step down (other rubble only off a drop of two or more), on past
  the wall the way it was knocked first, so a column that is broken leaves a low heap that is walked over
  from both sides, whatever the wall was. Units sent at a building they cannot get to break through (below).
- **Wall tiers** (`"upgrade"`, `"sturdy"` and `"walkway"` in `data/buildings.json`): what is laid is a
  **wooden barrier**, a row of stakes 2 high for 1 wood a column, up in a second and easy to break. It is
  upgraded where it stands to a **wooden wall** (4 high, 3 wood more, with a walk along its top) and that
  to a **stone wall** (6 high, 10 stone more, needs masonry), which is three columns thick (below). Gates
  go the same way: a barrier gate (3 high, 3 wood), a wooden gate (4 high, 5 wood more) and a stone gate
  (6 high, 4 wood and 12 stone more, masonry), a gatehouse three columns deep; the two upper tiers of gate
  have the walk too. A column of barrier comes down after 20 damage from its top, of wooden wall after 80
  and of stone wall after 225 (everyone but sappers deals a quarter damage to buildings). A sapper knocks
  out the bottom block and the column falls: a lone sapper is through a barrier in about 6 seconds, a
  wooden wall in 12, and one column of a stone wall in 23, of which there are three to get through. A
  higher tier is never laid as it is.
- **Facings** (`"facing"` in the data, `Sides`, `FacingCells`, `MissingFacings`): when a wall's upgrade to
  stone is done, a facing goes up on each free side of it across the way the line runs, as a site for its
  builders that is already paid for with the upgrade. Where the line turns, the facing runs on round the
  outside of the turn, and a wall that stands alone has none. The facing is as high and as sturdy as the
  wall and has the walk, so the deck of a stone wall is three columns wide. A wall never puts a facing in
  the way through a gate: the two columns straight out from a gate on each side are kept for the gate (the
  first is the gate's own facing, a column of gatehouse with leaves in it; the second keeps the way out
  open where the wall turns beside the gate). A facing is a building of its own, with its own health: it
  is knocked down by itself, and an attacker has to get through it, then the wall, then the facing behind.
  A builder who has finished a column goes on to a facing up to 32 columns off (to another column of wall
  8), so the workers who upgraded a wall face all of it without being sent. Facing that has been lost is
  put back with the Upgrade order on the wall behind it, at the facing's own price (3 stone a column of
  wall, 1 wood and 3 stone a column of gatehouse). A facing is never laid by itself.
- **Upgrading** (`UpgradeCommand`): the upgrade is paid for when it is ordered, and is builders' work like
  a site (any worker with nothing to do comes). The wall stands as it was, and holds, until the work is
  done; then it is the new wall, whole, whatever had been knocked out of it. One order takes the whole
  stretch: every wall and gate of the same tier joined to the one named, nearest first, as far as the
  goods go (at most 256); `Stretch = false` takes only the one. A unit standing on a wall that grows is
  lifted with it.
- **Towers** (`"kind": "tower"`, `TakesPlaceOf`, `Absorb`; `"aim"` in the data): a **watchtower** (3 by 3
  columns, 7 high, a timber frame; 30 wood, 25 seconds of work) and a **stone tower** (5 by 5, 9 high;
  60 stone and 20 wood, 60 seconds, needs masonry). A tower is solid to its deck, so nothing walks or
  shoots through it. It is placed like any building, on open ground, where it keeps no distance from walls
  (a wall runs right up to it); or onto the player's own finished walls and facings, any that lie wholly
  under it. Those stand as they are, and hold, while the tower goes up round and over them; when it is done
  they are part of it. A stone tower put over a watchtower takes its place the same way. A tower is not put
  over a gate, a wall that is still a site, or anyone else's wall, and it has no upgrade. A tower sees far,
  14 columns from a watchtower and 18 from a stone tower, against 2 for a wall, so with fog of war on it
  shows who is coming long before they reach it. Ranged units are posted on it as on a wall (below), and
  those on a tower **aim better**: of the shots that would miss because the target is in the dark or the
  rain, half land from a watchtower and three in four from a stone tower, and 10 and 15 more shots in a
  hundred find the head (taken from the legs first, then the arms). Units up there hold their ground and
  see no further than they otherwise would; it is the tower that sees.
- **Posts** (`PostCommand`): ranged units that fight (archers, mages and the like) are posted on top of a
  finished wall, gate or tower that has a walk, one to a column, along the **edge of the deck**, where the
  battlements are: the columns with a side that has nothing as high beside it. So a stone wall holds two
  rows, one on each face, and a tower a ring (8 on a watchtower, 16 on a stone tower). Each unit takes the
  free column nearest the one named, within 12 columns of it, walks to the foot of the wall nearest it
  (the ground within 2 columns of its post) and climbs (half a second a level). Up there it shoots over the
  wall both ways with the reach, sight and aim of high ground (above), and leans out over the edge: its
  line of fire starts at the edge of its own column toward the target, so it shoots what stands right at
  the foot of the wall. It holds its ground whatever its stance: it does not jump down after anyone. An
  enemy that cannot get at it goes for the wall under it, and a unit whose wall comes down comes down with
  it. An order to go somewhere it cannot walk to from up there has it climb down on the side it is going.
  Nobody is posted on a barrier, in the middle of a deck, or on a column whose top block is gone. Units
  of the order that are up already stay where they are, on the building named, or anywhere along the wall
  when a wall is named; named a tower, those on the wall beside it go up it, and named a wall, those on a
  tower come down to it. The game
  draws a deck half a block below the top of its blocks, between the battlements, and the units on it
  that much lower.
- **Breaking through** (`Breach`, `BreachFor`): units sent at a building they cannot get to (it is walled
  in) go for a wall, gate or facing of its owner within 16 columns instead: one they can get at from where
  they are, with the shortest way from them through it to the building (ties to the one nearer the
  building, then straighter ahead), so a group breaks through a thick wall in one place, column behind
  column, and not along its face. They remember what they were sent at and go on to it when the way is
  open. A wall as high as a stone wall is struck from the ground and from its own rubble alike. The rubble
  of a wall spreads as it settles and no longer crushes those standing beside it; a piece that falls
  straight onto someone still does.
- **Bridges** (`BridgeCommand`, `EconomySystem.Bridges.cs`): drawn from bank to bank like a wall, a bridge
  is a level plank deck at the standing height of the higher end (the two ends must be ground at most a
  climb apart). Every column of the line that nobody could walk across at about that height (deep water,
  a drop of two or more, a dug trench) gets a deck block, up to 32 of them, 3 wood each, paid for column by
  column until the wood runs out; ground on the way is left as it is. Each deck column is its own one-block
  building that workers build from ground or finished deck beside it, so a bridge goes out from the bank a
  column at a time, and each worker starts at the end on its side. Anyone can walk over a bridge, the enemy
  too, so a crossing you open is a way in as well as out. A few blows (a plank's toughness) knock a deck column
  out and it falls (into the water, which flows back): a player can also knock out their own bridge
  (`AttackBuildingCommand` is allowed on your own bridges) to cut the way across behind them. Bridges are
  never placed from the build menu (`CanPlace` refuses them).

`BridgeTests` checks bridges: workers bridge a river too deep to wade, a column at a time from the near
bank, and a spearman who could not get across walks over it; the enemy crosses it too until a spearman
knocks out its end column, which cuts the way; the deck runs level at the higher end and skips ground; and
a bridge stops where the wood runs out.

`CombatTests` has the issue's checks: each counter wins its matchup at equal cost from either side of the
field, archers on a hill beat the same archers below whichever side the hill is on (on the flat, where
arrows land is down to chance, so the check is over six matches), mages break a packed block of spearmen
that beats them spread out, and sappers bring a house down more than twice as fast as spearmen of the same
cost. `ArmourTests` checks armour: a helmet and mail take more than a quarter off a sword's average hit and
the head hurts most, slash, pierce and crush differ against the same armour and only slash and pierce
bleed, archers on a hill land over one and a half times the share of head hits, uphill blows find the legs, a shield stops
hits from the front and none from behind, and healing mends the worst wound first. `BleedingTests` checks
bleeding: a wound loses hit points each second until it clots, slash and pierce bleed 40% of their damage and
crush none, bleeding kills and credits the cutter (or stops at 1 with `"kills": false`), a grievous wound
kills untreated, a battle priest stems bleeding at once and sees to the bleeding first, home binds wounds,
a bleeding unit leaves a trail that fades in a day and three times faster in rain, digging or flooding a
stained block clears it, blood stays under the cap in a 1,000-unit battle, and a wolf follows a fresh trail
past its leash to the bleeding unit while ignoring old trails and wild ones. All the numbers are first
guesses for playtesting.

## Automation

`src/Sim/Automation/AutomationSystem.cs` runs each tick, right after the tick's commands. It gives the
same commands a player could, through the same code, so a game replayed with automation turned off and
its commands played back ends the same. An order of the player's always comes first.

- **Automatic workers** (`AutomateCommand`, J; `AutomateAllCommand`, Shift+J): once a second an
  automatic worker with nothing to do takes the nearest job it is needed for, in this order: take what
  it carries to storage; build a site that can use another builder (one, plus one for every 30 seconds
  of building); work a farm with a free place; keep a smelter going while there is something to smelt;
  otherwise gather within 40 columns what is needed most, or, once nothing is left to gather that close,
  the nearest of anything within 128 columns. Food and wood are wanted 3 parts each and
  stone 2 (iron ore and coal 1 each once you have a smelter), spread over the workers already on each;
  food counts four times as much while you are hungry or have less than two minutes' food, and a good
  you have 500 of hardly at all. A worker the player orders to move, stop or fight waits for orders
  until it is given work again or made automatic again. A job given up within 15 seconds without
  anything done is not given out again for a minute, and the worker who gave it up keeps away from the
  ground within 8 columns of it for that minute too, since the rest of a pit or ledge it couldn't get to
  is most likely out of its reach as well. A worker that gives up again and again is most likely stuck,
  in a pit say: from its second failure in a row it waits 10 seconds before looking again, then 20, 40
  and at most 60, instead of switching between idle and working.
- **Work priorities** (`PriorityCommand`, `DefaultPriorityCommand`, Ctrl+J): each worker has a priority
  list, the kinds of work it takes on its own, top first: build, farm, smelt, supply storehouses (below), gather what's needed most
  (the rule above), or gather one good (food, wood, stone, iron ore or coal; iron ore and coal then even
  without a smelter). An idle automatic worker still takes what it carries to storage first, then goes
  down its list and takes the first kind of work there is some of (the nearest site, farm or smelter, or
  within 40 columns for gathering, or 128 once nothing is closer); work left off the list it never takes on its own. A worker without a
  list of its own follows its player's default list, and so do the workers they train; the default starts
  as build, farm, smelt, supply, gather what's needed (supply only finds work once a storehouse has supply rules). A new list
  counts from the worker's next job. Lists are part of the game state.
- **Repeat** (`RepeatCommand`, P): a building on repeat queues each unit it finishes again as soon as
  there are goods for it and a minute's food left for everyone.
- **Houses and farms**: a player whose workers are all automatic (Shift+J) also gets buildings: a house
  when the units waiting to be trained would fill the housing, and a farm when food runs short (less
  than 100 plus two minutes' worth), no farm has a free place and there are fewer farm places than a
  third of the workers. They go on the nearest free spot within 24 columns of the town hall, keeping 3
  columns in front of every door clear, and the automatic workers build them.
- **Rebuild orders** (`RebuildCommand`, Shift+P, `AutomationSystem.Rebuild.cs`): with them on, each finished
  building of yours that is destroyed (knocked down, undermined, collapsed or burned; not one that is
  captured) is remembered as a ruin and drawn as a pale ghost where it stood. Every 2 seconds automation
  looks at it: while an enemy is within 12 columns of its middle nothing happens; while rubble lies on its
  footprint, idle automatic workers with build on their priority list clear it (at most 2 on a ruin, only
  when no site wants a builder, highest rubble first); then, once you can pay, it places the building again
  as a site, through the same command you would give, and the automatic workers build it like any site. A
  wall, gate or tower comes back at its first tier; bridges and a stone wall's facings are not remembered.
  A ruin is forgotten when someone builds on its spot, when it is no longer yours to build, after 10
  minutes without being rebuilt (a crater where it stood, say), or when you turn rebuild orders off. Without
  automatic workers the rubble stays until you clear it yourself.
- **Supply rules** (`SupplyCommand`, the Keep stocked buttons of a selected storehouse or town hall,
  `EconomySystem.Supply.cs`): a storage building of yours can be told how much of each good to keep (25,
  50, 100 or 200 from the buttons; up to 500). Workers on supply duty there fetch the good it is shortest
  of (counting what the others on duty are already bringing) from your nearest other storage that can spare
  it, a load at a time, and never bring more than is missing. A storage building spares what it holds beyond
  its own rule for that good, so two storehouses with rules never strip each other. Right clicking a storage
  building that has rules with workers puts them on supply duty: they stay there, waiting when there is
  nothing to fetch, until given other work or until its rules are cleared. Automatic workers with supply on
  their priority list (it is on the standard list, after smelt) take the duty on their own, at most 2 at a
  time on one building, and go back to other work once there is nothing to fetch. Costs are paid from the
  storage nearest the work, so a forward storehouse kept stocked with stone pays for the walls going up
  beside it; it is also there for raiders to capture or burn, and a captured one loses its rules.
- **Town governor** (`GovernCommand`, the Governor buttons of a selected town hall or Shift+U,
  `AutomationSystem.Governor.cs`): a town hall of yours can be given a governor with a focus, Growth, Army
  or Defence, or none (Off). Every 2 seconds the governor looks over its town, the ground within 24 columns
  of the hall, and gives the orders you would. It makes your idle workers there automatic (not ones you held
  with an order of your own). If no site of yours in the town is still going up, it places the first
  building wanted: a house when fewer than 4 places are free in your housing, a farm on the same rule as
  above, then what its focus wants. Growth wants a storehouse at least 12 columns out once you have 10
  workers, then a smelter once you have 20 iron ore; Army wants a barracks; Defence wants a barracks, then a
  watchtower at each of the four corners 9 columns out from the hall. The hall trains workers up to 30
  (Growth), 15 (Army) or 20 (Defence). Under Army or Defence each barracks in the town with nothing queued
  trains a soldier: Army the fighting type (line, heavy, ranged or cavalry) you have fewest of, Defence the
  ranged type you have fewest of, or spearmen until it has archers. Soldiers wait while a wanted building
  waits for goods. With nothing being researched, the governor of your first town hall researches the next
  tech its focus needs, read from the data: whatever farms need, then what smelters need (Growth), what the
  barracks' fighting units need (Army) or what its ranged units need (Defence); a tech that needs another
  starts with that one. Nothing is trained or researched without room in the houses and a minute's food left
  for everyone. With a governor in your town hall, the houses and farms for all-automatic workers above come
  from the governor instead.
  **Town guard**: soldiers you hand to the governor (Shift+V or the Defend the town button with soldiers
  selected, which picks your nearest governed hall, or Alt+right click on the hall: a guard order on it)
  defend the whole town instead of only the hall. They fight enemies within 24 columns of the hall and let
  one go only past 30; with nothing to fight, the governor sends each after the enemy in the town nearest
  to it that you can see (any of your units or buildings seeing it will do, so a raider out of the guards'
  own sight is still caught), except beaten monsters and a neutral city-state's guards. With the town clear
  they walk back to the hall. If the governor steps off they stay as plain guards of the hall (8 columns);
  any other order takes a soldier out of the town guard. The hall's panel shows how many it has.
- **Stances** (`StanceCommand`, V): soldiers are aggressive (they chase what they see), hold their
  ground (they fight only what is in reach and never move to chase), or retreat when hurt (aggressive
  until down to a third of their hit points, then they hold their ground, and when hit away from home
  they walk back to the nearest town hall).

`AutomationTests` has the issue's check: a town hall and five workers, given orders only at the start
(all workers automatic, train workers on repeat, research farming), grow for 10 minutes without going
hungry, from 5 to 40 people with 7 houses and 7 farms, and the same game replayed from its recorded
commands with automation off ends in the same state. All the numbers are first guesses for playtesting.

## Evolution

`src/Sim/Units/EvolutionSystem.cs` is the Settlers' signature: their units earn experience and evolve.
Orc units earn none.

- **Experience** (`UnitStore.Xp`): a Settlers unit earns 1 for each hit point of damage it deals to enemy
  units and buildings, 10 for each enemy unit it kills, 1 for each second of building and 5 for each
  minute it stays alive. The first level needs 150. The rates and levels are in `data/factions.json`.
- **Forms**: at each level a unit can take its next form: one of the unit types in `data/units.json`
  whose `evolvesFrom` names its type. Forms are not trained. Each gives something and takes something:
  more upkeep, less speed, or a weakness (`weakTo`: that many percent more damage from one role's attacks).

  | From | Forms |
  | --- | --- |
  | Worker | foreman (works half as fast again, eats 2), militia (line infantry, slower than a spearman; it stops working) |
  | Spearman | knight (heavy infantry, 130 hit points and 12 damage, slow), pathfinder (speed 6, sight 14, +50% from casters), battle priest (heals the most hurt ally within 5 blocks by 4 every 2 seconds, eats 2), siege engineer (siege: fire pots from 7 blocks that hit buildings at full damage, slow); at a cleared wolf den only, the Wolfheart (see [Cleared dens](#cleared-dens)) |
  | Archer | longbowman (range 12, slower), ranger (speed 5.5, sight 13, +50% from line infantry) |
  | Rider | lancer (18 damage, eats 3), outrider (speed 10, sight 14, +50% from ranged) |
  | Sapper | tunneller (digs tunnels twice as fast, eats 2), demolisher (9 damage, slower) |
  | Mage | battle mage (14 damage over a 2-block blast, eats 2), seer (range 11, sight 15, slower) |
  | Knight (level 2) | the **Marshal**, a hero (below) |
  | Longbowman (level 2) | the **Bowmaster**, a hero (below) |
  | Lancer (level 2) | the **Horselord**, a hero (below) |
  | Demolisher (level 2) | the **Breachmaster**, a hero (below) |

- **Heroes** (`"hero": true` in `data/units.json`, `HeroTests`): the last forms are heroes, one of a kind. The
  first is the **Marshal**, the knight's form at level 2 (500 experience): 200 hit points, 14 damage, a knight's
  armour and shield, and it eats 4 food a minute. A player has at most one living Marshal: while it lives no
  knight of theirs can take the form (the button says "you have one"), and once it falls the next knight with
  the experience can. A hero never breaks and runs. The Marshal **steadies** its own side's units within 8 blocks
  (and 4 levels), itself included (`"rally": 8`): a death near them costs them half the morale it would
  (`"steady": 50`), and they get 3 morale back a second instead of 1 (`"inspire": 3`). Enemies near it gain
  nothing. In the game the Marshal wears a crown on its helm, gilded pauldrons and a tall banner in its
  owner's colour on its back.
  The second is the **Bowmaster**, the longbowman's form at level 2 (500 experience): 80 hit points, 9 damage,
  range 13 and sight 14, and it eats 3 food a minute. It is one of a kind like the Marshal (each hero on its
  own: having a Marshal does not stop a Bowmaster) and never breaks. It **calls the volleys**: its own side's
  ranged units within 10 blocks (and 4 levels), itself included, deal a quarter more damage
  (`"rally": 10`, `"volley": 25`), to units and buildings alike; foot soldiers, other players' archers and
  archers out of its reach gain nothing, and the bonus ends when it falls. In the game it wears a gold
  circlet over its hood, carries a gilded great bow and a short pennant in its owner's colour on its back.
  The third is the **Horselord**, the lancer's form at level 2 (500 experience): 180 hit points, 22 damage,
  sight 11, heavy barding, and it eats 4 food a minute. It is one of a kind on its own like the others and never
  breaks. It **leads the charge**: its own side's cavalry within 12 blocks (and 4 levels), itself included,
  move a quarter faster (`"rally": 12`, `"spur": 25`), on roads too. A formation keeps its slowest member's pace,
  spurred for the riders near the Horselord, so riders riding with it stay together and arrive sooner. Foot
  soldiers, other players' riders and riders out of its reach gain nothing, and the spur ends when it falls.
  In the game it rides a barded horse with gilded trim, wears a gold helm with a crest in its owner's colour
  and carries a gilded lance with a pennon.
  The fourth is the **Breachmaster**, the demolisher's form at level 2 (500 experience): 120 hit points, 12 damage,
  sight 8, a heavier helm and leather, and it eats 3 food a minute. It is one of a kind on its own like the others
  and never breaks. It **works the breach**: its own side's sappers within 10 blocks (and 4 levels), itself
  included, work half as fast again (`"rally": 10`, `"undermine": 50`): their blows on buildings and walls land
  half as hard again, and they dig tunnels and dig areas in two thirds of the time. Soldiers, other players'
  sappers and sappers out of its reach gain nothing, and it ends when it falls. In the game it wears a
  gold-banded helm with a lamp at the brow, carries a great powder keg bound in gold with a pennant in its owner's
  colour on its back, and a gilded war pick.
  The design doc's other heroes (the mage and worker lines', with abilities of their own) come later.
- **Evolving** (`EvolveCommand`) is immediate and free. The unit keeps its id, place, orders, experience
  and the same share of its hit points; a worker that becomes militia drops its job and load and is no
  longer automatic. Experience is never spent, so a form of a form needs only the next level.
- **Auto-evolve** (`AutoEvolveCommand`): the player can choose the form each unit type takes on its
  own. Once a second, automation evolves the units of that type that can take it.
- **In the game**: your units that can evolve have a gold ring under them. Select them and the panel
  shows a row for each type with a unit ready (or a form chosen for it), with a button per form (hover
  it for what the form gains and gives up) and an Auto button that steps through the forms that type
  takes on its own. A single unit's panel shows its level and experience. Forms wear a gold belt.
  `--xp=N` starts your units with N experience, to try the forms at once.

`--xp=500` starts your units at the second level, to try the Marshal, the Bowmaster, the Horselord and the Breachmaster at once. `EvolutionTests` has the issue's check: a spearman kills three orc workers, reaches level 1 and becomes
a knight, which hits an orc spearman three times as hard as before (heavy infantry beats line infantry)
but walks a quarter slower. All the numbers are first guesses for playtesting.

## Monsters and taming

`src/Sim/Units/MonsterSystem.cs` is the Orcs' signature: wolf lairs guard the land between the start
areas, and Orcs who beat a wolf keep it.

- **Lairs**: at match start a lair lies on the way from each start area to the map centre, 40 blocks
  out (at most two thirds of the way and at least 20, on dry ground away from buildings), with a pack
  of four wolves around its den. Monsters belong to the wild side (player 7), everyone's enemy, which
  earns no experience and pays no upkeep.
- **Across the map** (issue #45): more lairs are spread over the map by biome, for each 512 by 512 blocks
  of it: a wolf pack in the grassland, two herds of **wild boar** in the forests and a **stone giant** in the
  hills, two pairs of **bog horrors** in the swamp and two pairs of **giant crabs** on the beaches (on the
  default 768 map, twice that, rounded down: 2, 4, 2, 4 and 4). Each den is at least 100 blocks from
  every start and 40 from any other lair or town. A boar herd is three boars (80 hit points, quick, tusks
  that pierce) that pay 200 food; Orcs who beat a boar can ride it, and the spearman becomes a rider (the
  Orcs' own rider sits on a war boar). A stone giant stands alone: 450 hit points, slow, heavy armour and
  crushing blows, and pays 200 stone and 60 iron; Orcs can recruit it. A bog horror (140 hit points,
  slashing claws) heals 2% of its hit points a second even in a fight and follows a fresh blood trail
  well past its leash; a pair pays 120 food. A giant crab (160 hit points, slow, crushing claws) is
  shelled hard all over (head 3, body 5, legs 2); a pair pays 150 food. Each leaves a den with its own
  prizes (see "Cleared dens"). In the data, `biome` and `count` in a `lair` spread it, and `atStarts` keeps
  it at the starts too.
- **Roaming** (issue #65): the wolf packs and boar herds spread across the map roam (`roam` in a `lair`: 32
  blocks for wolves, 24 for boars). Every 20 seconds, unless it is fighting, the pack wanders on together to
  another spot within that distance of its den (on dry ground, away from buildings), and it guards round where it
  is rather than round the den: it goes after any unit, workers too, within its guard radius of that spot, and
  its leash is measured from there. The starts' wolves, the city-states' monsters and the rest stay home.
- **The cavern** (issue #65, `CavernTests`): on maps at least 384 blocks across, the cavern under the hills has
  three nests of **cave crawlers** spread along it, from near the way in to near the far end, and a **cave wyrm**
  alone in its deepest chamber (the furthest on foot from the way in). A crawler (40 hit points, quick, piercing
  bites) is weak alone, but a nest of five comes at once; a nest pays 30 iron and 40 coal and leaves no den. The
  wyrm is the cavern's boss: 700 hit points, slow, armoured all over, with a crushing bite; it pays 150 stone and
  120 iron, and its den gives the **wyrm crystal**. In the data, `cavern` in a `lair` is `along` or `deepest`, with
  `count` lairs. A bigger map's further hill ranges each have a cavern of their own (issue #67; two on the
  default map), with the crawler nests but no wyrm: the wyrm lives in the main one only.
- **Guarding**: wolves attack the nearest unit within 12 blocks of their den (also workers; not more
  than 4 levels above or below it). One chased more than 20 blocks out (the leash) breaks off and goes
  home, and with nobody near they go back to the den. Monsters never break and run.
- **Clearing**: a lair is cleared when none of its pack is left wild. If whoever beat the last wolf
  cannot tame monsters (the Settlers), they get the lair's reward, 150 food and 20 iron, in their
  storage nearest the den.
- **Beaten, not killed**: a wolf that an Orc unit would kill is beaten instead. It stays where it fell
  with 1 hit point, now the Orc player's, and does nothing until they choose (`TameCommand`): **ride**
  it (the nearest spearman of theirs within 12 blocks climbs on and becomes a wolf rider, with speed 9
  instead of 4, keeping its share of hit points; the wolf is gone) or **recruit** it (it joins
  the army as it is, back at full health). Beaten wolves count as a kill, take housing and eat; an
  explicit attack order or a blast can still finish them.
- **Data**: a monster is a unit with the `monster` role and a `lair` (pack size, guard and leash
  radius, reward); `ride` says which unit rides it and what that unit becomes (the wolf rider is an
  Orc unit that is not trained). A faction with `tamesMonsters` beats monsters instead of killing them.
- **In the game**: when your units beat a monster, the Beaten button in the top bar counts the ones
  waiting and selects the next on each click (a box around several selects them all). They have a gold
  ring under them, and their panel has Ride (greyed out until a spearman is near enough) and Recruit.

### Cleared dens

`DenTests` (issue #3): once a lair is cleared, its den can be used once, by a player with one of their
units within 8 blocks of it (`DenCommand`). What it becomes is that faction's prize for beating that
monster, the lair's `prizes` in `units.json` (`PrizeTests`): each monster has its own, and two factions can
share one. A faction with no prize there leaves the den be, and a monster with no prizes leaves no den.
The den is gone from the map either way.

| Monster | Settlers | Orcs |
| --- | --- | --- |
| Wolf | a spearman becomes a Wolfheart | a wolf lodge |
| Wild boar | a boar pen | a boar pen |
| Stone giant | a fighting unit takes up the giant's heart | none: they keep the beaten giant itself |
| Bog horror | a fighting unit takes up the bog lantern | the same: the bog lantern |
| Giant crab | a fighting unit takes up a crab shell shield | crab pots |
| Cave crawler | none | none |
| Cave wyrm | a fighting unit takes up the wyrm crystal | the same: the wyrm crystal (and they keep the beaten wyrm) |
| Dusk Warden | the Dusk Staff, by healing the tree (see "Dusk tree") | the beaten warden itself |


- **Orcs: the wolf lodge.** A site for a wolf lodge goes up on the den (or as near as the ground allows,
  its middle at most 4 columns off), paid for like any building (30 wood), and the workers among the
  selected units go to build it (15 seconds for one worker). Finished, it houses 5, like a house, and
  its wolves bring in 5 food every 15 seconds (about one worker on a farm) into the nearest storage.
  It is never in the build menu (`fromDen` in `buildings.json`); `yield` and `yieldTime` are what a
  building brings in on its own.
- **Settlers: the Wolfheart.** The Settlers' `den` names a form (`{ "form": id }`, a unit with `fromDen`
  in `units.json`): the selected spearman nearest the den becomes a **Wolfheart** at once, free. It is a
  form no spearman reaches through experience, and it is not among the forms in the evolve row or for
  Auto. A Wolfheart wears the pelt of the pack: 110 hit points, 12 damage, speed 5.5, sight 12 and a
  little more armour (head 1, torso 2), and it eats 3 food a minute. With no spearman near the den, the
  den cannot be used. The numbers are first guesses for playtesting.
- **Anyone: the boar pen.** At a cleared boar wallow either faction's workers can put up a boar pen
  (25 wood, 12 seconds): it houses 2 and its boars bring in 8 food every 15 seconds.
- **Settlers: the giant's heart, a relic.** A relic (`{ "relic": id }`, from `data/relics.json`) is a
  one-of-a-kind thing a unit takes up: the selected fighting unit (not a worker) nearest the den that bears
  no relic yet takes it up at once. The giant's heart gives its bearer 60 more hit points (it gets them
  at once too) and 2 more armour on every part. A unit bears one relic at most, keeps it when it evolves,
  and drops it where it falls when it dies (see "Dropped relics and the Dusk Staff"). The bog lantern adds 4 damage to each
  hit, the crab shell shield 3 armour on every part, and the wyrm crystal 40 hit points, 6 damage a hit and
  1 armour on every part.
- **Orcs: crab pots.** At a cleared crab den the Orcs' workers set crab pots (20 wood, 10 seconds): they
  house 2 and bring in 6 food every 15 seconds.
- **Stays as it was:** the Settlers still get a lair's reward for clearing it.
- **In the game**: with your units selected near a cleared den, the selection panel has a button for
  it ("Make the wolf den a wolf lodge", greyed out while you cannot pay, "Make a spearman a
  wolfheart at the wolf den", or "Take up the giant's heart at the stone giant den"). A bearer's panel
  names its relic ("Spearman with the Giant's heart"). Computer players leave dens be.

`MonsterTests` has the issue's check: Orcs clear a wolf lair, recruit one beaten wolf and ride another
(a spearman becomes a wolf rider); Settlers who clear one get its reward instead, as does whoever beats
the last wolf. All the numbers are first guesses for playtesting.

### The dusk tree

`src/Sim/Units/DuskTreeSystem.cs` (tests in `DuskTreeTests`): every map has exactly one dusk tree, a
large bare tree in the hills about halfway between the starts, and a monster guards it.

- **The tree**: `RegionGenerator.GrowDuskTree` grows it on level hill ground well away from water and
  lava, with a clearing of 8 columns where no other tree grows: a trunk of 10 blocks (`DuskWood`), four
  roots, a crooked top and six twisting bare branches (`DuskBranch`). It cannot be felled, gathered or
  dug, and nothing else is in the way of it.
- **The Dusk Warden** (`dusk_warden` in `units.json`, a monster whose `lair` has `duskTree`): one of it,
  2 columns from the trunk; 500 hit points, slow (3.5), hits hard (18 crush), armoured. It guards 10
  blocks round the tree with a leash of 16, never leaves a den and pays no reward. Orcs beat it rather
  than kill it like any monster and can recruit it (it has no rider).
- **Healing** (a faction's `duskTree` is `heal`: the Settlers): once the warden is gone (dead, or beaten),
  right-clicking the tree with battle priests selected sends them to it (`DuskTreeCommand`). A priest
  within 3 columns of the trunk tends it for 10 seconds; any other order stops it. When one finishes, the
  tree grows a violet canopy round its branches (once a game), and that priest gets the **Dusk Staff**.
- **The Dusk Staff**: +20 hit points, heals 2 more each time, and **Dusk Bloom** (`BloomCommand`): every
  ally within 6 blocks heals 25 hit points (wounds mend too), then it waits 45 seconds. If the bearer
  dies it drops the staff where it fell, and any side's unit that heals can pick it up there (see
  "Dropped relics and the Dusk Staff").
- **Razing** (`duskTree` is `raze`: the Orcs): once the warden is gone, Orc fighters sent to the tree
  hack it down: 30 seconds of one unit's work, shared among all of them. It falls as rubble (cleared for
  wood like a felled tree), and nobody can heal it any more.
- **In the game**: the tree stands in purple mist with a cold cyan glow at night. A priest tending it
  shows "(healing the dusk tree, X%)", an Orc "(hacking at the dusk tree, X%)". The bearer is drawn
  holding the staff, its panel says "with the Dusk Staff", and the Dusk Bloom button (or the `,` key)
  uses it. The computer players leave the tree alone for now.

## Dropped relics and the Dusk Staff

Issue #73. A unit that dies bearing a relic or the Dusk Staff drops it in the block where it fell
(`DroppedItemSystem`, `Simulation.Items`).

- **Picking it up**: a unit that comes within 1 block of it (and 1 level) takes it up at once, whoever's
  unit it is, enemies included. A relic goes to a fighting unit (not a worker) without a relic, as at a den;
  the staff to a unit that heals, as at the tree. Units that are beaten or running away, wild monsters and a
  city-state's guards and caravans leave it. The nearest takes it (ties to the lowest id), and gets the
  item's hit points at once. To fetch one, move a unit onto it.
- **It lasts**: a dropped item never crumbles. If the ground under it is dug or falls away it falls too,
  and if rubble falls on it it comes up on top.
- **Not dropped**: a beaten monster that is tamed instead of killed keeps what it bears. There is no
  handing an item over on purpose. A mount that is ridden off drops its relic where it stood, so a rider
  without one picks it up. Units taken off the map in test mode (Remove, Clear) take their items with them.
- **In the game**: the item stands over the ground as its pixel icon (the relic's, or Dusk Bloom's for the
  staff), bobbing in a soft light, where you can see. Computer players do not go after dropped items yet.

## Old ruins and what they hold

`src/Sim/World/RuinSystem.cs` (tests in `RuinTests`): the broken stone halls the map generator puts up in the
open grassland (`RegionGenerator.PlaceRuins`, 3 per 512 by 512 blocks, 6 on the default map) still hold what their
builders left, and a line of their writing, so the land tells its own story (the design doc's discoveries).

- **Finds** (`data/ruins.json`, `RuinFindDef`): each ruin is given one find at match start, shuffled by the map's
  seed. A find is goods, a relic or both, with a name and the words found with it: a mason's store (150 stone,
  30 iron), a sealed granary (200 food, 60 wood), an old armoury (80 iron, 40 coal), a toolmaker's chest (100
  wood, 60 stone, 40 iron) and a warden's tomb (the **Sunstone circlet**, a relic: +30 hit points, +2 damage, +1
  armour). A find with a relic goes to one ruin at most; once every find is dealt, the further ruins go round the
  ones without a relic.
- **Searching** (`SearchRuinCommand`): any player's units (not beaten, not running away) walk into the ruin, to
  the floor within a column of its middle, and search it while they stand inside its walls (within 3 columns of
  the middle). Each tick each searcher adds a tick of work to its player's search of that ruin, up to 3 of a
  player's at a time; at 12 seconds of work (12 for one unit, 4 for three) the player finds what it holds. An
  order of any other kind, a fight, running away or being beaten stops a unit searching; the work its player has
  done stays, so coming back finishes the rest.
- **Who gets it**: the first player to finish, once a match (two finishing on the same tick: the lower player
  number). Rivals' searches of it end. The goods go into the finder's storage nearest the ruin (none if they have
  no storage). The relic goes to the unit that finished the search if it can bear one (a fighting unit, not a
  worker, without a relic yet); otherwise it is left lying at that unit's feet, for any fighting unit to pick up
  (see "Dropped relics and the Dusk Staff").
- **In the game**: an unsearched ruin you have seen has a carved stone tablet floating over it, in a warm light;
  it is gone once you search it, or once you see that someone else has. Right click on the ruin (its walls, floor
  or fallen stone) sends the selected units to search it; with your units selected within 24 columns of one, a
  "Search the old ruin" button does the same and says how far your search has got. A searching unit's panel says
  "(searching the ruin, 40%)". The find comes up as an alert ("Found a sealed granary in the ruin: 200 food, 60
  wood"); hover it to read the words found with it. Computer players from Easy up search them too, and go looking for them in the fog (see "Ruin parties" under
  the computer players). The classic level leaves ruins be. All numbers are first
  guesses for playtesting.

## Natural hazards

`src/Sim/World/HazardSystem.cs` (tests in `WildsTests`, `HazardTests`, `QuicksandTests` and `RockfallTests`, issues #45, #66 and #78): ground that
hurts the units on or near it.

- **Poison bog**: patches of violet sludge (`BlockType.Bog`) on the swamp's dry mud, never near a start.
  Units walk on it, but each second on it costs 3 hit points. Workers dig it away like mud, which makes the
  ground safe. In the game (#78) bubbles swell on it and pop, and a low sickly-green haze lies over it,
  thicker at night and in the mist (a look only: `game/scripts/Ambience.cs`, `PlaceBog`).
- **Lava heat**: a unit within a column of lava, at its feet's level or the one below, loses 6 hit points
  a second.
- **Quicksand** (#78): patches of dark, wet sand (`BlockType.Quicksand`, five columns across) on the dry
  sand at the water's edge on the beaches (and on a river bank low enough for it), never near a start or by a ford, 10 per 512 by
  512 blocks of map. Units walk on it at a third of their speed, and each second on it costs 2 hit points.
  Workers dig it away like sand (it gives nothing); monster lairs are not put on it.
- All three land like falling ground does, by no one: armour does not help and nobody gets the kill. Wild
  monsters are hurt the same. A unit's panel says "(poisoned by the bog)", "(caught in quicksand)" or "(scorched by lava)" while it
  is. The numbers are first guesses for playtesting.
- **Sinkholes** (#66): round lids of pale, cracked crust (`BlockType.Crust`, five columns across) over a
  hollow, on flat grassland, forest and hill ground away from the starts, 12 per 512 by 512 blocks of map.
  The tick a unit stands on one the whole lid gives way: the units on it drop in and lose 20 hit points
  (by no one). The hole is a block deep at its edge and two in its middle, so they climb back out. Workers
  can dig the crust away (it gives nothing); monster lairs are not put on it.
- **Rockfall** (#78): loose rock (`BlockType.LooseRock`, dark weathered boulders: three along the brink and
  one on top) perched on the brink of a cliff at least three blocks high in the hills, over a field of scree
  (`BlockType.Scree`, pale broken stones) at its foot, up to four columns out and two to either side, never
  near a start or the dusk tree, 10 per 512 by 512 blocks of map. The tick a unit steps onto the scree (or
  onto the rocks), every loose rock within four columns of it and up to ten levels above comes away and is
  thrown off the brink to come down where the unit stands: what it hits loses 30 hit points (as from any
  falling ground, by no one), and it lands as stone rubble. Then the scree whose rocks are all gone is plain
  stone. Workers quarry loose rock for stone and dig scree away (it gives nothing); monster lairs are not put
  on either.
- **Keeping off**: move orders path around all of them where there is another way (see Moving). Computer
  players (every level) keep their workers off them too: they never send wood cutters to a tree whose work
  spot is hazard ground, nor lay out a building whose footprint, or the ring round it where its builders
  stand, has any (`HazardSystem.HazardNear`), and Hard digs no quarry within a lava's heat.
- **Alert**: a unit of yours killed by the ground alone raises an alert, "Your worker died in the poison
  bog", "was dragged under by quicksand", "burned beside the lava" "fell into a sinkhole" or "was crushed by falling rocks" (`GameEventKind.UnitLostToHazard`).

## City-states

`src/Sim/Economy/CityStateSystem.cs`: towns of other races, too small to win, that trade with everyone
and are won by force. There are two, the goblins and the ogres (`data/citystates.json`); everything below
holds for both, with the goblins' numbers unless the ogres' section says otherwise.

- **The town**: at match start each city-state gets a town about as far from every start area as from
  the others (on a 512 map about 96 blocks from their middle, where the ground allows): a levelled
  15×15 square with four stone huts around a market, and the goblins' totem on the market (a 3×3 stone
  plinth with a carved pole, 7 high). Nobody can build on the square, though its blocks can be dug like
  any others. Its own wolf lair lies 24 to 36 blocks further out, so the start areas reach the town
  before its lair. Each further town is placed the same way, but also kept about one and a half times
  that distance (144 blocks on a 512 map) from the towns placed before it, so the towns spread round the
  middle, and never nearer a start than that; it may lie up to two and a half times that distance from the
  middle of the starts, as the fair ground nearer in may be taken, and where the ground allows no fair spot
  it lies somewhat nearer one start. No town goes within 36 blocks of the dusk tree's warden.
- **The totem** (`goblin_totem` in `buildings.json`): attacked like any building (right-click it with
  soldiers). Whoever breaks it holds the town, and it stands again 5 seconds later in their colour.
  Breaking a held town's totem takes the town over. Your own units do not attack your totem.
- **Guards**: six goblins (three cutthroats, three slingers) stand round the market. They belong to
  whoever holds the town (the wild side while nobody does), take no orders, need no housing and eat
  nothing. They fight soldiers that come within 4 columns of the square, never workers or caravans, so
  anyone can walk through and trade. Soldiers do not pick a fight with neutral guards on their own (they
  answer one that hits them). A guard that chased a soldier out walks back to its post, and a fallen
  guard is replaced every 45 seconds.
- **Vassals**: while you hold the town its guards fight your enemies' soldiers there, it sends you a
  tribute caravan with 15 iron every minute (to your first town hall, and the caravan goes home after;
  it can be killed on the way), you alone can hire its mercenaries (`HireCommand`): goblin cutthroats
  (quick blades) and slingers, paid for in goods, at most 6 ready at a time and one more every 30
  seconds, stepping out at the market and needing room in your housing; and your caravans earn 50% more
  there.
- **Healing at the town** (issue #44): while you hold the town, your hurt units within 6 columns of its
  square heal there as at home once out of the fight 10 seconds: 2% of their hit points a second, with
  their wounds bound (`"heal": { "range": 6 }` in `citystates.json`). Other players' units heal there only
  at the usual slow rate.
- **Caravans** (`TradeCommand`): town halls train them (20 food, 40 wood). Right-click a town (or its
  totem) with caravans selected and each fetches 20 food from your storage nearest the town, walks it to
  the market, sells it for 5 iron per 100 blocks between that storage and the town, takes the iron to your
  nearest storage, and goes again. Anyone's caravans can trade, whoever holds the town. Caravans cannot
  fight, and a wolf lair between a start area and the map centre kills one that walks past it alone.
- **In the game**: the top bar has a button per town saying who holds it. It, or a click on the town,
  selects the town: its panel shows the totem's health (or when it stands again), the guards, how to take
  the town, the tribute, what its caravans trade and how many mercenaries are ready, with a Hire button
  per mercenary, for its holder only. The totem is drawn as a carved goblin pole painted in its holder's
  colour (plain while neutral), losing its top as it is knocked down. You hear when you take a town,
  lose one, and when tribute sets out.
- **Computer players** (from Easy up) send a party of 6 soldiers from 6 minutes on, once they have 8
  at home and no wave out: it fights the guards, then breaks the totem, and is tried again 3 minutes
  later if it fails. Holding a town, they hire its goblins when they can.

- **The ogres' hold** (issue #46): a second city-state, placed after the goblins and away from them. Its
  huts are stone with red tile roofs, and its totem is an ogre idol (`ogre_idol`: a stone pillar 8 high on
  a 3×3 plinth, carved with a horned ogre face, painted in the holder's colour). It is guarded by two ogre
  brutes and two ogre hurlers (a fallen one is replaced every 60 seconds), and a stone giant's lair lies
  beyond it.
  Its holder gets 30 stone every minute as tribute, caravans bring back stone there (10 per 100 blocks of
  route) instead of iron, and its holder hires ogres, at most 3 ready at a time and one more every 60
  seconds:
  - **Ogre brute**: 260 hit points, speed 3.5, a club that crushes (18 damage every 1.6 seconds); 120 food
    and 30 iron.
  - **Ogre hurler**: 160 hit points, throws boulders 9 columns that crush (16 damage every 3 seconds, a
    little splash); 120 food and 60 stone.
  - Both **regenerate**: they heal 1% of their hit points every second even in a fight (`"regen"` in
    `units.json`), and out of the fight at that rate or the usual one, whichever is faster. Each eats 3
    food a minute.

This replaced the earlier influence alliance (gifts, influence from trade and lairs, raiding caravans
for influence), which is gone. `CityStateTests` has the issue's checks: breaking the totem makes the
goblins your vassals and tribute reaches your hall; another player breaking it takes the town over;
neutral guards fight soldiers but not workers or caravans; vassal guards fight the holder's enemies and
only the holder hires. All the numbers are first guesses for playtesting.

## Winning and computer players

`src/Sim/MatchSystem.cs`: every player given a town hall at the start is in the match. A player with no
finished town hall left standing is out, and so is a player who surrenders (`SurrenderCommand`; their
units and buildings stay). The last player in wins; players who go out on the same tick draw. A sandbox
with fewer than two players never ends. Who is out, who won and when are part of the state hash.

`src/Sim/AI/AiPlayer.cs` is a computer player. It plays only by giving the commands a person could
give, deciding from the game as it stands, so a match with it records and replays like any other. It
plays at one of four levels (`AiLevel`): **Classic**, the first computer player, described in the next
two points and kept as it was to measure the others by; **Easy** and **Normal** (issue #47,
`AiPlayer.Smart.cs`; before issue #2 they were called Normal and Hard); and **Hard** (issue #2,
`AiPlayer.Hard.cs` for its town, `AiPlayer.HardArmy.cs` for its army). The start screen picks the level
(Normal unless changed), and so does `--ai-level` in the game and `--levels` in the match runner.

No level is given anything a player does not get: each starts with the same town and soldiers, pays the
same prices and upkeep, and has only the goods its own workers bring in. Classic sees through the fog of
war; from Easy up a computer player knows only what its own units and buildings have seen, at Hard as
much as at Easy. The two things the fair levels take from the shape of the map rather than from sight are
ones a player knows too: the enemy starts across the map, and there is a lair on the way to the middle.

- **Economy**: all its workers are automatic (see [Automation](#automation)), so automation keeps them gathering what
  is needed, puts up houses and farms. On top of that it trains workers at the town hall (10 at first,
  one more each minute, up to 30), researches the techs in a set order, puts up a barracks once it has
  10 workers (a second after 12 minutes) and a storehouse where its workers gather far from storage.
  It keeps a minute's food in hand. When food runs short it moves a few workers onto food, and keeps
  wood for a farm rather than spending it; when wood runs short and the trees near its workers are gone,
  it sends workers to the trees nearest its town hall, out to 128 columns.
- **Army**: its starting soldiers first clear the nearest wolf lair, if they outweigh its pack (see
  "Hunting parties" below; a raid that is losing to the pack breaks off and comes home). Its barracks train a weighted mix
  of what they can (spearmen most, siege least). Soldiers at home gather 12 columns in front of the
  town hall. From 3 minutes on, when there are 12 more than the home guard (a fifth, at least 2), they
  go as a wave to the nearest enemy town hall, fighting what they meet on the way, then go for its
  defenders (soldiers within 18 columns of its door) and bring it down. Defenders that hold the wave up
  for 30 seconds without one of them falling, out of reach on a wall say, stop holding it back. Each
  wave needs 4 more soldiers than the last (half as many after 25 minutes), and a wave of at least 4
  goes anyway when the enemy has less than half as many soldiers in all. A wave down to a quarter of
  its soldiers comes home. Enemies within 32 columns of its town hall are met by every soldier at home,
  and by a wave still within 48 columns. The Orcs ride, then recruit, the monsters they beat; the
  Settlers' soldiers evolve into the form with the most damage times hit points.
- **Easy** plays by the fog of war: it goes only by what it sees and remembers seeing. Enemy buildings
  are remembered once seen until it sees the place without them; enemy soldiers for 3 minutes, or until
  it sees them fall. It is not told where anyone starts. It guesses that an enemy town hall stands across
  the map from its own (through the middle first, then straight across each way), and while it knows of
  none its fastest soldier (a rider, at least 6 blocks a second) scouts the guesses, and then, a start
  area being wider than a rider sees, the ground round them on a 16-column grid until it sees an enemy
  building; it comes home when hurt. A scout, or a wave looking for the enemy, heads for a point 5 columns
  short of a place it has not seen (where a building stands there is only the roof to stand on, and no way
  up), is done with the place once it has seen it, and each time it comes to a stop on the way tries from
  4 columns further back; a scout gives a place up after 4 stops. With fog of war on it looks at the town
  again every 3 minutes while no wave is out. Its barracks weigh what to train by the soldiers it has seen: much more of the roles
  strong against them, less of the roles they are strong against, a few sappers for each wave and at
  most two siege units. Its first wave waits until 5 minutes (meeting the classic player's 3-minute wave
  at home, by the town hall, wins that fight), and a wave sets out when big enough and not much weaker
  than the enemy army it knows of, or small when it is twice as strong. A wave marches in formation
  (melee in front, ranged behind) to a line 24 columns short of its target, then attacks. A wave that
  knows of no enemy building goes to look for one the way the scout does, and one that has razed every
  building it knew of looks for the rest, for up to 8 minutes, before it comes home. Soldiers of a wave
  below 35% of their hit points go home to the town hall's door to heal, and rejoin at 85%. A wave that
  sees an enemy army twice its strength before the fight starts falls back. Its sappers go for the
  target building whatever else is happening. It saves for its barracks before research, builds the
  second at 8 minutes, sends idle workers to trees, and after 6 minutes keeps more wood in hand.
- **Normal** is Easy, plus: in a fight its archers and mages shoot the enemy in reach they are strong
  against, else the one with the fewest hit points, and riders and spearmen switch onto enemies they
  are strong against; up to 36 workers; a third barracks after 15 minutes when rich; after 10 minutes a
  second town hall where its workers gather far from storage; and after 9 minutes, with the wood, a
  wall of barriers round the town hall 4 columns out with two gates three wide, for a faction it plays
  balanced (the Settlers; see the styles below). Once a minute after that it looks whether it can pay
  to upgrade the whole wall a tier with 100 of each good left over, and does: to wooden walls, then
  (with masonry) to stone.
- **Hard** knows and has no more than Easy; it plays as well as it can with it. Its **town**: it runs
  its workers itself. They are automatic, but on its own priority lists (building, then food or wood,
  and nothing else, so none wanders off to dig), and every 8 seconds it moves up to two between the
  farms and the trees to keep the share it wants on the farms (45%, more as wood piles up, less as food
  does, between 25% and 70%), and sends idle ones to the nearest trees it knows of. A tree with few
  others round it has work for only a few: those that come back with nothing to do are sent on to the
  next nearest trees, not at the same ones again. It trains workers at every town hall it has
  (12 at first, 3 more each minute, up to 48). It pays for things in order of need and saves up for the
  first it cannot pay for: houses before it is out of room; soldiers while its army is short (see
  below), unless its town is too small to pay for one (fewer workers than 12 and one more for each
  minute played, up to 48), when workers come first; workers; the first barracks at 8 workers; a storehouse where 3 or more workers gather 14
  columns or more from storage; research in its own order (farming, tools, crop rotation, horse
  breeding, roads, carts, archery, masonry, then the rest); farms for its farmers; another barracks (up
  to 6) when every one is training and goods are still piling up; its wall, for a faction it plays
  balanced (below); a second town hall 44 columns out on
  the side away from the enemy once it has 30 workers and 2 barracks; and soldiers again with whatever
  is left. While it has not the stone for something it is saving for, it keeps only the stone in hand
  and goes on spending its food and wood: stone comes in slowly, and what waited for it would be soldiers
  not trained. Upkeep is paid once a minute, so it puts the next payment's food by over the minute and is
  never caught short. For stone it marks out a quarry early (a pit 14 by 6 dug 12 deep, 14 to 30
  columns from the town hall; a smaller or further one if after 2 minutes there is no such ground),
  builds nothing there, and from 16 workers on keeps two to five of them digging it while stone is
  short. While enemies are at its buildings, and for 20 seconds after, it saves for nothing but
  soldiers.
- **Hunting parties** (Easy, Normal and Hard, issue #78; `AI/AiPlayer.Hunt.cs`, tests in `HuntTests`):
  from 4 minutes on, with no wave out, it clears the lairs spread across the map (boar, stone giants, bog
  horrors, crabs, wolves; not the cavern's or the dusk tree's) for their rewards and prizes, but weighs each
  pack first. A pack is worth its monsters' hit points times their damage a second, as the computer weighs
  soldiers. It goes for the nearest lair it has seen within 200 columns of its town hall that the soldiers it
  can spare (all at home but the guard and the hurt) outweigh two to one, and sends just enough of them,
  nearest the den first (at least 2). A lair too strong for what it can spare is left alone, however near.
  The party goes for the pack where it sees one of them (packs roam), else the den. When what is left of the
  party is worth less than what is left of the pack it breaks off and comes home, and that lair is left
  alone for 10 minutes; it also comes home after 4 minutes. After a hunt the next may set out a minute
  later. Its hurt go home to heal.
- **Ruin parties** (Easy, Normal and Hard; `AI/AiPlayer.Ruins.cs`, tests in `AiRuinTests`): from 6 minutes
  on (after the early rushes; a pair out looking sooner found lairs that drew Hard's hunting party away just
  as Classic's first attack came), never while it sees enemy soldiers within 64 columns of its town hall (Hard:
  nor for 20 seconds after an attack), and with 2 soldiers to spare (fit, not the scout or explorer, beyond the
  guard; a wave may be out), it
  sends those 2 to search the nearest old ruin it has seen within 240 columns of its town hall (see "Old
  ruins and what they hold"). It passes a ruin with a lair it knows of within 20 columns, or where what it
  sees within 20 columns outweighs the pair. Any of the pair a fight took off the search is sent back to it.
  The party comes home once the ruin is searched (by it or anyone it sees), after 3 minutes, or when it is
  outweighed there: then that ruin is left alone for 10 minutes. With no ruin seen, the pair goes looking,
  as a player would: it sweeps round the town hall in rings from 128 columns out (ruins stand at least 136
  from a start on the default map), to the unseen ground nearest it on a 24-column grid, and searches the
  first ruin it sees (a ruin counts as seen once its middle or a corner of its floor is). It comes home
  after 3 minutes of looking, or when what it sees around it outweighs it. The next party may set out 20
  seconds after one ends.
- Hard's **army**: its starting soldiers clear the lair as the others' do, but its fastest stays to
  explore round its town (a 28-column grid out to 112 columns), which is how it finds trees, lairs and
  city-states. Its soldiers at home gather beside the town hall (6 columns out from its south side), not
  12 columns out toward the enemy: there they heal as at home, and raiders come up against the town and
  all of them at once. It builds nothing within 8 columns of that ground, and a soldier shut in where it
  stands (the ground levelled for a house can leave a hole beside it) is left out of everything. It
  takes the enemy army to be worth what it has seen of it in the last 3 minutes, never less than the
  army a player starts with, and for 6 minutes after one of its own waves was beaten, what that wave
  was worth. While its own army is worth less than 120% of that, soldiers come before everything but
  houses. Its barracks weigh a unit as Easy's do, times what it is worth in a fight for its price. It
  scouts the enemy town only when it means to attack (every look risks a rider, who comes home at 85%
  of its hit points): a wave of at least 12, leaving 15% at home (a quarter for a faction it plays
  balanced), sets out when it is worth 150% of the
  enemy army and a scout has been for a look at their town in the last 3 minutes, or been chased off
  trying (with no one fast enough to scout, it goes without). Soldiers trained while the wave is out
  follow it in groups of 8. While a wave of its own is looking for the enemy (it set out for a guess, or
  has razed every building it knew of), a rider looks too, one of the wave's if it has one: it covers
  the ground several times as fast, and goes back to the wave once there is something to go for. A wave in a fight falls back when it is worth less than 60% of the enemies
  it sees round it, and comes home when it is down to a quarter of its soldiers; either way it counts
  as beaten. At home, enemies within 26 columns of a town hall of its own or of where its soldiers
  gather, or 14 of any other building, are met in formation, so its soldiers arrive together: by a town
  hall, by everyone; by a far building, a few raiders by a party worth three times as much, while a
  real force (half its own worth or more) is waited for in a line 10 columns out from the town hall,
  where its new soldiers come out. A wave that is out is called home only for enemies worth more than
  80% of the soldiers at home, and from more than 48 columns away only when they are by a town hall. It
  fights with Normal's focus fire, sends totem parties and hires as the others do. It does not raid
  yet, and in a long match it runs short of wood once the trees near its town are gone.
- **Styles**: how the computer plays a faction is in the game data (`aiStyle` in `data/factions.json`).
  The Orcs are played in **waves**: no walls, and nearly every soldier goes with the wave. The Settlers
  are played **balanced**: a walled town, and attacks that leave a good part of the army at home behind
  the wall. Easy and Classic play both the same; Normal walls only a balanced faction. Hard, balanced,
  marks out the line of its wall before it builds anything: round the middle of its town, 16 columns out
  from the town hall and 20 on the side of its door (where its soldiers gather), so its first farms and
  houses stand inside as near the town hall as they would without a wall (farmers carry every load to
  storage). There is a gate three columns wide in the north and south sides and two in the west and the
  east. It builds nothing on the line, right beside it, or within 4 columns of a gate in line with it,
  and digs no quarry within 3. It starts on the wall once it has a barracks and 16 workers, the side
  toward the enemy first: it saves up for a side, then lays it from one end to the other with never
  more than 6 columns waiting to be built (every site calls the nearest worker with nothing to do, so a
  whole side at once would call in half its workers). What it lays is barriers, so the whole ring is up
  for 196 wood. It then upgrades the wall out of what it can spare: only goods that nothing it is saving
  for needs, with 100 wood (and, for stone walls, 60 stone) left in hand, a column at a time with the 6
  waiting columns as before, the side toward the enemy first, and every column to wooden wall (516 wood in
  all) before the first to stone (1636 stone and 72 wood, after masonry, which pays for the facing that
  makes the wall three columns thick; a column that will be faced counts as three of the 6 waiting, as its
  two facings are sites too). While the wall is all it is
  short of stone for it keeps at most 3 workers at the quarry. Every 10 seconds it looks the wall over
  and lays again what has been knocked out (as a barrier, to be upgraded again). Its archers and mages at
  home are posted on the wall by the gate nearest the enemy, as many as there are free columns of walk
  along that side within 12 columns of the gate (on a stone wall, both edges of its deck), and stay up there when the enemy comes within 20 columns
  of them (the rest of its soldiers meet the enemy as before). A town too near the edge of the map for the line to fit gets no wall. Its waves
  leave a quarter of its soldiers at home, not 15%, and the part it sends is what has to be worth 150%
  of the enemy army, so it attacks with a bigger army in all than the Orcs would. At home its soldiers
  do not go out after raiders at a building more than 24 columns outside the line of the wall (they
  would leave through the gates a few at a time for a fight far from home, and be beaten in parts):
  they meet the enemy at the town. A town hall is always defended. The whole wall is 160
  columns (142 of wall and 18 of gate). Over 20 seeds against Hard's Orcs the ring of barriers was whole
  at 8 to 14 minutes (11 on average; the stone wall it used to build was started on its last side at 17),
  the first columns were wooden at 10 to 15, and at 20 minutes 63 of the 160 were, on average. After that
  it goes slowly, as wood is what Hard runs short of: in the nine matches that went the full 40 minutes
  44 to 160 columns were wooden at the end (96 on average), and one had the whole ring wooden (at 28
  minutes) and had started on stone. A barrier holds a group up for a few blows; a wooden wall takes
  four times as much, a stone wall eleven times for each of its three columns, and archers shoot from the
  top of both. The computer players do not build towers yet. Hard does not
  fight from behind its wall yet, though: all but its posted archers still go out to meet whoever comes
  near the town, so the wall is worth less to it than it would be to a player who waits behind it.
- `scripts/ai-versus.sh` plays two levels against each other over 20 seeds with fog of war, the first
  level on a different side of the map each seed. With issue #2, Hard beat Normal and Classic in all
  20; Normal beat Classic in 12 (6 lost, 2 undecided after 40 minutes) and Easy in 9 (11 lost). Since
  the styles Hard still wins all 20 against each, but playing the Settlers balanced it takes longer
  over Normal (28 minutes on average, 22 before; 21 as the Orcs, as before). Hard's Settlers against
  Hard's Orcs (`--levels=hard,hard`) over 20 seeds: the Settlers won 2, the Orcs 10, 8 undecided (before
  the styles 5, 8 and 7): the wall costs the Settlers about a tenth of their army at 15 minutes and a
  quarter of it stays home, and a wall does not yet give that back.
  With the wall tiers (walls laid as barriers and upgraded) the numbers are about what they were: Hard
  beat Classic in all 20 and Normal in 19 (as the Settlers in 29 minutes on average, as the Orcs in 21);
  Normal beat Classic in 11 (7 lost, 2 undecided) and Easy in 9 (11 lost); Hard's Settlers against Hard's
  Orcs won none, lost 11 and left 9 undecided. The match Hard lost to Normal (seed 19, as the Settlers)
  went at 6 minutes: its soldiers followed raiders seen from its half-built wall out past it, and met
  Normal's whole first wave outside the town.
  With the bigger walls (stone walls three columns thick, at 10 stone a column) and the towers, Hard beat
  Classic in all 20 and Normal in 18 (1 lost, the same seed 19, and 1 undecided after 40 minutes; as the
  Settlers in 28 minutes on average, as the Orcs in 21).
  Before issue #2 Normal (then called Hard) was told where the enemy starts and beat Classic in 15:
  finding the enemy itself costs it and Easy some of that, and Classic still sees through the fog.

`AiMatch.Play` plays computer players against each other from the usual start (town halls, four of every
trained unit, the lairs and the city-states), and `--ai` in the match runner does it from the command
line, prints the winner and fails if there is none:

```bash
dotnet run -c Release --project tools/MatchRunner -- --ai --seed=3 --record=ai.log   # at most 40 minutes
```

CI runs `scripts/ai-matches.sh`, issue #15's check: on seeds 1 to 5 (512×512 maps) every match must be
won within 40 game minutes, and replaying its log must give the same hashes and winner. Since issue #47
the matches are Hard against Classic with fog of war; Hard plays the Settlers there, and since
it plays them balanced they are won in 23 to 34 minutes (20 to 27 before).
All the numbers are first guesses for playtesting.

## Terrain magic

`src/Sim/Units/SpellSystem.cs`: spells that reshape the ground, made of the same blocks digging and building
use (the design doc's "Shape with magic"). A unit type has its spells in `data/units.json`; the mage has four.

- **Earth Wall** (`EarthWallCommand`, `"earthWall": true`: the mage): a wall of earth rises along a line
  (the line a wall is laid along) of up to 8 columns, 3 blocks high from the ground, so nobody walks through
  or climbs over it. Its top is grass, the rest soil. The line's middle must be within 12 blocks of the mage;
  one further away walks up and casts once in range (any other order on the way calls it off, and costs
  nothing). Of the selected units, the mages that are ready cast together (see Group spells below).
- **Where earth rises**: on ground a unit stands on, but not where a building or a city-state's town is, on
  water, or in a column with a unit standing in it (or right at its edge). Leaves in the way are pushed aside;
  an overhang stops the column short.
- **How long**: the wall stands 45 seconds, then the earth still there sinks back into the ground. Until then
  workers can dig through it (it gives nothing) and sappers can tunnel it, like any earth; earth dug out from
  under falls as rubble, which stays.
- **Waiting**: a mage that cast a spell (either one) waits 40 seconds before it can cast again; others are
  not held up.
- **Ice Bridge** (`IceBridgeCommand`, `"iceBridge": true`: the mage): the mage freezes the water into a
  level way of ice from bank to bank, the way a bridge is laid: both ends must be ground about level with each
  other (at most a climb apart), and the ice runs one block under the higher end's standing level, in every
  column of the line whose ground is lower than that, and in the column on each side (across the line's longer
  axis), so an army crosses three abreast. At most 16 columns of the line take ice. Some column of the line
  must have water under it: the ice is frozen out of the river, so a dry trench or chasm gets none. Where the
  banks are as low as the water the water's top freezes; where they are higher (the river's steep banks) the
  ice stands over the water at their height. Ice forms in open air or water (leaves are pushed aside) with
  room to stand above it, and not where a building or a city-state's town is. The middle of the line must be
  within 12 blocks of the mage, which otherwise walks there, or to the nearest dry ground beside it, first.
  A line that spans nothing (its ends not ground, or no water between) freezes nothing and costs nothing.
- **Ice**: anyone walks on it, but it is slippery: a unit on ice keeps seven eighths of last tick's slide
  and puts only an eighth of its step where it wants to go, so it takes about a second to get up to speed,
  swings wide on a turn, and slides on a block or two after it stops (bumping into something stops it that
  way). Nobody digs it, and earth does not rise on it. It holds 30 seconds, shows dark
  cracks in its last 5, then melts, back into water where it was water and into air above that. A unit still
  on it falls to whatever is under it: into shallow water it wades on, but with only deep water under it, it
  drowns (as if crushed: nobody gets the kill).
- **Chasm** (`ChasmCommand`, `"chasm": true`: the mage): the ground along a line (the line a wall is laid
  along, as long as an earth wall) opens 3 blocks deep, deeper than a unit climbs out of. Whoever stands on it
  falls in and takes 15 damage, as from falling ground (nobody gets the kill), and is stuck at the bottom,
  where archers on the edge have them, until the chasm closes. The middle of the line must be within 12 blocks
  of the mage, which otherwise walks up first.
- **Where the ground opens**: natural ground (earth, sand, stone, ore, rubble) all the way down, so not a
  tree's trunk, a building or a city-state's town, and not where water or ice is on, in or beside the pit
  (a chasm is not a channel from the river). Loose ground around it may still fall in, as when digging. A line
  with nothing to open costs nothing.
- **Closing**: after 30 seconds the ground closes as it was (stone back as stone, ore as ore), bottom up,
  wherever it is open again; ground that fell in stays. Units in it are lifted back out onto the top, unhurt.
- **Grow Forest** (`GrowForestCommand`, `"growForest": true`: the mage): full-grown trees spring up along a line
  (the line a wall is laid along, as long as an earth wall) on every other square of the tree grid (one column, or
  2 by 2 on the default map), so the line becomes a row of trees with a gap between each two. Trunks stop arrows
  and feet, so a grove is cover for archers and a screen an army files through a few at a time; the crowns are
  high enough to walk under. The trees stay: they are ordinary broad-leaved trees, and wood cutters fell them for
  wood. A tree grows where every column of its trunk is natural ground (earth, sand, stone, ore) at one level with
  open air (or leaves, pushed aside) over it, and not on a building, a city-state's town, water, ice, a hazard or a
  unit standing there. The middle of the line must be within 12 blocks of the mage, which otherwise walks up
  first. A line with no room for a tree grows nothing and costs nothing.
- **Group spells** (issue #39): every ready mage among the selected casts the spell together, up to 3. The
  ones in range wait while the others walk up, and the spell goes off once all of them are in range (one given
  another order on the way drops out, and the rest cast without it). Each mage beyond the first makes an earth
  wall or a chasm one column thicker (the line, then the column before it, then the one after) and 4 columns longer (8,
  12, 16), an ice bridge 2 columns wider (3, 5, 7 abreast), and a grove a row of trees thicker: each new row two
  squares of the tree grid to one side (then the other), its trees on the gaps between the first row's, so the grove
  is staggered. Every mage in the group then waits its 40
  seconds; a mage still waiting does not join.
- **In the game**: the Earth Wall button (Shift+Y), the Ice Bridge button (Shift+'), the Chasm button (Shift+Z) and the Grow
  Forest button (Shift+L) show with a mage selected, greyed out with the wait while none of the selected is ready. All numbers are starting guesses to
  be tuned in playtests.

## Fog of war

`src/Sim/VisionSystem.cs`: with fog of war on, each player sees only what is within sight of their
units (each unit type's sight in `data/units.json`, counted from the column it stands on) and their
buildings (a building's `sight` in `data/buildings.json`, in columns beyond its footprint: 10 for a
town hall, 6 for the others). Every 4 ticks, the same on every machine, each column is worked out for
each player as never seen, seen before, or in sight now. How much each player has seen is part of the
state hash. With fog of war off, every player sees everything.

In the game (`game/scripts/FogOfWar.cs`), ground you have never seen is black. Ground you have seen but
cannot see now is dimmed and greyed, and shows the land and buildings as they were when you last saw
them (`src/Sim/WorldMemory.cs`): a change out of your sight shows once you see that column again.
Other players' units, their health bars and their shots show only where you can see, and buildings and
towns can only be picked where you have looked. When the match is over, the fog lifts.

The start screen has a Fog of war switch, on unless switched off (or started with `--fog=off`). A match
started from the command line has fog of war only with `--fog`. With day, night and weather, sight is
shortened in the dark and in bad weather, except into the light (see [Day, night and weather](#day-night-and-weather)). It is recorded in the command log (a
`fog 1` line), so a replay plays back with it. Computer players from Easy up go by it (see
[Winning and computer players](#winning-and-computer-players)); the Classic one sees everything.

## Day, night and weather

`src/Sim/Conditions.cs`: with day, night and weather on (`Simulation.DayNight`), the time of day and the
weather are game rules that change how far units see and how well they shoot. All the numbers are
starting guesses to be tuned in playtests.

- **Clock.** A day lasts 20 minutes of game time and a match starts at 8:00 (or at the hour set with
  `--time-of-day`). Dawn is 5:00 to 6:30, dusk 19:30 to 21:00, night in between.
- **Weather** rolls in and out: clear, cloudy, rain, storm, or a morning fog. A spell lasts one to five
  hours of the day, and when one begins the next is already decided, so the sky (and the HUD's tooltip)
  shows it coming. What comes next leans on what there is: a clear sky clouds over before it rains, rain
  can build to a storm, a storm dies down to rain, and fog only rolls in during the early morning and
  burns off by 11:00. The first three hours are clear. The rolls use the simulation's own random stream,
  stirred by how many units and buildings there are, so no two matches get the same weather, but every
  machine and every replay does. The weather is part of the state hash.
- **Sight** (units and buildings under fog of war, and how far soldiers look for enemies): dusk and dawn
  85%, night 60%; rain 85%, storm 70%, fog 60%; these multiply, and in the swamp's mist (at night and
  until 9:00) a unit sees a quarter less again; never below 40%.
- **Light beats darkness.** From dusk to dawn, ground near a light is lit: lanterns on town halls (4
  columns beyond the walls) and on houses, storehouses, barracks and smelters (3), torches (7),
  campfires (9) and lava (3). What stands on lit ground is seen from as far as by day (the weather still
  counts) and shot at as by day. A storm puts out the open flames of torches and campfires until it
  passes; lanterns are sheltered. A building's light is `light` in `data/buildings.json`.
- **Aim.** A ranged shot at a unit lands every time on a clear day. At night it misses 20% more often
  (dusk and dawn 10%) unless the target is lit; rain adds 10%, a storm 25%, fog 15%; at least half of
  shots always land. A miss lands beside the target. Melee blows and shots at buildings always land.
  Whether a shot lands is a hash of the tick, the shooter and the seed.
- **Torches and campfires** (`kind: light`) are cheap buildings: a 1-block post of planks two high, or a
  ring of stone one high, that workers build like any other.
- **Lava** lies in a few pools up in the hills, well away from the starts. Nothing stands on it or digs
  through it, and it lights the ground around it at night.

The start screen has a Day, night and weather switch, on unless switched off (or started with
`--daynight=off`); a match from the command line has them only with `--daynight` or `--time-of-day`. They
are recorded in the command log (`daynight 1`, and `daystart M` for a start at another minute of the
day), so a replay plays back with them. With them off it is always a clear day and nothing changes.

In the game (`game/scripts/DayCycle.cs`, `WorldLighting.cs`, `Ambience.cs`) the light, the sky and the
weather follow the rules' clock and weather; see [Controls and HUD](playing.md) for how it looks.

## Start screen, match end and replays

Started without command-line options (or with `--menu`), the game opens on the start screen
(`game/scripts/StartScreen.cs`). Each player has their faction (see [Game data](../README.md#game-data)) and generated maps have
two starts, so picking the Settlers or the Orcs picks the player and start too: player 0 in the west or
player 1 in the east. The world seed comes up at random (New seed draws another), and the computer
opponent is on unless switched off, which leaves the other side standing idle. So is fog of war (see
[Fog of war](#fog-of-war)). Matches from the start screen have no terrain tool. With options, the game starts a match
at once as they say (seed 1234 unless given, the computer only with `--ai`, fog of war only with
`--fog`), as the tests run it.

The Surrender button under the top bar asks first, then gives up the match. When the match is over for
you (you won, lost your last town hall or surrendered), the clock stops and `MatchEndPanel` shows
Victory, Defeat or Draw, how it came about, the match time and the seed. Look around hides it (the
button under the top bar, now Results, brings it back), New match goes back to the start screen and
Quit leaves.

Every match from the usual start is recorded as it is played: `Simulation.Recording` keeps each command
on the tick it is carried out (automation's own orders are left out, since a replay gives them again),
under the header `CommandLog.UsualStart` writes for the start the game sets up
(`Simulation.SetUpUsualStart`). At the end the log is saved as a replay in `user://replays` (on Windows
`%APPDATA%\Godot\app_userdata\RTS\replays`; change with `--replays=DIR`), named after the time and
seed, with a comment line saying who played whom and how it ended. The match runner plays it back and
prints who won (see [Headless matches and replays](../README.md#headless-matches-and-replays)). A match set up with `--map`, `--units`, `--battle`,
`--goods`, `--houses` or `--xp` would not replay from such a log, so it is not saved, and the panel
says why.

## Test mode

For building a scenario to try something out (issue #89). Tick **Test mode** on the start screen (or
start with `--test-mode`) and the match has a test panel on the right (`game/scripts/TestPanel.cs`; F1
hides it), the terrain tool (T) and an 8x speed. Everything the panel does is a test mode command
(`SandboxUnitCommand` and the rest in `Commands.cs`, carried out by `Sandbox/SandboxSystem.cs`), so a test
match is recorded and replays like any other. The simulation carries them out only when the match was
started in test mode (`Simulation.TestMode`, set before the first tick and part of the state hash), so they
cannot change a normal match, a computer players' match or the determinism check.

- **Place**: pick what to put down and click the map, as often as you like (right click or Esc stops):
  any unit type, for either player or as a wild monster, in a group of up to 100; any building but a
  city-state's totem, finished and free (a town hall for a player who has none brings them into the
  match); a tree of each kind; a square of blocks (stone, soil, grass, sand, mud, iron ore, coal,
  crystal, berry bushes, water or lava stacked on the ground, or air, which digs that deep and clears
  what stands above); a monster lair with its pack; fresh blood; or the eraser, which removes the units
  and buildings around the click (city-state totems stay). Water put down at or under the sea's level runs
  into the open ground beside it, up to that level; higher up it stays where it is put, since water never
  runs out and would flood the land.
- **Units**: heal (full health, wounds and bleeding gone, morale back), kill (as by falling ground: they
  fall and bleed, and nobody gets the kill), remove, wound (bleeding from the legs for 12 seconds) or
  season (the most experience there is) the selected units, anyone's; and play the other side (with fog
  of war off).
- **World**: the weather (for four hours) and the time of day, with day and night on; 1000 more of every
  good, or every tech, for either side.
- **Cheats**, for either side: instant building (sites go up at once, with or without workers), instant
  training (housing and food still count), instant research, and invulnerability (units and buildings
  take no damage and do not bleed; a test mode kill still kills).
- **Save**: one tick at a time (F2, which pauses), and saving the scenario: the match's command log, in
  `user://scenarios` (change with `--scenarios=DIR`). Loading a saved one (from the list there, or
  `--scenario=PATH`) plays it back from the start to the moment it was saved, paused, with the camera where
  it was. A scenario is a plain text command log, so it can be written or edited by hand (see
  `Replay/CommandLog.cs` for the `sandbox-*` lines).

## Alerts and idle workers

`src/Sim/EventLog.cs`: the simulation keeps a log of what each player should hear about, filled as it
happens the same way on every machine: your units or a building of yours hit by another side (or a
capture or burning of it begun), a building site finished, research done, a unit trained, your town hall
destroyed or captured, an enemy town hall you last hit destroyed, or one you captured, and a unit of
yours killed by a natural hazard. Under attack
events are kept to one per place (within 16 columns) every 5 seconds per player. The log keeps the
newest 256 events, each with a sequence number that only grows, so the game asks for those after the
last one it saw. Nothing in the simulation reads it, so it is not part of the state hash.
`EconomySystem.IdleWorkers` lists a player's workers with nothing to do: standing with no order and no
job (automatic ones too, while they find no work).
`CombatSystem.ArmyUnits` lists a player's combat units: every unit they own that is not a worker or a
caravan, leaving out beaten monsters not yet ridden or recruited.

In the game (`game/scripts/Alerts.cs`), under the top bar on the left, the Idle workers button shows how
many of your workers are idle; each click (or the . key) selects the next one and moves the camera to
it. Under it the Army button shows how many combat units you have; a click (or F2) selects them all.
Below those are your latest five alerts, newest first, red for attacks and losses, green for buildings
finished and town halls won, blue for research. Clicking one moves the camera to where it happened. The
same alert again within 10 seconds is counted on the one showing ("x3"); an alert goes after 20 seconds
on screen, not counting time paused.
