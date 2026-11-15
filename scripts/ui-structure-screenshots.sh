#!/usr/bin/env bash
# Screenshot test for buildings drawn at character scale: masonry and smelting are researched, a barracks,
# storehouse and smelter are placed by the start with a wall and gate in front, the workers build them, and
# the town is photographed close up from a few sides. Saves PNGs to OUT_DIR and fails if the UI state is
# not as expected.
# Usage: scripts/ui-structure-screenshots.sh [OUT_DIR]
# CI: skip (close-up look checks, run by hand when the looks change)
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/structures}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Tools come first, as masonry needs them. Seed 1234, west start: town hall at 84..90 x 253..259, starting units in rows just south of it, and open
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
clickworld 86,g,268 right
key plus*2
key k
wait 3
clickui tech_tools
mouse 1500,820
key k
ticks 650
key k
wait 3
clickui tech_masonry
mouse 1500,820
key k
ticks 650
key k
wait 3
clickui tech_smelting
mouse 1500,820
key k
ticks 650
print
key b
clickworld 98,g,257
ticks 2
print
key g
clickworld 101,g,265
ticks 2
print
key m
clickworld 93,g,265
ticks 2
print
key o
clickworld 98,g,272
ticks 2
print
key y
mouseworld 94,g,272
down here
mouseworld 103,g,272
wait 3
up here
ticks 4
print
key shift+j
ticks 300
shot $OUT/ui-structures-building.png
ticks 6000
print
camera 96,266 20 40 16
wait 5
shot $OUT/ui-structures-town.png
camera 96,266 200 35 14
wait 5
shot $OUT/ui-structures-back.png
camera 98,271 15 20 6
wait 5
shot $OUT/ui-structures-wall.png
camera 87,258 15 25 9
wait 5
shot $OUT/ui-structures-hall.png
"
LOG="$OUT/ui-structures.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --goods=2000 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI structure test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 7 ] || fail "expected 7 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" techs)" -ge 3 ] || fail "tools, masonry and smelting are not researched: ${UI[0]}"
for k in 1 2 3 4; do
  [ "$(field "${UI[$k]}" buildings)" -eq $((k + 1)) ] || fail "building $k was not placed: ${UI[$k]}"
done
[ "$(field "${UI[5]}" walls)" -eq 10 ] || fail "the wall was not laid: ${UI[5]}"
[ "$(field "${UI[6]}" buildings)" -eq 15 ] && [ "$(field "${UI[6]}" sites)" -eq 0 ] || fail "the town is not finished: ${UI[6]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 5 ] || fail "missing screenshots"
echo "UI structure screenshot test OK: $OUT"
