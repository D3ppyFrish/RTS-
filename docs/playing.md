# Playing the game

How to get a build, the controls and what the HUD shows. How each system works is in
[rules.md](rules.md).

## Getting a build

Every pull request and every push to `develop` produces a Windows build. Open the run on the
**Actions** tab, download the `RTS-windows-...` artifact, unzip it and run `RTS.exe`.

It opens on the start screen: pick the Settlers (who start in the west) or the Orcs (in the east), the
world seed, whether the computer plays the other side and how well (Computer level: Easy, Normal, which
is the default, Hard, or Classic, the first computer player; hover a level to read how it plays. No level
is given anything you are not, and only Classic sees through the fog of war), and whether there is fog of
war, then Start match. The computer plays the two factions differently: the Orcs mass their soldiers and
come in big waves from an open town; the Settlers (at Normal and Hard) wall their town and make the wall
stronger as the match goes on, and at Hard post their archers on it and keep a quarter of their soldiers
at home behind it when the rest attack.
Each faction has its own look for the menus, panels and buttons: castle stone for the Settlers, rough
timber for the Orcs. The start screen changes to the look of the faction you pick. Destroy the other side's
town hall to win; the Surrender button under the right end of the top bar gives up. When the match is
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
| Right click | depends on what is under the cursor: units attack an enemy unit or building (an enemy storehouse they capture instead); workers gather a tree (or a felled tree's rubble), berry bush, stone or ore, build a site or start a plan, or work a farm, smelter, quarry or mine, go on supply duty at a storehouse or town hall of yours that has supply rules, or clean up blood on the ground (the stains within 8 columns, nearest first); caravans trade with a city-state's town; otherwise move there |
| Ctrl+right click | attack-move: walk there and fight whatever is met on the way; on an enemy storehouse, burn it |
| Alt+right click | patrol: the selected soldiers walk there and back again and again, fighting whatever they meet, until given another order (units that cannot fight just walk there); the selection panel says "patrolling" |
| Alt+right click on one of your units or buildings | guard it: the selected units that can fight keep near it, follow it, and fight enemies that come near it or hit it, until given another order or it is gone; the selection panel says "guarding" |
| Right click, with only your town hall or barracks selected | set its rally point: a flag shows where the units it trains walk to when they step out (right click the building itself to clear it) |
| H, G, C, B, M, N | place a house, storehouse, farm, barracks, smelter or town hall; a see-through model of it follows the cursor, amber where it will be built, cyan or violet where it will be a plan, red where it does not fit. The selected workers build it (Shift+click places more; right click or Esc stops). Something you cannot afford yet is placed as a plan |
| Quarry and Mine buttons | place a quarry or a mine the same way (no key: from the build panel only). Right click it with workers to work it |
| Tab | plan mode (also the Plan mode button at the top of the Build panel): what you place is a plan, marked out but not paid for or built. Plans show as a see-through model: violet while you cannot pay for it, cyan (pulsing) once you can. Right click a plan, with workers selected or not, to pay and start building it; select it for Start building and Remove plan (Delete) buttons |
| O | place a gate (a barrier gate, 3 wood): a wall column whose bottom is a gate only your units walk through (draw a wall through it, or up to it) |
| I, ; | place a torch (1 wood) or a campfire (3 wood): from dusk to dawn they light the ground around them, so what stands there is seen and shot at as by day; a storm puts them out until it passes |
| Y | wall tool (also the Wall button): drag a line for the selected workers to wall off, a column at a time, with wooden barriers at 1 wood a column (the first tier of wall: cheap, low and easy to knock down); columns with no room or that you cannot pay for show red; Shift keeps the tool for another stretch; right click or Esc stops |
| Upgrade button, U | with a wall or gate of yours selected: upgrade the stretch of that tier it stands in (every wall and gate joined to it, nearest first, as far as your goods go) to the next tier; Only this one (Shift+U) upgrades that column alone. A barrier becomes a wooden wall (4 high, 3 wood a column) that archers stand on, and that a stone wall (6 high, 10 stone, needs Masonry), much sturdier and faced on both sides once it is done, so it is three columns thick with battlements along both edges; gates go the same way, and a stone gate is a gatehouse as deep as the wall. Workers do the work (automatic ones by themselves, or right click the wall with workers), and the wall stands as it is until they are done. On a stone wall that has lost some of its facing the button reads Put the facing back (3 stone a column) |
| Upgrade button; U, or Ctrl+U where U trains | with a house, town hall, storehouse, barracks, smelter or farm of yours selected: upgrade it to a bigger, better building of its kind (large house, then manor; great hall, then castle keep; warehouse; great barracks; foundry; large farm). Its tooltip says what the new one does better. It grows from its middle onto the free, level ground round it, so leave room: the button reads "no room round it to grow" when there is none. Workers do the work while the building keeps working as it is. Plain U trains at a town hall or barracks, so there it is Ctrl+U |
| /, \ | place a watchtower (30 wood; 3 by 3, 7 high) or a stone tower (60 stone and 20 wood, needs Masonry; 5 by 5, 9 high). On open ground it stands alone. With the cursor on a finished wall of yours, or right beside one, it snaps onto the wall, its middle on the line: the wall stands until the tower is done and is then part of it. A stone tower can be put over a watchtower the same way. A tower sees 14 or 18 columns around it, and archers posted on it aim better (below) |
| Right click a wall or tower of yours | with archers, mages or other ranged units selected, on a wooden or stone wall or gate, or a tower: they climb up and take posts along the edge of its top, one to a column, the nearest free columns to the one clicked (a stone wall holds a row on each side, a watchtower 8 and a stone tower 16). Up there they see and shoot a quarter further, lean out to shoot what stands at the foot of the wall, are out of reach of blows from the ground, and hold their ground; order them anywhere to bring them down. On a tower they also aim better: half (watchtower) or three in four (stone tower) of the shots they would miss in the dark or the rain land, and they hit the head more often |
| ' | bridge tool (also the Bridge button): drag from bank to bank for the selected workers to lay a level plank deck across deep water, a drop or a trench, 3 wood a column; columns with no room or that you cannot pay for show red, and nothing shows while the ends are not ground about level with each other; Shift keeps the tool; right click or Esc stops. Anyone walks over a bridge; Ctrl+right click one with soldiers selected (yours too) to knock that column out (see [Combat](rules.md#combat)) |
| L | lay a road: click where it starts, then where it ends (Shift carries on from there) |
| U | train at the selected building: a worker at a town hall, a spearman at a barracks (its panel has a button for each unit it trains) |
| P | repeat at the selected building: each unit it finishes is queued again once there are goods for it and a minute's food to spare |
| Shift+P | rebuild orders on or off (also the top bar's Rebuild button): your destroyed buildings are cleared and put back where they stood by your automatic workers, once no enemy is near and you can pay; a pale ghost marks each one waiting (see [Automation](rules.md#automation)) |
| Shift+U | with your town hall selected: its governor steps off, growth, army, defence (also the Governor buttons in its panel). The governor runs the town for you: idle workers there go automatic, houses, farms and the buildings of its focus go up, workers and soldiers are trained and techs researched (see [Automation](rules.md#automation)) |
| J, Shift+J | make the selected workers automatic, or stop; Shift+J does it for all your workers, and those trained later (see [Automation](rules.md#automation)) |
| Ctrl+J | work priorities menu (also the top bar's Priorities button, and Work priorities in a worker's panel): on the left pick the default list, the selected workers or any one worker; on the right move each kind of work up or down, take it off, or add one back. The default list is for new workers and every worker without a list of its own; "Use the default list" drops a worker's own list, "Put every worker on it" does that for all of them (see [Work priorities](rules.md#automation)) |
| V | stance of the selected soldiers, in turn: aggressive, hold their ground, retreat when hurt |
| Shift+V | hand the selected soldiers to the governor of your nearest governed town hall (also the Defend the town button): they become its town guard and defend the whole town, chasing raiders you can see within 24 columns of the hall (see [Automation](rules.md#automation)) |
| Evolve buttons | in the selection panel, for your units with the experience (they have a gold ring under them): a button per form evolves the selected units of that type into it, and Auto picks the form that type takes on its own (see [Evolution](rules.md#evolution)) |
| Beaten, Ride, Recruit | Orcs only: the top bar's Beaten button selects your next beaten monster (it has a gold ring under it); in its panel, Ride has the nearest spearman climb on and become a wolf rider, and Recruit makes it one of your units (see [Monsters and taming](rules.md#monsters-and-taming)). With units selected near a cleared den, the Orcs' panel has a button to make the den a wolf lodge, and the Settlers' a button to make the selected spearman nearest it a Wolfheart (see [Cleared dens](rules.md#cleared-dens)) |
| Town buttons, Hire | the top bar has a button per city-state's town saying who holds it; it (or clicking the town) selects the town, whose panel shows its totem and guards and, for its holder, has a Hire button per mercenary. Right-click the totem with soldiers to break it and take the town (see [City-states](rules.md#city-states)) |
| Right-click the dusk tree, `,` | once its warden is gone: battle priests heal the tree and one gets the Dusk Staff (a bearer that dies drops it, or its relic, where it fell: walk a unit over it to pick it up); Orc fighters hack it down instead. With the staff's bearer selected, the Dusk Bloom button or `,` heals every ally near it (see [The dusk tree](rules.md#the-dusk-tree)) |
| Shift+Y | with a mage selected (also the Earth Wall button in the selection panel): drag a line of up to 8 columns and let go, and a ready mage raises a wall of earth along it, walking into range first. Green squares are where earth rises; red ones have a unit, a building or water on them (see [Terrain magic](rules.md#terrain-magic)) |
| Shift+' | with a mage selected (also the Ice Bridge button in the selection panel): drag from bank to bank across water and let go, and a ready mage freezes a level way of ice across, three columns wide and up to 16 long, walking into range first. Green squares are where ice forms; red ones have something in the way. Units on the ice slide: they are slow to get going and to stop. Dark cracks show on it in its last 5 seconds before it melts (see [Terrain magic](rules.md#terrain-magic)) |
| K | open the tech tree: click a tech to research it at your town hall |
| . | select your next idle worker and look at it (as the Idle workers button does) |
| F2 | select all your combat units, leaving workers and caravans out (as the Army button does) |
| Ctrl+1..9, 1..9 | assign a control group, recall it (press twice to centre the camera on it) |
| Space, - and + | pause (orders still work), slower and faster: 0.5x, 1x, 2x, 4x |
| F7 | detailed units on or off: near the camera, Settlers workers, spearmen and archers are drawn finer and swing their arms and legs as they walk and fight (a prototype; see the README) |
| PgDn, PgUp, Home | height slice: hide every layer above a cut to see tunnels and caves; Home turns it off |
| T | terrain tool, for trying things out (not in matches from the start screen): left click digs a block, right click places stone (T or Esc to leave) |
| X | tunnel tool: the selected sappers dig to the block clicked, a level up or down each column; click a building to dig just under it so that part of it falls in (Shift+click digs another; right click or Esc stops) |
| Z | dig tool (also the Dig an area button): drag out an area for the selected workers and sappers to dig out, [ and ] for how many levels deep (below the ground where the drag starts); Shift keeps the tool for another area; right click or Esc stops |

The HUD shows the goods in your storage and your population over housing (each beside its picture; hover for its name), food a minute beside the food (the food gathered in the last minute less what your people eat each minute; green when it grows, red when it shrinks), the game clock, the tech
being researched, the tech tree, automatic workers and speed buttons, the Idle workers and Army buttons and alerts under the top bar on the left (see [Alerts and idle workers](rules.md#alerts-and-idle-workers)), build buttons on the left (a building you lack the
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
rain falls around the camera and darkens the ground, storms bring wind and lightning and put out torches
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
weather unless it is clear). Its tooltip says what is coming, how far units see now and how often shots
land. Click it to keep the world lit as by day: that changes only the look, and units still see as the
time and weather allow.
