# RTS

A 3D voxel real-time strategy game: one long match on a newly discovered continent,
with diggable terrain, rival races, monsters, magic and deep economy. Built with Godot 4 and C#.

Design doc: https://claude.ai/artifact/NUFTDiS6HPWihhhmrhiyQm

## Playing

Every pull request and every push to `develop` produces a Windows build: download the
`RTS-windows-...` artifact from the run on the **Actions** tab, unzip it and run `RTS.exe`.

- [docs/playing.md](docs/playing.md): getting a build, the start screen, the controls and the HUD.
- [docs/rules.md](docs/rules.md): how each system works (economy, combat, automation, evolution,
  monsters, city-states, winning and computer players, fog of war, match end and replays, alerts).

## Developing

```bash
scripts/setup.sh                    # .NET 8 SDK + Godot 4.7.2 (mono)
dotnet test RTS.sln                 # simulation tests
scripts/screenshot.sh out.png       # render the game to a PNG on a virtual screen
scripts/ui-screenshots.sh [DIR]     # scripted mouse and keyboard: box select, pause, height slice
scripts/ui-economy-screenshots.sh   # scripted economy: gather, research, build, train, road
scripts/ui-combat-screenshots.sh    # scripted battle: attack-move, tunnel under a house
scripts/ui-automation-screenshots.sh  # scripted automation: automatic workers, repeat, a house put up on its own
scripts/ui-supply-screenshots.sh      # scripted supply rules: a storehouse told to keep stone and wood is stocked from the town hall
scripts/ui-governor-screenshots.sh    # scripted town governor: a Defence governor runs the town, putting up a barracks and four watchtowers
scripts/ui-evolution-screenshots.sh   # scripted evolution: evolve buttons, auto-evolve, a knight's panel
scripts/ui-monsters-screenshots.sh    # scripted wolf lairs: Orcs beat a pack, ride one wolf and recruit one; Settlers get the reward
scripts/ui-citystate-screenshots.sh   # scripted city-state: break the goblins' totem, hire goblins, a caravan trades, the ogres' hold, the kobolds' warren, the troll bridge, the centaur camp, the merfolk cove
scripts/ui-match-screenshots.sh       # scripted match flow: start screen, surrender, defeat and its replay, victory
scripts/ui-dominion-screenshots.sh    # scripted ancient sites: soldiers walk into a ruin and take it; its banner, the sites button, the alert
scripts/ui-ascendancy-screenshots.sh  # scripted great work (--great-work): the Hall of Legends in the deep caves, the crown countdown, the win
scripts/ui-awakening-screenshots.sh   # scripted dark power (--awakening --awaken=2): the corrupted Heartscar, its button, broods rising
scripts/ui-fog-screenshots.sh         # scripted fog of war: the start screen switch, enemies hidden until in sight
scripts/ui-dig-screenshots.sh         # scripted dig tool: workers and sappers dig out a dragged area
scripts/ui-wall-screenshots.sh        # scripted walls: a gate placed, a wall drawn through it and built, upgraded twice, archers posted on it, a stone tower put onto it and a watchtower beside it
scripts/ui-bridge-screenshots.sh      # scripted bridge: a deck drawn across a river and laid by the workers
scripts/ui-ramp-screenshots.sh        # scripted ramp: a ramp drawn up a cliff, heaped by the workers and walked up
scripts/determinism.sh              # 10-minute headless match, replayed twice, hashes compared
scripts/ai-matches.sh               # Hard computer against Classic, five matches with fog; each must have a winner within 40 minutes
scripts/ai-versus.sh [A] [B] [N]    # level A against level B over N seeds with fog (default hard, classic, 20), both sides of the map
scripts/benchmark.sh [SECONDS]      # load the 512x512 region, dig 100 blocks/s, log frame times
scripts/setup.sh --with-export-templates && scripts/export-windows.sh
```

### Maps

