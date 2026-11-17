# RTS project: working rules

3D voxel RTS in Godot 4 (C#). Design doc: https://claude.ai/artifact/NUFTDiS6HPWihhhmrhiyQm

## Layout

- `src/Sim/` simulation core, plain C# (no Godot). All game rules live here.
- `tests/Sim.Tests/` xUnit tests for the core.
- `game/` Godot project: rendering, input, UI. Reads from the core, never owns game rules.
- `scripts/` setup, screenshot and export helpers.

## Where things live

Read only the rows your task touches. How each system plays is in `docs/rules.md` (one `##` section
per system), controls and HUD in `docs/playing.md`; `README.md` keeps the developer notes (commands,
maps, units, game data, match runner, scripted input). Core paths are under `src/Sim/`, tests under
`tests/Sim.Tests/`, scripts under `scripts/`. Most UI for a system is in `game/scripts/Hud.cs` (panels,
buttons) and `PlayerControl.cs` (keys, clicks, tools).

| System | Core | Tests | Screenshot script |
| --- | --- | --- | --- |
| Economy (gather, build, train, roads, raids) | `Economy/EconomySystem.cs` (+ `.Jobs`, `.Research`, `.Trade` partials), `Economy/BuildingDef.cs`, `Economy/Goods.cs` | `EconomyTests.cs`, `ResearchTests.cs`, `QuarryTests.cs` | `ui-economy-screenshots.sh` |
| Plans and building ghosts | `Economy/EconomySystem.cs` (`PlanCommand`, `CanStart`); game: `GhostRenderer.cs` | `PlanTests.cs` | `ui-plan-screenshots.sh` |
| Walls, gates and towers (tiers, upgrades, facings, archers posted on top) | `Economy/EconomySystem.Walls.cs` (upgrades, `Sides`, `FacingCells`, posts); towers onto walls: `Economy/EconomySystem.cs` (`TakesPlaceOf`, `Absorb`); tiers in `data/buildings.json` (`upgrade`, `facing`, `sturdy`, `walkway`, `aim`); units on walls: `Units/CombatSystem.cs` (`Holding`, `BringDown`, `Aim`, `ClearShot`, `Breach`), `Units/UnitMovement.cs` (`ClimbDown`); rubble: `World/PhysicsSystem.cs` (`Spreads`); looks: `Meshing/StructureDetail.cs` (`Rampart`, `Trestle`, `Parapet`); game: `PlayerControl.cs` (`SnapToWall`) | `WallTests.cs`, `TowerTests.cs` | `ui-wall-screenshots.sh` |
| Bridges | `Economy/EconomySystem.Bridges.cs` | `BridgeTests.cs` | `ui-bridge-screenshots.sh` |
| Combat (orders, damage, morale, healing, buildings) | `Units/CombatSystem.cs` (+ `.Structures`, `.Tunnels`, `.Guard` partials); patrols: `CombatSystem.Turn`, `Simulation.ApplyMove`; rally points: `EconomySystem.Apply(RallyCommand)`, `StepTraining`; game: `SelectionRenderer.cs` (rally flag) | `CombatTests.cs`, `HealingTests.cs`, `PatrolTests.cs`, `GuardTests.cs` | `ui-combat-screenshots.sh`, `ui-patrol-screenshots.sh`, `ui-guard-screenshots.sh` |
| Bleeding and blood on the ground | `Units/CombatSystem.cs` (bleeding), `World/BloodSystem.cs`; game: `BloodRenderer.cs` | `BleedingTests.cs` | `ui-blood-screenshots.sh` |
| Formations | `Units/Formations.cs` | `FormationTests.cs` | `ui-formation-screenshots.sh` |
| Automation (automatic workers, repeat, stances) | `Automation/AutomationSystem.cs` | `AutomationTests.cs` | `ui-automation-screenshots.sh` |
| Rebuild orders (ruins put back) | `Automation/AutomationSystem.Rebuild.cs` (hooks: `CombatSystem.Collapse`, `EconomySystem.StepRaid`); game: `GhostRenderer.cs` (ruin ghosts) | `RebuildTests.cs` | `ui-rebuild-screenshots.sh` |
| Town governor (a town hall run to a focus) | `Automation/AutomationSystem.Governor.cs` (`GovernCommand`, `Building.Governor`); game: `Hud.cs` (`BuildGovernorRow`), `PlayerControl.cs` (`SetGovernor`, Shift+U) | `GovernorTests.cs` | `ui-governor-screenshots.sh` |
| Supply rules (storage kept stocked from other storage) | `Economy/EconomySystem.Supply.cs` (`SupplyCommand`, `UnitJob.Supply`), `Automation/AutomationSystem.cs` (`Wants`, rank 3); game: `Hud.cs` (`BuildSupplyRow`), `PlayerControl.cs` (`CycleSupply`) | `SupplyTests.cs` | `ui-supply-screenshots.sh` |
| Work priorities (per-worker and default lists) | `Automation/WorkPriorities.cs`, `Automation/AutomationSystem.cs` (`FindJob`); game: `PrioritiesPanel.cs` | `PriorityTests.cs` | `ui-priorities-screenshots.sh` |
| Evolution (Settlers) | `Units/EvolutionSystem.cs` | `EvolutionTests.cs` | `ui-evolution-screenshots.sh` |
| Monsters and taming (Orcs) | `Units/MonsterSystem.cs` | `MonsterTests.cs` | `ui-monsters-screenshots.sh` |
| Dusk tree (warden, healing, staff, razing) | `Units/DuskTreeSystem.cs`; tree shape: `World/RegionGenerator.cs` (`GrowDuskTree`) | `DuskTreeTests.cs` | `ui-dusktree-screenshots.sh` |
| Terrain magic (Earth Wall, Ice Bridge) | `Units/SpellSystem.cs` (`EarthWallCommand`, `IceBridgeCommand`, `PlanIce`; `"earthWall"`, `"iceBridge"` in `data/units.json`; `BlockType.Ice`); game: `PlayerControl.cs` (`ToolMode.EarthWall`, Shift+Y; `ToolMode.IceBridge`, Shift+'), `Hud.cs` (`RefreshEarthWall`, `RefreshIceBridge`), `IceRenderer.cs` (cracks) | `SpellTests.cs`, `IceBridgeTests.cs` | `ui-spell-screenshots.sh`, `ui-ice-screenshots.sh` |
| City-states | `Economy/CityStateSystem.cs`, `Content/CityStateDef.cs` | `CityStateTests.cs` | `ui-citystate-screenshots.sh` |
| Computer players (AI) | `AI/AiPlayer.cs` (Classic), `AI/AiPlayer.Smart.cs` (Easy, Normal; fog, scouting), `AI/AiPlayer.Hard.cs` (Hard's town), `AI/AiPlayer.HardArmy.cs` (Hard's army), `AI/AiPlayer.HardWall.cs` (Hard's wall; how it plays each faction, `aiStyle` in `data/factions.json`), `AI/AiMatch.cs` | `AiTests.cs`, `HardAiTests.cs` | none; `ai-matches.sh` and `ai-versus.sh` play headless AI matches (`--story`, `--trace` in the match runner say what they did) |
| Winning, match end, replays | `MatchSystem.cs`, `Replay/CommandLog.cs`, `Replay/MatchReplay.cs`; game: `StartScreen.cs`, `MatchEndPanel.cs`, `Replays.cs` | `MatchTests.cs`, `ReplayTests.cs` | `ui-match-screenshots.sh` |
| Fog of war | `VisionSystem.cs`, `WorldMemory.cs`; game: `FogOfWar.cs` | `VisionTests.cs` | `ui-fog-screenshots.sh` |
| Digging (dig areas, tunnels) | `Economy/EconomySystem.Dig.cs`, `Economy/DigArea.cs`, `Units/CombatSystem.Tunnels.cs` | `DigTests.cs` | `ui-dig-screenshots.sh` |
| Falling ground and collapse | `World/PhysicsSystem.cs` | `PhysicsTests.cs` | `ui-physics-screenshots.sh`, `ui-collapse-detail-screenshots.sh` |
| Content loading (`data/*.json`) | `Content/ContentLoader.cs`, `Content/ContentDb.cs` | `ContentTests.cs` | none |
| Alerts and idle workers | `EventLog.cs`; game: `Alerts.cs` | `EventTests.cs` | `ui-alerts-screenshots.sh` |
| Movement and pathfinding | `Units/UnitMovement.cs`, `Units/WalkGraph.cs`, `Units/FlowField.cs` | `UnitTests.cs`, `PathTests.cs` | none |
| Map generation | `World/RegionGenerator.cs`, `World/RegionMap.cs` | `RegionGeneratorTests.cs` | none |
| Building, tree and unit looks | `Meshing/StructureMesher.cs`, `Meshing/StructureDetail.cs`; game: `UnitModels.cs`, `UnitIcons.cs`, `FallingRenderer.cs` (falling ground and trees) | `StructureDetailTests.cs` | `ui-structure-screenshots.sh`, `ui-tree-screenshots.sh`, `ui-lineup-screenshots.sh` |
| Chunk drawing: levels of detail, and the half-size blocks prototype (`--terrain=fine`) | `World/FineTerrain.cs` (the world in half-size blocks), `World/FineWorld.cs` (kept exact as blocks change), `Meshing/GreedyMesher.cs` (any cube size), `Meshing/StructureMesher.cs` (`Shrink`: bigger voxels far away), `VoxelWorld.cs` (`Upscaled`, `CopyBoxPadded`); game: `ChunkRenderer.cs` (`ChunkLod`, `Reach`, `GroundDrop`, `GroundVersion`), `ChunkMesher.cs`, `TerrainMaterials.cs` (`v_scale`) | `FineTerrainTests.cs`, `GreedyMesherTests.cs` | none; `--benchmark` with `--terrain=fine`, `--lod=off`; any `ui-*.sh` with `RTS_ARGS=--terrain=fine` |
| Detail prototype (finer, animated units near the camera; detailed castle scenery) | game: `Detail/FineModel.cs` (grid, mesher, shader), `Detail/DetailedUnits.cs` (drawn from `UnitRenderer.cs`), `Detail/DetailedKeep.cs` (`--keeps`) | none (no tests for `game/`) | none; `--benchmark` with `--detail=off/on`, `--keeps=N` |
| UI (HUD, input, camera, selection) | game: `Hud.cs`, `PlayerControl.cs`, `CameraRig.cs`, `SelectionRenderer.cs`, `TechTreePanel.cs`; scripted input: `InputScript.cs` | none (no tests for `game/`) | `ui-screenshots.sh` |
| Test mode (spawn anything, cheats, scenarios) | `Sandbox/SandboxSystem.cs`, `World/Trees.cs`; game: `TestPanel.cs` | `SandboxTests.cs` | `ui-test-mode-screenshots.sh` |
| Day, night and weather (rules: sight, aim, lights, lava) | `Conditions.cs` | `ConditionsTests.cs` | `ui-daynight-screenshots.sh` |
| Lighting and ambience (look) | game: `WorldLighting.cs` (sky, sun, haze, lanterns), `DayCycle.cs` (the rules' time and weather as a look), `Ambience.cs` (flames, smoke, rain, stars, swamp mist and fireflies, lava lights), `TerrainMaterials.cs` (block look, voxel grain, voxel-stepped shadows, lit windows, water, swaying leaves, wet ground), `Outlines.cs` (dark outlines); shading in corners: `Meshing/GreedyMesher.cs` (`Occlusion`), `Meshing/StructureMesher.cs` (`FaceAo`) | `OcclusionTests.cs` | `ui-daynight-screenshots.sh` |
| UI look (theme, fonts, icons) | game: `UiStyle.cs` (looks, panels, buttons), `UiIcons.cs` (pixel icons; `icon` in the data), fonts in `game/fonts` (OFL) | `ContentTests.cs` (icons in the data) | `ui-style-screenshots.sh` |

Tick order and commands: `Simulation.cs`, `Commands.cs`. Determinism check: `determinism.sh`
(match runner in `tools/MatchRunner`). CI (`.github/workflows/ci.yml`) runs every `scripts/ui-*.sh` as its own parallel job, found
automatically; a script with a `# CI: skip` header line is left out (`ui-structure`, `ui-tree`,
`ui-collapse-detail`, `ui-lineup`).

## Rules for the simulation core

- Deterministic for lockstep multiplayer: no `float`/`double`, no `System.Random`, no `DateTime`,
  no `Dictionary`/`HashSet` iteration where order affects results. Use `Fix64`, `DeterministicRandom`
  and sorted collections.
- Player actions are `Command`s scheduled on a tick. Nothing else changes state.
- Content (units, buildings, techs) is data, not code, once the data loader exists.
- Every new rule gets a test. Keep the same-inputs-same-hash test passing.

## Checks before pushing

1. `dotnet test RTS.sln`
2. `dotnet build game/Game.csproj`
3. For visual changes: `scripts/screenshot.sh screenshots/<name>.png` and look at the image.

## Branches and merging (standing rules from the repo owner)

- Work on a feature branch, open a PR into `develop`.
- Claude may merge its own PR into `develop` once CI is green and the change matches an issue or a request.
- Only the repo owner merges `develop` into `main`.
- The backlog is GitHub Issues; pick the next open issue in the current milestone, lowest number first.
- Never force-push `develop` or `main`. Never skip or disable a failing test.
