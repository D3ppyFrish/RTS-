# Playing the game

How to get a build, the controls and what the HUD shows. How each system works is in
[rules.md](rules.md).

## Getting a build

Every pull request and every push to `develop` produces a Windows build. Open the run on the
**Actions** tab, download the `RTS-windows-...` artifact, unzip it and run `RTS.exe`.

It opens on the start screen: pick your faction, the Settlers (who start in the west), the Orcs (in the
east) or the Deepfolk (an insect hive that hatches swarms in batches at brood chambers and burrows under walls;
see [The Deepfolk](rules.md#the-deepfolk)) or the Elves (forest guardians with long-range archers, healing druids and
living walls that grow back; see [The Elves](rules.md#the-elves)) or the Dwarves (sturdy, iron-clad soldiers who see in
the dark, stone walls from the first day, and the Book of Grudges; see [The Dwarves](rules.md#the-dwarves)), and under Against the faction of the other side (the same as yours makes a mirror match), the
world seed, the map size (Standard, 768 by 768 blocks, or Large, 1024 by 1024: nearly twice the land, the towns
about 700 blocks apart instead of 520, and the land beyond the standard map's twice as thick with monster lairs; see
[Monsters and taming](rules.md#monsters-and-taming)), whether the computer plays the other side and how well (Computer level: Easy, Normal, which
is the default, Hard, or Classic, the first computer player; hover a level to read how it plays. No level
is given anything you are not, and only Classic sees through the fog of war), and whether there is fog of
war, then Start match. The computer plays the two factions differently: the Orcs mass their soldiers and
come in big waves from an open town; the Settlers (at Normal and Hard) wall their town and make the wall
stronger as the match goes on, and at Hard post their archers on it and keep a quarter of their soldiers
at home behind it when the rest attack.
Each faction has its own look for the menus, panels and buttons: castle stone for the Settlers, rough
timber for the Orcs and the Elves, dark slate for the Deepfolk, castle stone for the Dwarves too. The start screen changes to the look of the faction you pick. Destroy the other side's
town hall to win, or (unless switched off on the start screen) hold more than half of the old ruins, the
ancient sites, for 3 minutes: soldiers standing alone in one for 15 seconds take it, and the sites button in the
top bar says how many you hold and counts down when someone holds most (see
[Ancient sites and dominion](rules.md#ancient-sites-and-dominion)). A faction's final tech opens its great work, which
must be built in the deep caves; once it is finished, keep it standing 5 minutes and you win, and the crown button in
the top bar counts down (see [Ascendancy and great works](rules.md#ascendancy-and-great-works)). Unless switched off on
the start screen, the dark power under the Heartscar, at the heart of the land, wakes 45 minutes in (sooner if you dig
near it): corrupted ground spreads from it and hurts your units on it, and broods of corrupted beasts come for your
town hall every few minutes. The dark power button in the top bar counts down to it and to each next stage; click it
to look at the Heartscar (see [The dark power](rules.md#the-dark-power-the-awakening)). The Surrender button under the right end of the top bar gives up. When the match is
over for you, the clock stops, a panel says Victory or Defeat and how, and the match is saved as a
replay (in `%APPDATA%\Godot\app_userdata\RTS\replays`; the panel has a button that opens the folder).
New match goes back to the start screen. See [Start screen, match end and replays](rules.md#start-screen-match-end-and-replays).

Tick **Test mode** on the start screen to try things out: a test panel puts any unit, building, tree,
block (water and lava too), monster lair or blood where you click, heals, kills or wounds the selected
units, sets the weather and time of day, hands out goods and techs, turns on cheats (instant building,
training and research, invulnerability), steps one tick at a time (F2) and saves and loads scenarios. F1
hides the panel. See [Test mode](rules.md#test-mode).

Controls:

| Input | What it does |
| --- | --- |
| WASD / arrows, Q/E, R/F | pan, rotate, tilt the camera |
| Middle mouse drag (Shift to pan), wheel | orbit, zoom |
| Left click, left drag | select a unit or building, box-select your units (Shift adds, double-click picks that type on screen) |
| Right click | depends on what is under the cursor: units attack an enemy unit or building (an enemy storehouse they capture instead); workers gather a tree (or a felled tree's rubble), berry bush, stone or ore, build a site or start a plan, repair a damaged building, wall, gate or tower of yours (every faction, no tech needed; they go on along a wall; the building's panel says it is damaged or being repaired), take down a building of yours marked to take down (Craft guilds, Settlers only: the **Take down** button on its panel marks it), mend a damaged catapult or battering ram of yours where it stands (Craft guilds, Settlers only), or work a farm, smelter, quarry or mine, go on supply duty at a storehouse or town hall of yours that has supply rules (carts too, and they do nothing else), or clean up blood on the ground (the stains within 8 columns, nearest first); caravans trade with a city-state's town; on an old ruin nobody has searched yet, the selected units walk in and search it (see [Old ruins](rules.md#old-ruins-and-what-they-hold)); otherwise move there |
| Ctrl+right click | attack-move: walk there and fight whatever is met on the way; on an enemy storehouse, burn it |
| Alt+right click | patrol: the selected soldiers walk there and back again and again, fighting whatever they meet, until given another order (units that cannot fight just walk there); the selection panel says "patrolling" |
| Alt+right click on one of your units or buildings | guard it: the selected units that can fight keep near it, follow it, and fight enemies that come near it or hit it, until given another order or it is gone; the selection panel says "guarding" |
| Right click, with only your town hall or barracks selected | set its rally point: a flag shows where the units it trains walk to when they step out (right click the building itself to clear it) |
| H, G, C, B, M, N | place a house, storehouse, farm, barracks, smelter or town hall; a see-through model of it follows the cursor, amber where it will be built, cyan or violet where it will be a plan, red where it does not fit. The selected workers build it (Shift+click places more; right click or Esc stops). Something you cannot afford yet is placed as a plan |
| Quarry, Mine, Fishing hut, Forester's lodge and Hunter's lodge buttons | place a quarry, a mine, a fishing hut, a forester's lodge or a hunter's lodge the same way (no key: from the build panel only). A fishing hut must stand right beside open water: it shows red elsewhere. Right click it with workers to work it; foresters plant saplings round their lodge, and the panel says how many are growing; hunters shoot the deer within reach of theirs, and the panel says how many deer are in reach |
| Siege workshop button | place a siege workshop (40 wood, 40 planks and 20 stone, needs Smelting; no key: from the build panel only). Select it to train catapults (60 planks, 10 iron, 40 food) with its Train button or U. A catapult throws stones over walls from up to 15 blocks off and knocks down walls and towers, but cannot hit anything within 5 blocks of it: keep soldiers in front of it. It also trains battering rams (40 planks, 40 wood, 10 iron, 30 food): right click an enemy wall, gate or building with rams selected to send them at it; they never attack soldiers, shrug off arrows and break gates fast |
| Tab | plan mode (also the Plan mode button at the top of the Build panel): what you place is a plan, marked out but not paid for or built. Plans show as a see-through model: violet while you cannot pay for it, cyan (pulsing) once you can. Right click a plan, with workers selected or not, to pay and start building it; select it for Start building and Remove plan (Delete) buttons |
| O | place a gate (a barrier gate, 3 wood): a wall column whose bottom is a gate only your units walk through (draw a wall through it, or up to it) |
| I, ; | place a torch (1 wood) or a campfire (3 wood): from dusk to dawn they light the ground around them, so what stands there is seen and shot at as by day; a storm puts them out until it passes |
| Y | wall tool (also the Wall button): drag a line for the selected workers to wall off, a column at a time, with wooden barriers at 1 wood a column (the first tier of wall: cheap, low and easy to knock down); columns with no room or that you cannot pay for show red; Shift keeps the tool for another stretch; right click or Esc stops |
| Upgrade button, U | with a wall or gate of yours selected: upgrade the stretch of that tier it stands in (every wall and gate joined to it, nearest first, as far as your goods go) to the next tier; Only this one (Shift+U) upgrades that column alone. A barrier becomes a wooden wall (4 high, 3 wood a column) that archers stand on, and that a stone wall (6 high, 10 stone, needs Masonry), much sturdier and faced on both sides once it is done, so it is three columns thick with battlements along both edges; gates go the same way, and a stone gate is a gatehouse as deep as the wall. Workers do the work (automatic ones by themselves, or right click the wall with workers), and the wall stands as it is until they are done. On a stone wall that has lost some of its facing the button reads Put the facing back (3 stone a column) |
| Upgrade button; U, or Ctrl+U where U trains | with a house, town hall, storehouse, barracks, smelter or farm of yours selected: upgrade it to a bigger, better building of its kind (large house, then manor; great hall, then castle keep; warehouse; great barracks; foundry; large farm). Its tooltip says what the new one does better. It grows from its middle onto the free, level ground round it, so leave room: the button reads "no room round it to grow" when there is none. Workers do the work while the building keeps working as it is. Plain U trains at a town hall or barracks, so there it is Ctrl+U |
| /, \ | place a watchtower (30 wood; 3 by 3, 7 high) or a stone tower (60 cut stone and 20 wood, needs Masonry; cut stone comes from a mason's yard; 5 by 5, 9 high). On open ground it stands alone. With the cursor on a finished wall of yours, or right beside one, it snaps onto the wall, its middle on the line: the wall stands until the tower is done and is then part of it. A stone tower can be put over a watchtower the same way. A tower sees 14 or 18 columns around it, and archers posted on it aim better (below) |
| Field hospital button | place a field hospital (40 wood and 20 food; 4 by 4; needs Field hospital, after Field dressings; no key: from the build panel only). Your units within 12 columns of it heal as at home: 2% of their hit points a second once 10 seconds out of the fight, and their wounds stop bleeding. Put one by the front |
| Listening post button | place a listening post (20 wood and 10 stone; 2 by 2; no key: from the build panel only). With fog of war on, units underground (in a tunnel or a cave, with rock over their heads) are not seen from the surface; a listening post shows you other players' units underground within 20 columns of it. Without one you see them only within 2 columns of one of your units, or within the sight of one of your units that is underground too |
| Right click a wall or tower of yours | with archers, mages or other ranged units selected, on a wooden or stone wall or gate, or a tower: they climb up and take posts along the edge of its top, one to a column, the nearest free columns to the one clicked (a stone wall holds a row on each side, a watchtower 8 and a stone tower 16). Up there they see and shoot a quarter further, lean out to shoot what stands at the foot of the wall, are out of reach of blows from the ground, and hold their ground; order them anywhere to bring them down. On a tower they also aim better: half (watchtower) or three in four (stone tower) of the shots they would miss in the dark or the rain land, and they hit the head more often |
| ' | bridge tool (also the Bridge button): drag from bank to bank for the selected workers to lay a level plank deck across deep water, a drop or a trench, 3 wood a column; columns with no room or that you cannot pay for show red, and nothing shows while the ends are not ground about level with each other; Shift keeps the tool; right click or Esc stops. Anyone walks over a bridge; Ctrl+right click one with soldiers selected (yours too) to knock that column out (see [Combat](rules.md#combat)) |
| Shift+X | ramp tool (also the Ramp button): with workers or sappers selected, drag from the foot of a cliff, a steep hill or a wall to its face, and they heap a ramp of earth up to its top, a level a column, 1 stone a block as it goes in. Green squares show where it goes; red ones have no room, and nothing goes up unless it reaches the top (2 to 10 levels up, about a column for each level, at most 16). Units walk up it; from a wall they climb down the far side when sent on. The enemy can use it too, and anyone can dig it away (see [Combat](rules.md#combat)) |
| Shift+K | ladder tool (also the Ladder button): with workers or sappers selected, drag from the foot of a wall, a tower or a cliff onto its face, and they raise a siege ladder against it, a rung at a time, 2 wood a rung. Two squares show its foot and the top it reaches, green if it goes up, red if not (something 2 to 10 levels up right beside the foot, nothing over the foot, wood enough). Units at its foot climb it onto the top, and from a wall they climb down the far side when sent on. Anyone up there can climb down it, and an enemy on the top beside it throws it down after 2 seconds unless your units hold the top too (see [Combat](rules.md#combat)) |
| Shift+O | dam tool (also the Dam button): with workers or sappers selected, drag from bank to bank across water or a dug channel, and they build a dam of rammed earth from the bed up to the level of the higher bank, 1 stone a block as it goes in. Green squares show where it goes; red ones have no room, and nothing goes in unless the whole dam fits (at most 32 columns and 12 deep). It holds the water back, and units walk across on top; the enemy too, and anyone can dig it away and let the water through (see [Combat](rules.md#combat)) |
| Sluice button | with units of yours selected within 24 columns of a finished dam of yours: Open the dam's sluice takes the boards out of its deepest column (all but the top one, so the way across stays), and the water pours through into lower ground on the far side; Shut the dam's sluice puts them back (not while anyone is in the gap) |
| Right click your ship | with units selected: they walk to the shore beside it and step aboard, 10 at most (see [Ships and docks](rules.md#ships-and-docks)). With a ship selected, a right click on water sails there and one on land sails to the shore by it and puts everyone aboard ashore |
| Put ashore button | with ships of yours selected (it shows how many are aboard): they sail to the nearest land and put everyone aboard ashore |
| Go fishing button | with fishing boats of yours selected (it shows how many are fishing): they sail out to open water, fish and bring the catch home to their nearest dock, again and again (see [Ships and docks](rules.md#ships-and-docks)). Right-click one of your docks with them selected to fish from that one |
| L | lay a road: click where it starts, then where it ends (Shift carries on from there) |
| U | train at the selected building: a worker at a town hall, a spearman at a barracks (its panel has a button for each unit it trains) |
| P | repeat at the selected building: each unit it finishes is queued again once there are goods for it and a minute's food to spare |
| Shift+P | rebuild orders on or off (also the top bar's Rebuild button): your destroyed buildings are cleared and put back where they stood by your automatic workers, once no enemy is near and you can pay; a pale ghost marks each one waiting (see [Automation](rules.md#automation)) |
| Shift+U | with your town hall selected: its governor steps off, growth, army, defence (also the Governor buttons in its panel). The governor runs the town for you: idle workers there go automatic, houses, farms and the buildings of its focus go up, workers and soldiers are trained and techs researched (see [Automation](rules.md#automation)) |
| J, Shift+J | make the selected workers automatic, or stop; Shift+J does it for all your workers, and those trained later (see [Automation](rules.md#automation)) |
| Ctrl+J | work priorities menu (also the top bar's Priorities button, and Work priorities in a worker's panel): on the left pick the default list, the selected workers or any one worker; on the right move each kind of work up or down, take it off, or add one back. The default list is for new workers and every worker without a list of its own; "Use the default list" drops a worker's own list, "Put every worker on it" does that for all of them (see [Work priorities](rules.md#automation)) |
| V | stance of the selected soldiers, in turn: aggressive, hold their ground, retreat when hurt |
| Ctrl+M | the selected hive units that are badly hurt molt (also the Molt button; Deepfolk, needs Molting): each stands still and heals back to full over 15 seconds, then stays soft for 30 (no armour, hit half as hard again, slower) (see [Combat](rules.md#combat)) |
| Ctrl+C | the selected war drummers beat the charge (also the Beat the charge button; Orcs, needs War drums): for 6 seconds your units within 14 columns of a drummer run 40% faster and their first blow lands half as hard again, then the drum needs a minute; rivals within 40 columns see a drumming drummer through the fog (see [Combat](rules.md#combat)) |
| Ctrl+S | with your beast pen selected (Orcs, needs Beast pens): drag a line and the beasts kept in it burst out as a stampede along it, trampling and knocking down the enemies in their way and smashing wooden barriers (also the Stampede button; Let the beasts out frees them). Right-click your pen with tamed wolves or boars selected to pen them (see [Beast pens and the stampede](rules.md#beast-pens-and-the-stampede)) |
| Ctrl+G | with your crystal lens selected (needs Crystal lens, a discovery tech): click anywhere on the map and the lens shows 30 columns round that spot through the fog for 20 seconds, for 25 mana; then it needs 3 minutes (also the Gaze button) (see [Discovery techs](rules.md#discovery-techs)) |
| Ctrl+V | the selected line infantry raise a shield wall, or lower it (also the Shield wall button; needs Drilled ranks): they stand still, and arrows and charging riders from the front do little (see [Combat](rules.md#combat)) |
| Ctrl+X | fell tree tool (also the Fell tree button; needs Better tools): with militia selected (Orcs and Deepfolk: workers), press on a tree and drag the way it should fall, then let go. They fell it that way: units under it take heavy damage, buildings are hit, and a trunk landing across a gap or stream of one or two blocks lies there as a log bridge. Squares show where it comes down (see [Economy](rules.md#economy)) |
| Ctrl+Z | sound for ore tool (also the Sound for ore button; needs Better tools): with foremen selected (Orcs and Deepfolk: workers), click a spot. After 8 seconds there every ore, coal, gold and crystal block within 12 blocks, however deep, shows through the ground as a glowing cube in its colour: orange iron ore, dark grey coal, yellow gold, pale blue crystal. A ring of squares shows the reach |
| Shift+V | hand the selected soldiers to the governor of your nearest governed town hall (also the Defend the town button): they become its town guard and defend the whole town, chasing raiders you can see within 24 columns of the hall (see [Automation](rules.md#automation)) |
| Evolve buttons | in the selection panel, for your units with the experience (they have a gold ring under them): a button per form evolves the selected units of that type into it, and Auto picks the form that type takes on its own (see [Evolution](rules.md#evolution)) |
| Beaten, Ride, Recruit | Orcs only: the top bar's Beaten button selects your next beaten monster (it has a gold ring under it); in its panel, Ride has the nearest spearman climb on and become a wolf rider, and Recruit makes it one of your units (see [Monsters and taming](rules.md#monsters-and-taming)). With units selected near a cleared den, the Orcs' panel has a button to make the den a wolf lodge, and the Settlers' a button to make the selected spearman nearest it a Wolfheart (see [Cleared dens](rules.md#cleared-dens)) |
| Town buttons, Hire | the top bar has a button per city-state's town saying who holds it; it (or clicking the town) selects the town, whose panel shows its totem and guards and, for its holder, has a Hire button per mercenary. Right-click the totem with soldiers to break it and take the town; the troll bridge's panel shows its toll, what it took for you and any feud with you, the centaur camp's how far its scouts show its holder the land, and the merfolk cove's how much more its holder's fishing huts catch (see [City-states](rules.md#city-states)) |
| Right-click the dusk tree, `,` | once its warden is gone: battle priests heal the tree and one gets the Dusk Staff (a bearer that dies drops it, or its relic, where it fell: walk a unit over it to pick it up); Orc fighters hack it down instead. With the staff's bearer selected, the Dusk Bloom button or `,` heals every ally near it (see [The dusk tree](rules.md#the-dusk-tree)) |
| Shift+Y | with a mage selected (also the Earth Wall button in the selection panel): drag a line of up to 8 columns and let go, and a ready mage raises a wall of earth along it, walking into range first. Every spell costs mana (its button says how much, and is greyed out while you have too little). Green squares are where earth rises; red ones have a unit, a building or water on them (see [Terrain magic](rules.md#terrain-magic)) |
| Shift+' | with a mage selected (also the Ice Bridge button in the selection panel): drag from bank to bank across water and let go, and a ready mage freezes a level way of ice across, three columns wide and up to 16 long, walking into range first. Green squares are where ice forms; red ones have something in the way. Units on the ice slide: they are slow to get going and to stop. Dark cracks show on it in its last 5 seconds before it melts (see [Terrain magic](rules.md#terrain-magic)) |
| Shift+Z | with a mage selected (also the Chasm button in the selection panel): drag a line of up to 8 columns and let go, and a ready mage opens the ground along it 3 blocks deep, walking into range first. Green squares open; red ones have a building, water or no natural ground under them. Whoever stands there falls in and is stuck until the ground closes, 30 seconds later, and lifts them out. With several mages selected, up to 3 ready ones cast together: each extra one makes it a column wider and 4 longer |
| Shift+L | with a mage selected (also the Grow Forest button in the selection panel): drag a line of up to 8 columns and let go, and a ready mage grows full-grown trees along it, one on every other spot, walking into range first. Green squares are where a tree grows; red ones have a building, water, a unit or something in the way. Trunks stop arrows and block the way, and the trees stay (fell them for wood like any tree). With several mages selected, up to 3 ready ones cast together: each extra one adds a row of trees on the gaps of the first and makes it 4 longer |
| K | open the tech tree: click a tech to research it at your town hall |
| . | select your next idle worker and look at it (as the Idle workers button does) |
| F2 | select all your combat units, leaving workers, caravans and carts out (as the Army button does) |
| Ctrl+1..9, 1..9 | assign a control group, recall it (press twice to centre the camera on it) |
| Space, - and + | pause (orders still work), slower and faster: 0.5x, 1x, 2x, 4x |
| F7 | detailed units on or off: near the camera, Settlers workers, spearmen and archers are drawn finer and swing their arms and legs as they walk and fight (a prototype; see the README) |
| PgDn, PgUp, Home | height slice: hide every layer above a cut to see tunnels and caves; Home turns it off |
| T | terrain tool, for trying things out (not in matches from the start screen): left click digs a block, right click places stone (T or Esc to leave) |
| X | tunnel tool: the selected sappers dig to the block clicked, a level up or down each column; click a building to dig just under it so that part of it falls in (Shift+click digs another; right click or Esc stops) |
| Z | dig tool (also the Dig an area button): drag out an area for the selected workers and sappers to dig out, [ and ] for how many levels deep (below the ground where the drag starts); Shift keeps the tool for another area; right click or Esc stops |

The HUD shows the goods in your storage (crystal, gold, planks and cut stone among them), your mana (the violet drop: what spells cost, channelled from crystal at a shrine, see [Economy](rules.md#economy)) and your population over housing (each beside its picture; hover for its name), food a minute beside the food (the food gathered in the last minute; units eat nothing once trained), the game clock, the tech
being researched, the tech tree, automatic workers and speed buttons, the Idle workers and Army buttons and alerts under the top bar on the left, a button per city-state town under its middle (who holds it; click to look at it), a truce button per rival beside them (offer a truce, accept theirs, take an offer back or break a truce off; see [Truces](rules.md#truces)) and a trade button per rival (offer a trade deal, accept theirs, take an offer back or end the deal; with a deal on, right-click their town hall with caravans selected to trade there; see [Trade deals](rules.md#trade-deals)) (see [Alerts and idle workers](rules.md#alerts-and-idle-workers)), build buttons on the left (a building you lack the
tech for says which tech it needs), the height slice slider and what is selected: a unit's hit points,
what it is doing, its load and morale, its level and experience and what its type is about, or a
building's health, progress, goods, workers and training queue (with train buttons and a repeat
button); a storehouse or town hall of yours has a Keep stocked button per good for its supply rules (click
for more, right click for less; see [Supply rules](rules.md#automation)). Units that can evolve get a row of evolve buttons per type (hover one for what the form gains
and gives up). Workers carrying goods have a load on their back in the goods' colour.
Hurt and selected units and damaged buildings have a health bar; arrows, blasts and blows are drawn as
they land, units that have broken and are running away have a white ring under them, and your units
that can evolve, and your beaten monsters, a gold one. Wolf lairs are rock mounds with their pack
around them. A city-state's town is four stone huts around a market, whose banner takes its ally's
colour (pale while it has none).

Day, night and weather (on unless switched off on the start screen; see
[Day, night and weather](rules.md#day-night-and-weather) for what they do to sight and aim): the match
starts in the morning, and a whole day and night take 20 minutes of game time (faster at a higher game
speed, still while paused). The sun crosses the sky, mornings and evenings are golden, dusk is orange,
and the night is lit by a cool blue moon and stars, with a lantern glowing at the door of every building
you can see, flames on torches and campfires, and lava glowing in the hills. A pixel sun hangs in the
sky where the light comes from (a pixel moon at night); tilt the camera down to the horizon (F) around
sunrise or sunset, or at night, to see it and the stars. Weather rolls in and out: clouds grey the sky,
rain falls around the camera and darkens the ground, and a long rain turns grass and soil to dappled brown
mud with shining puddles that stays until the ground dries; storms bring wind and lightning and put out torches
(they smoke instead), and a morning fog closes the distance in. The sky turns before the weather does,
so you can see it coming. Trees sway in the wind, chimneys smoke, houses smoke at breakfast and supper,
and the swamp has mist at night and in the morning and fireflies on clear nights.

Each kind of tree has its own look: broad-leaved trees with round green crowns, pines with tiers of
blue-green needles over reddish bark, slender white birches with small yellow-green crowns, willows in the
swamp with curtains of leaves hanging from their rims, giant red-capped mushrooms with pale spots and gills,
and, in the hills about halfway between the starts, the one dusk tree: a huge bare, twisted grey tree with
cyan cracks and a skull at its foot, standing in purple mist and guarded by the Dusk Warden. Once the warden
is gone, right-click the tree with battle priests to heal it (it grows a violet canopy and one priest gets
the Dusk Staff), or, as the Orcs, with fighters to hack it down.

The time of day and the weather are beside the game clock at the top (a sun or moon, the hour, and the
weather unless it is clear, then Damp or Muddy while the ground is wet). Its tooltip says what is coming,
how far units see now and how often shots land, how wet the ground is and how much it slows units off the
roads, carts and siege engines, and speeds up farms. Click it to keep the world lit as by day: that changes only the look, and units still see as the
time and weather allow.