`RegionGenerator` (src/Sim/World) builds the region from a seed with integer maths, 768×128×768 by default
(`RegionGenerator.DefaultSize`, issue #45, and `DefaultHeight`; `--world=WxHxD` picks another size, and the features are laid out
for 512 across and 64 high and scale with it). On a map at least 96 high, the land's 64 layers sit on
`RegionGenerator.DeepLayers` more of deep rock (64 on the default map), with **the deep caves** running through it:
two levels of domed chambers on a grid about `DeepSpacing` (120) blocks apart under most of the map, joined by
winding tunnels, sloping shafts between the levels, a tunnel down from each hill cavern and a few long ramps up to
open country. Their walls are seamed with iron, coal, crystal (more crystal deeper) and a little gold, crystal and stone spikes
rise from the floors and hang from the roofs, giant mushrooms stand in the upper chambers, pools of water and lava
lie in the floors, and every `DeepNestEvery`th chamber is a crawler nest (`RegionMap.Deeps` lists the chambers;
`DeepCaveTests`). A lower map has no deep rock and is laid out as before:
ocean and beaches on the west and east edges, a river running north–south that can be waded at three
shallow fords (about a quarter, half and three quarters of the way down), grassland and forests,
a hill range with stone ridges, lodes of gold ore (`PlaceGold`, see Gold in [Economy](docs/rules.md#economy)) and pools of lava, a swamp with patches of poison bog (see
[Natural hazards](docs/rules.md#natural-hazards)), a cavern with ore, crystal and a little gold under the hills, and old
stone ruins (broken stone brick walls and fallen stone to clear for stone, `RegionGenerator.RuinsPer512` for
each 512×512 blocks), each holding a find from `data/ruins.json` for the first side to search it. A bigger map gets more features, not just bigger ones (issue #67,
`RegionGenerator.FeatureSets`): one more, smaller hill range (with its own cavern, crawler nests and lava) in
the swamp's half and one more swamp in the hills' half for each further 512×512 blocks of land, so the 768
map has two of each. `RegionMap.Caverns` and `RegionMap.Ruins` list where they are. Each
player starts in a clearing near their coast with berry bushes, a grove, a stone outcrop and an iron
outcrop mirrored around it. Each biome grows its own trees (`RegionGenerator.KindFor`): broad-leaved
trees and birches in the grassland, those and pines in the forest, pines in the hills (plus the one dusk tree, see
`docs/rules.md`), and willows and giant mushrooms in the swamp; the start grove has two birches and a pine. Round the middle of a map at least `RegionGenerator.WildsMapSize` across lie the shadowed wilds (`Biome.Wilds`, `RegionGenerator.Wilds.cs`, `PlaceWilds`, laid out last from streams of their own): gloom moss (`BlockType.Gloom`) for grass, gloom pines (`TreeKind.Gloom`, `BlockType.GloomLeaves`) close together and outcrops of shadow crystal (see [The shadowed wilds](docs/rules.md#the-shadowed-wilds)).
Every kind is its own trunk and leaf block (`BlockInfo.IsTrunk`, `BlockInfo.IsLeaves`). `RegionMap.Save`/`Load` read and write a run-length-encoded binary map,
so handmade maps can be loaded with `--map=PATH` (format 3 lists every cavern, ruin and deep chamber; formats 1 and 2 still load).
The `ui-*` screenshot scripts and CI's AI matches play the default map (issue #67); the ones CI skips
(`ui-structure`, `ui-tree`, `ui-collapse-detail`, `ui-lineup`, `ui-sculpt`, `ui-detail`) still pin
`--world=512x64x512`, which their coordinates were written for. `--focus=start0|start1|cavern|dusk|X,Z` points the camera; `--dusk-party=N` puts a party of yours near the dusk tree.

### Units

Units live in the simulation core as flat arrays (`src/Sim/Units/UnitStore.cs`) with fixed-point
positions and stable ids. A move order sends a group to a point. Units step up one block (never two),
drop up to three, wade through shallow water, fall when the ground under them is dug away, push each
other apart so crowds spread out, and stop when they arrive or the crowd at the goal has settled. Unit
types (worker, spearman, archer, rider, sapper, mage, healer, and the Settlers' forms they evolve into: see
[Evolution](docs/rules.md#evolution)) come from `data/units.json` (see [Game data](#game-data)).
Godot draws them with one MultiMesh per model; the placeholder models are built from voxels an eighth
of a terrain block across, tinted in the owner's colour. Each player starts with a town hall at their
start area and four of every unit type that is trained in front of it (except caravans), a wolf lair
lies on the way to the map centre (see [Monsters and taming](docs/rules.md#monsters-and-taming)) and each city-state has a town (see
[City-states](docs/rules.md#city-states)); `--goods=N` gives every town hall N more of every good (and its player N more mana), and `--houses=N` puts N houses
behind yours, since the start has more people than room; `--raid=N` sends N enemy sappers at the one of
those nearest your start (to try out rebuild orders, see [Automation](docs/rules.md#automation)). `--great-work` puts your faction's great work, finished, in the deep caves' chamber nearest your start, its clock running (see [Ascendancy and great works](docs/rules.md#ascendancy-and-great-works)). `--units=N` instead spawns N of your units
around the camera focus (all on one side, so they walk rather than fight), and `--battle=N` sets two
armies of N soldiers each facing each other at the camera focus, with an enemy house nearby (both sides
have 500 mana, so the mages can cast).
`--player=1` plays the Orcs at the east start instead of the Settlers at the west one, `--ai` has
the computer play the other players (see [Winning and computer players](docs/rules.md#winning-and-computer-players)), at `--ai-level=easy`, `normal` (the default), `hard` or `classic`, and `--fog` turns fog of war on
(see [Fog of war](docs/rules.md#fog-of-war)). `--dominion` plays with dominion, winning by holding most of the old
ruins (see [Ancient sites and dominion](docs/rules.md#ancient-sites-and-dominion)); the match runner takes `--dominion` with `--ai` too. `--awakening` plays with the dark power waking (see
[The dark power](docs/rules.md#the-dark-power-the-awakening)), and `--awaken=N` starts it at stage N; the match
runner takes `--awakening` with `--ai`. `--daynight` plays with day, night and weather, and
`--time-of-day=H` does too, starting at hour H (8 morning, 13 noon, 19.3 dusk, 0 midnight);
`--weather=W` makes the first day's weather W (clear, cloudy, rain, storm or fog), `--lights=N` puts N
torches and a campfire around your town hall, and `--focus=swamp` or `--focus=lava` looks at the swamp
or the first pool of lava (see [Day, night and weather](docs/rules.md#day-night-and-weather)).
Generated maps grow their trees twice the size of the first ones (`RegionGenerator.DefaultTreeScale`), a
trunk two blocks square; `--trees=1` grows the first, smaller trees and `--trees=3` bigger ones still, and
`scripts/ui-bigtree-screenshots.sh` shows the same views at each size. Match logs keep the size ("trees N");
saved maps don't need to. These options start a match at once; started without options, the game shows the start screen instead (see [Start screen, match end and replays](docs/rules.md#start-screen-match-end-and-replays)).

Pathfinding (`src/Sim/Units/WalkGraph.cs`, `FlowField.cs`): the walk graph keeps up to four standing
levels per block column (surface, tunnels, caves, bridges) and the steps between them, following the
same rules units move by, and rebuilds only the columns around changed blocks. Every unit ordered to
the same spot shares one flow field: path distances to the goal, computed outward from it with a fixed
budget per tick (12,000 nodes) and headed toward the units, so a field covers the way there rather
than the whole map. When blocks change under a field it is marked dirty: units that find their way
gone get it recomputed within half a second, and otherwise it is refreshed after five seconds, which
picks up new shortcuts such as a freshly dug tunnel. Units with no way to the goal (a wall across the
map, a pit they fell into) give up at once instead of walking into the wall. A move order may name
the level to go to (`MoveCommand.Y`), so units can be sent into a tunnel under a hill rather than
onto it.

### Game data

Factions, techs, buildings, units, the combat role table and the city-states are data, in six JSON files under `data/`
(comments and trailing commas allowed; each file describes its fields at the top). They are built into the simulation core,
so the game, the tests and the match runner all play with the same content; `--data=DIR` makes the game
load a folder of edited copies instead. `ContentLoader` checks everything before the game starts and
reports every problem at once, one line each: misspelt fields, missing or out-of-range values,
duplicate ids, references to ids that do not exist, prerequisite cycles (with the loop spelled out),
and anything that would need another faction's tech. Command logs record a hash of the data, so a
replay made with different data says so instead of drifting apart.

- **Factions**: Settlers and Orcs (who tame monsters: see [Monsters and taming](docs/rules.md#monsters-and-taming)). Player 0 plays the first
  faction, player 1 the second, and so on; picking a faction on the start screen picks the player who
  has it.
- **Techs** (`ResearchCommand`): researched at your town hall, one at a time, paid for when started. 18
  in all: a shared core (farming, better tools, roads, archery, basic magic, masonry, crop rotation,
  horse breeding, handcarts, smelting, sapping), two per faction, and three discovery techs (`"discovery"`:
  Wardens' charts, Beast lore, Trail maps) opened only by exploring (see
  [Discovery techs](docs/rules.md#discovery-techs)). Prerequisites form a branching
  tree up to four deep. A tech unlocks the units and buildings that name it in `requires`, and can have
  effects: faster gathering, bigger loads, richer farms, more housing, more hit points (for units
  already alive too), faster walking, roads, showing the map's ruins, lairs or towns through the fog
  (`reveal`), and wild monsters sparing workers (`spare_workers`).
  Design principle for new techs: **research gives tools, not numbers**. A tech should give players a new
  ability, unit option, building or tactic to outsmart enemies and adapt, not just a higher number; stat
  buffs are mostly avoided. The stat effects above are to be replaced (see issue #80, on hold).
- **Roles** (`roles.json`): the design doc's role table, saying which roles each role is strong and weak
  against and how much less damage the others deal to buildings, and how fast units out of the fight
  heal (see [Combat](docs/rules.md#combat)).
- **Buildings and units** name the techs they need; the barracks trains spearmen at once, and archers,
  riders, sappers, mages and healers after their techs; the siege workshop trains catapults after Smelting (`minRange`,
  `lobs` and `machine`, see [Combat](docs/rules.md#combat)). `units.json` also holds the forms units evolve into and the
  monsters, with each monster's `prizes` per faction for clearing its lair (`relics.json` has the relics units
  take up as prizes, see [Monsters](docs/rules.md#monsters-and-taming)), and `factions.json` how a faction's units earn experience (see [Evolution](docs/rules.md#evolution)), whether it
  tames monsters, and how a computer player plays it (`aiStyle`: `waves` or `balanced`, see
  [Winning and computer players](docs/rules.md#winning-and-computer-players)).
- **Walls, gates and towers** in `buildings.json`: walls and gates come in tiers, where `"upgrade"` names
  the building one is turned into where it stands (so only the first of a chain is ever laid) and
  `"facing"` the building that goes up on each free side of an upgrade when it is done (a stone wall is
  three columns thick). `"sturdy"` multiplies the damage each of its blocks takes, `"thick": true` marks a
  thick, solid one (close-up blows do `"thick"` in `roles.json` times less, and a unit with `"blast": true` in
  `units.json` topples it, see [Combat](docs/rules.md#combat): thick walls) and `"walkway": true`
  lets ranged units be posted on top. A building of `"kind": "tower"` is solid to its top, is put up alone
  or onto the player's own walls, and gives the units posted on it `"aim"` (the share in a hundred of
  their would-be misses that land, and a fifth of it more shots to the head). The materials
  `"wall": "palisade"` (a barrier's stakes), `"rampart"` (pale stone with battlements) and `"trestle"` (a
  watchtower's open frame) are theirs (see [Combat](docs/rules.md#combat): wall tiers, facings, towers).
- **City-states** (`citystates.json`): each town's totem (a building of kind `totem` in `buildings.json`)
  and how soon it stands again, its guards, the tribute its holder gets, what its caravans trade, its
  mercenaries (units whose `hiredFrom` names it), its lair monster and what its huts are built of (see
  [City-states](docs/rules.md#city-states)).

The tech tree screen (K) shows your faction's techs in columns by how many techs lead to them, with
lines from each tech to what it opens up: green when researched, gold while being researched, bright
when you can start it now and dim otherwise. A discovery tech not yet discovered says what to do for it
("Search an old ruin"). All the numbers are first guesses for playtesting.

### Headless matches and replays

`tools/MatchRunner` runs a match with no Godot at full speed (a 10-minute match with 200 units takes
about 20 seconds, mostly pathfinding). It either generates a match with scripted players from a seed
or replays a command log, records the state hash every N ticks, and fails if repeated runs differ.
Command logs are plain text and double as replays:

```bash
dotnet run -c Release --project tools/MatchRunner -- --minutes=10 --record=match.log   # generate + check
dotnet run -c Release --project tools/MatchRunner -- --units=500 --minutes=5           # bigger armies
dotnet run -c Release --project tools/MatchRunner -- --log=match.log --runs=3          # replay a log
```

The replays the game saves are command logs too: `--log=FILE` plays one back, checks it replays the
same, and prints who won and when.

Scripted players dig and build near their home and, once a second, send a slice of their army (100
units each by default, `--units=N`) to their start or their dig site, attack-move it to the map centre
or the enemy start, set it on the nearest enemy or an enemy building, or have its sappers tunnel.
Every two seconds they also make an economic move from their town hall: gather, build, farm or smelt,
research a tech, train a worker or soldier, lay a road, raid an enemy storehouse, or change their
automation (automatic workers, repeat, stances or the form a unit type takes on its own); when one of
their units can evolve, half the time they evolve every unit of its type that can instead. Now and then
they send a slice to the nearest wolf lair, and half the time they ride or recruit the monsters they beat.
They also send their raiders at the city-states' totems, send idle caravans to trade with them, and hire
mercenaries where they hold the town.
CI runs `scripts/determinism.sh` on every PR and push: it replays the match twice in one process and
again from the saved log in a second process, and uploads the log and hash traces.

### Scripted input

`--input="STEPS"` (or `--input-file=PATH`) drives the game with scripted mouse and keyboard input:
steps such as `drag 500,300 900,600`, `click 1150,330 right`, `key space`, `key pagedown*14`,
`camera X,Z YAW PITCH DISTANCE`, `shot PATH` and `print`, separated by `;` or new lines (the full list is
in `game/scripts/InputScript.cs`; `clickworld X,Y,Z` clicks a block wherever the camera shows it, and
`clickui NAME` clicks a HUD button by name, such as `tech_roads` or `train_worker`; `dragworld` drags
between two blocks; `waitmatch` waits for a match started from the start screen).
`scripts/ui-screenshots.sh` uses it to box-select the starting units, order them to move, pause, and
cut a hill with the height slice; `scripts/ui-economy-screenshots.sh` sends the workers to dig the
stone outcrop, researches roads from the tech tree, places and builds a house, queues a worker at the
town hall and lays a road; `scripts/ui-shrine-screenshots.sh` starts with `--goods=100`, researches Basic
magic, puts up a shrine beside the town hall and has a worker keep it, so the mana in the top bar rises as it
channels the crystal; `scripts/ui-sawmill-screenshots.sh` puts up a sawmill beside the town hall and has a worker
work it, so planks show in the top bar; `scripts/ui-mason-screenshots.sh` researches Better tools and Masonry, puts up a
mason's yard beside the town hall and has a worker work it, so the cut stone in the top bar rises; `scripts/ui-siege-screenshots.sh` (test mode) puts up a siege workshop, trains a catapult there and sends catapults at an Orc barracks until it falls; `scripts/ui-combat-screenshots.sh` starts with `--battle=16` at the map
centre, sends a sapper to tunnel under the enemy house and attack-moves the rest of the army through
the enemy; `scripts/ui-automation-screenshots.sh` makes the starting workers automatic and the soldiers
hold their ground, puts the town hall on repeat, then makes every worker automatic and waits for them
to put up a house on their own; `scripts/ui-evolution-screenshots.sh` starts with `--xp=150`, turns the
spearmen into knights with the evolve button, sets the archers to become longbowmen on their own, evolves
the rest, and selects one knight; `scripts/ui-monsters-screenshots.sh` plays the Orcs (`--player=1`), whose
starting units attack-move into the nearest wolf lair and beat the pack, then picks a beaten wolf with
the Beaten button for a spearman to ride and the next to recruit, and plays the Settlers, who clear
their lair for its reward; `scripts/ui-citystate-screenshots.sh` starts with `--goods=1000 --houses=4`,
picks the goblin town from the top bar, clears the wolf lair on the road and breaks the goblins' totem
with the starting units, hires a goblin of each kind, and trains a caravan and right-clicks the town with
it, then waits for the trade and tribute to come in, and last picks the ogres' hold, then the kobolds' warren (in the deep caves, through the height slice) then the troll bridge, then the centaur camp and then the merfolk cove from the top bar; `scripts/ui-match-screenshots.sh` starts with `--menu`, picks the
Orcs on the start screen with the computer playing the Settlers, surrenders, checks the Defeat panel
and that the match runner plays the saved replay back to the same end, goes back to the start screen,
and then, from the command line with `--units=40`, brings down the Orcs' town hall for a Victory;
`scripts/ui-fog-screenshots.sh` switches fog of war off and on again on the start screen and starts a
`--battle=16` match without a computer player, checks that the enemy army out of sight is not drawn,
attack-moves until it is, and checks that without fog of war every enemy unit shows. `scripts/ui-alerts-screenshots.sh`
selects one idle worker after another with the Idle workers button, builds a house and clicks its
alert from far away to bring the camera back, then attack-moves a `--battle=16` army into the enemy's
and clicks the under attack alert. `scripts/ui-test-mode-screenshots.sh` starts with `--test-mode` and uses the test panel
to put down trees, a pond, lava, wolves, the Orcs' spearmen and barracks, a wolf lair and blood, turns on
invulnerability, sets dusk and rain, saves the scenario and loads it back (`select NAME ITEM` picks from an
option list, `spin NAME V` sets a number box). Each checks the
printed UI state and saves screenshots. CI runs each of them as its own job, in parallel, on every PR
and uploads the PNGs as the `ui-screenshots-...` artifact, one folder per script. A new `scripts/ui-*.sh`
is picked up by CI automatically; put a `# CI: skip` line in its header to leave it out.

### Benchmark

`--benchmark[=SECONDS]` (default 10) loads the full 512×64×512 region, digs 100 blocks per second
near the camera while it slowly orbits, then prints one `BENCHMARK ...` line with load time, FPS,
frame-time percentiles, frames over 16.7 ms and main-thread CPU work per frame, and quits. The line
is also appended to `user://benchmark.txt` (on Windows `%APPDATA%\Godot\app_userdata\RTS\benchmark.txt`;
change with `--benchmark-out=PATH`). On a Windows build run `RTS.exe -- --benchmark`.
Add `--units=1000` to also spawn 1,000 of your units around the camera; every 8 seconds they are all
sent to one new point near the camera, so they keep walking around the holes being dug
(`RTS.exe -- --benchmark --units=1000`).
The cloud dev container only has a software renderer, so its FPS numbers are not representative
(`clock_avg_ms` is the time a frame really took there; the engine's own figure is rounded to whole screen
refreshes). The line also says how the world was drawn: `lod_chunks` and `lod_tris` are the chunks and
triangles at each level of detail (half-size ground / full detail / bigger voxels / plain blocks),
`mesh_mb` the vertex data on screen, `fine_world_mb` the half-size blocks' storage, and `fine_upkeep_ms`
the time spent keeping them right as blocks changed (`fine_upkeep_columns` block columns of steps worked
out again; the benchmark digs a hundred blocks a second, far more than a match does).

### Levels of detail and half-size ground

`ChunkRenderer` draws each chunk as finely as its distance from the camera calls for (`ChunkLod`), and
re-meshes a chunk on a worker thread when the camera crosses a line (with a little slack, so nothing
flickers on it). A chunk whose blocks changed goes ahead of those that only change level, on the workers
and onto the screen, so what was dug or fell shows at once however fast the camera is moving:

- **Half-size ground** up to 90 m: see below.
- **Full detail** up to 100 m: whole blocks, trees and buildings in fine voxels.
- **Bigger voxels** from 100 m: trees, buildings and rubble in voxels four times the size, two to a block
  (`StructureMesher.Shrink`): the same shapes for well under half the triangles. Trees and buildings are
  seven eighths of the world's triangles; the ground is the other eighth.
- **Plain blocks** from 250 m: everything as whole blocks with no shading in the corners, a seventh of
  the triangles. Crowns get the colour their leaves have on average (`ChunkMesher.HorizonColor`).

`--lod=off` draws everything as finely as the nearest; `--lod-reach=A,B,C` sets the three distances.

The ground is drawn in **half-size blocks** (0.25 m) within 90 m of the camera, with every step of open
natural ground split in two (`Rts.Sim.World.FineTerrain`): the top half of a ground block goes wherever
the ground is nearer to lower ground than to higher ground, so a hillside climbs in quarter-metre steps
and the edge of a plateau gets a shoulder. It is a look only. The rules still run on whole blocks: units,
buildings, digging, pathfinding, maps and replays are unchanged, and what stands or lies on a lowered step
(units, their bars and icons, blood, order markers, the rally flag, road, dig and formation marks) is
drawn a quarter of a metre lower; a ring lies as high as the highest ground of its level under it, so the
whole step beside a lowered one does not hide half of it. The finer world is a second `VoxelWorld` of 1024x128x1024 blocks made
at load (53 MB against 8 MB); trees and buildings are drawn from the whole blocks as before.
`--terrain=blocks` draws whole blocks everywhere instead.

As the world changes, `Rts.Sim.World.FineWorld` keeps the finer world what it would be if it were made
afresh: the ground round a new pit gets its shoulder, a step that is built on is filled in, and it comes
back when the building or rubble on it is gone. A change to the ground's height has the steps within
eight blocks of it worked out again before the next frame (600 block columns a frame at most, so a great
collapse settles over a few frames); any other change only those beside it.

The mouse is aimed at what is drawn (`VoxelRay`): its ray is marched through the half-size blocks where
they are on screen, so a click on a lowered step lands on that block and not on the one behind it, and
the terrain tool's right click on one puts its stone on top of it. Scripted clicks (`clickworld`) aim at
the top of a block as it is drawn.

Falling ground (`FallingRenderer`) looks as it did standing: a piece is drawn with the ground's own look
(`TerrainMaterials.Piece`: the shade, grain and block lines it had, carried along as it tips), in
half-size blocks with its lowered steps still lowered, and half-size chunks crumble off it as it falls
and fly out where it lands. That is for show: the rules' pieces are whole blocks, and where the rubble
lands is theirs to say.

Known gaps: a plan's ghost stands where the building will (a quarter of a metre over a lowered step,
which is filled in when it is built), a piece that breaks apart in the air is whole blocks again, and the
ground steps up a quarter of a metre where half-size blocks give way to whole ones (90 m off).

To run any script that starts the game with other flags, pass them in `RTS_ARGS` (space-separated; the
command line wins): `RTS_ARGS=--terrain=blocks scripts/ui-dig-screenshots.sh`.

### Detail prototype

A prototype of finer buildings and units, to see how close the game can get to the moodboard's
diorama look on a full-size map (`game/scripts/Detail/`). Everything is drawn from voxels a sixteenth of
a block across (3.1 cm, half the size of today's unit voxels), greedy-meshed into rectangles of one
material; stones, planks, shingles and a shade per voxel are drawn by the shader
(`DetailShader`), so they cost no triangles.

- **Units** (`DetailedUnits`): the Settlers' worker, spearman and archer have detailed versions in six
  parts (legs, body, head, arms) that swing as they walk, and thrust in time with their attacks (from
  the attack cooldown). Each part is a MultiMesh, so any crowd is 18 draw calls; only units within
  `--detail-distance` (40 m) of the camera are drawn this way, the rest with their usual model. On by
  default; `--detail=off` or F7 turns them off.
- **Sculpted units** (`Sculpt`, `Sculpted`): the Orcs' spearman and worker, the wolf, the wolf rider, and a
  stone golem with an orc on its back are modelled in shapes (balls, tapered limbs, boxes with bevelled
  edges, armour plates with a raised rim and rivets, straps) in units of a thirty-second of a block
  (1.6 cm), then weathered by passes over the surface (patches of colour, chipped stone, moss on what faces
  up). Being shapes rather than hand-placed voxels, each is built at three voxel sizes, its levels of
  detail: 1.6 cm within `--detail-near` (16 m) of the camera, 3.1 cm out to `--detail-distance` (40 m), and
  beyond that the usual plain model (the golem, which has none, at 6.25 cm). The finest level casts its
  shadow from the middle one. `--detail-fine=off` stops at the middle level, to measure what the finest
  costs. Faces in nooks are shaded darker (ambient occlusion baked into the mesh).
- **Showpieces**: `--showcase[=X,Z]` stands a golem, an orc spearman and worker, two wolves, a wolf rider and
  two knights together (player 1's colour); `--golems=N` puts N golems in rows near the camera focus, for the benchmark.
  They are scenery, not units of the rules. `scripts/ui-sculpt-screenshots.sh` shoots the showcase from
  far to close, with detail on and off.
- **Imported models**: a textured GLB (from Meshy AI, say) becomes a voxel model with
  `python3 scripts/import-model.py MODEL.glb HEIGHT game/models/NAME.vox.gz` (HEIGHT in sculpt units, an orc
  is about 62; needs numpy and Pillow). Its colours come from the texture, brightened a little since the
  texture has its shadows painted in. Add it as a rig with `ImportedRig("res://models/NAME.vox.gz")` in
  `Sculpted.cs`; it gets the same levels of detail but stands still (one part, no limbs). The knight
  (`game/models/knight.vox.gz`) was made this way, and so were the Settlers' worker, militia, spearman, pathfinder,
  archer, rider (imported at 84) and mage (the Orcs keep their own sculpted or plain versions).
- **Castle** (`DetailedKeep`): `--keeps=N` puts N stone castles (curtain wall, round towers, gatehouse,
  keep, banners in your colour) on flat ground round the camera focus. They are scenery, not buildings of
  the rules: units walk through them. Past 60 m each is drawn with a mesh a quarter as fine
  (`--keep-lod=off` turns that off).
- The benchmark line adds `avg_tris`, `avg_draws`, `detailed_units`, `unit_draw_ms` (time to place every
  unit, plain and detailed, per frame), `fine_units` and the triangle counts of the detailed models (per
  level, finest first), so
  `RTS.exe -- --benchmark --units=2000 --detail=off` and the same with `--detail=on` (or `--keeps=30`)
  compare the cost on real hardware. Detailed units only show near the camera, so bring it in:
  `--player=1 --cam-distance=10 --cam-pitch=35` (Orcs, so the crowd has sculpted units), with
  `--detail=off`, `--detail-fine=off` and nothing, and `--golems=8`.

To open in the editor, open `game/project.godot` with Godot 4.7.2 (.NET edition).
