#!/usr/bin/env bash
# Scripted-input screenshot test for walls, gates and towers (issue #44): the starting units are selected, O
# places a gate by the start and Y draws a wall through it. The workers build the wall a column at a time,
# leaving the gate in it: a wooden barrier, the first tier. Then a column of it is selected and its Upgrade
# button turns the stretch into a wooden wall four blocks high, the units are right-clicked onto it and the
# archers and mages take posts on top, and with masonry researched it is upgraded again to stone, under
# them: six blocks high, and faced on both sides when it is done, so it is three columns thick with a
# gatehouse round the gate. Then / puts a watchtower onto the wall (its ghost snaps onto the line from the
# column beside it) and \ a stone tower across it, the workers build them round the wall that stands there,
# and the archers are sent up the stone tower. Saves PNGs to OUT_DIR and fails if the UI state is not as
# expected.
# Usage: scripts/ui-wall-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/walls}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 84..90 x 253..259, starting units in rows just south of it, and open
# ground at 92..103 x 262..269.
STEPS="
wait 20
camera 97,264 30 50 20
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
key o
mouseworld 97,g,266
wait 2
clickworld 97,g,266
wait 2
print
key y
wait 2
mouseworld 92,g,266
down here
mouseworld 103,g,266
wait 3
print
shot $OUT/ui-wall-drag.png
up here
ticks 4
print
key plus*2
ticks 200
wait 5
shot $OUT/ui-wall-building.png
ticks 1600
camera 97,262 20 40 16
wait 5
print
shot $OUT/ui-wall-done.png
key k
wait 3
clickui tech_tools
mouse 1500,820
key k
clickworld 93,g,266
wait 3
print
shot $OUT/ui-wall-upgrade-button.png
clickui wall_upgrade
ticks 2
print
dragworld 84,g,258 104,g,270
wait 2
clickworld 93,g,266 right
ticks 1300
key k
wait 3
clickui tech_masonry
mouse 1500,820
key k
print
clickworld 100,g,266 right
ticks 400
wait 5
print
shot $OUT/ui-wall-posted.png
ticks 400
clickworld 93,g,266
wait 3
clickui wall_upgrade
ticks 2
print
dragworld 84,g,258 104,g,270
wait 2
clickworld 93,g,266 right
ticks 3600
wait 5
print
shot $OUT/ui-wall-stone.png
camera 98,267 30 45 22
wait 5
key backslash
mouseworld 102,g,267
wait 3
print
shot $OUT/ui-tower-snap.png
clickworld 102,g,267
ticks 2
print
ticks 1800
print
key slash
mouseworld 95,g,273
wait 3
print
clickworld 95,g,273
ticks 2
print
ticks 200
wait 5
shot $OUT/ui-tower-building.png
ticks 800
print
dragworld 84,g,258 106,g,276
wait 2
clickworld 102,g,266 right
ticks 600
clickworld 102,g,266
wait 5
print
shot $OUT/ui-tower-posted.png
"
LOG="$OUT/ui-wall.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --goods=500 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI wall test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 18 ] || fail "expected 18 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" own)" -gt 8 ] || fail "the starting units are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" gates)" -eq 1 ] || fail "no gate placed: ${UI[1]}"
[ "$(field "${UI[2]}" tool)" = Wall ] && [ "$(field "${UI[2]}" wall_preview)" -eq 11 ] || fail "the wall being drawn is not 11 columns round the gate: ${UI[2]}"
[ "$(field "${UI[3]}" tool)" = Select ] && [ "$(field "${UI[3]}" walls)" -eq 11 ] || fail "no wall laid: ${UI[3]}"
[ "$(field "${UI[4]}" walls)" -eq 11 ] && [ "$(field "${UI[4]}" gates)" -eq 1 ] || fail "the wall or gate is gone: ${UI[4]}"
[ "$(field "${UI[4]}" sites)" -eq 0 ] || fail "the wall is not finished: ${UI[4]}"
[ "$(field "${UI[5]}" building)" = barrier ] || fail "the barrier is not selected: ${UI[5]}"
[ "$(field "${UI[6]}" upgrading)" -eq 12 ] || fail "the stretch is not being upgraded: ${UI[6]}"
[ "$(field "${UI[6]}" wood)" -eq $(($(field "${UI[5]}" wood) - 38)) ] || fail "the upgrade was not paid for (3 wood a wall, 5 the gate): ${UI[6]}"
[ "$(field "${UI[7]}" tier1)" -eq 12 ] && [ "$(field "${UI[7]}" upgrading)" -eq 0 ] || fail "the barrier did not become a wooden wall: ${UI[7]}"
[ "$(field "${UI[7]}" posted)" -eq 0 ] || fail "somebody is on the wall before being sent: ${UI[7]}"
[ "$(field "${UI[8]}" posted)" -eq 7 ] || fail "the archers and mages are not posted on the wall: ${UI[8]}"
[ "$(field "${UI[9]}" upgrading)" -eq 12 ] || fail "the wooden wall is not being upgraded: ${UI[9]}"
[ "$(field "${UI[10]}" tier2)" -eq 12 ] && [ "$(field "${UI[10]}" upgrading)" -eq 0 ] || fail "the wooden wall did not become stone: ${UI[10]}"
[ "$(field "${UI[10]}" posted)" -eq 7 ] || fail "the archers and mages did not stay on the wall as it turned to stone: ${UI[10]}"
[ "$(field "${UI[10]}" facings)" -eq 24 ] && [ "$(field "${UI[10]}" sites)" -eq 0 ] || fail "the stone wall is not faced on both sides, the gate too: ${UI[10]}"
[ "$(field "${UI[11]}" tool)" = Build ] && [ "$(field "${UI[11]}" ghost)" = Ok ] || fail "a stone tower does not fit onto the wall: ${UI[11]}"
[ "$(field "${UI[12]}" towers)" -eq 1 ] && [ "$(field "${UI[12]}" tier2)" -eq 12 ] || fail "no stone tower site on the wall, or the wall under it is gone already: ${UI[12]}"
[ "$(field "${UI[12]}" stone)" -eq $(($(field "${UI[10]}" stone) - 60)) ] || fail "the stone tower was not paid for (60 stone): ${UI[12]}"
[ "$(field "${UI[13]}" towers)" -eq 1 ] && [ "$(field "${UI[13]}" sites)" -eq 0 ] || fail "the stone tower is not finished: ${UI[13]}"
[ "$(field "${UI[13]}" tier2)" -eq 8 ] && [ "$(field "${UI[13]}" facings)" -eq 16 ] || fail "the stone tower did not take in the wall it stands on (4 columns of it, with their facing): ${UI[13]}"
[ "$(field "${UI[14]}" ghost)" = Ok ] || fail "a watchtower does not fit on open ground: ${UI[14]}"
[ "$(field "${UI[15]}" towers)" -eq 2 ] && [ "$(field "${UI[15]}" wood)" -eq $(($(field "${UI[13]}" wood) - 30)) ] || fail "no watchtower site (30 wood): ${UI[15]}"
[ "$(field "${UI[16]}" towers)" -eq 2 ] && [ "$(field "${UI[16]}" sites)" -eq 0 ] || fail "the watchtower is not finished: ${UI[16]}"
[ "$(field "${UI[17]}" building)" = stone_tower ] && [ "$(field "${UI[17]}" posted_on_towers)" -ge 5 ] || fail "the archers and mages are not up on the stone tower: ${UI[17]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 9 ] || fail "missing screenshots"
echo "UI wall screenshot test OK: $OUT"
