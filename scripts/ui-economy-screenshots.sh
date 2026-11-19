#!/usr/bin/env bash
# Scripted-input screenshot test for the economy UI: workers sent to dig the stone outcrop by the start,
# roads researched from the tech tree, a house placed and built, a worker queued at the town hall, and a
# road laid. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-economy-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it, the
# stone outcrop at 163..165 x 389..391 (top block 164,22,390) and open ground for a house at 138..141 x 392..395.
STEPS="
wait 20
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
clickworld 164,22,390 right
ticks 40
print
key k
wait 3
clickui tech_roads
mouse 1500,820
ticks 40
print
shot $OUT/ui-tech-tree.png
key k
key h
mouseworld 140,g,394
wait 3
print
shot $OUT/ui-build-house.png
clickworld 140,g,394
ticks 4
print
key plus*2
ticks 500
shot $OUT/ui-house-built.png
print
clickworld 131,g,384
key u
ticks 4
print
shot $OUT/ui-town-hall.png
key l
clickworld 130,g,392
mouseworld 130,g,404
wait 3
shot $OUT/ui-road.png
clickworld 130,g,404
ticks 4
print
"
LOG="$OUT/ui-economy.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI economy test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 7 ] || fail "expected 7 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" working)" -eq 4 ] || fail "the four workers are not gathering: ${UI[0]}"
[ "$(field "${UI[1]}" techtree)" -eq 1 ] && [ "$(field "${UI[1]}" research)" = roads ] || fail "roads not being researched: ${UI[1]}"
[ "$(field "${UI[2]}" tool)" = Build ] && [ "$(field "${UI[2]}" ghost)" = Ok ] || fail "no house to place: ${UI[2]}"
[ "$(field "${UI[3]}" sites)" -eq 1 ] && [ "$(field "${UI[3]}" wood)" -eq 120 ] || fail "house site not placed and paid for: ${UI[3]}"
[ "$(field "${UI[4]}" sites)" -eq 0 ] && [ "$(field "${UI[4]}" buildings)" -eq 2 ] || fail "house not built: ${UI[4]}"
[ "$(field "${UI[4]}" techs)" -eq 1 ] && [ "$(field "${UI[4]}" research)" = none ] || fail "roads not researched: ${UI[4]}"
[ "$(field "${UI[5]}" building)" = town_hall ] && [ "$(field "${UI[5]}" queue)" -eq 1 ] || fail "no worker queued at the town hall: ${UI[5]}"
[ "$(field "${UI[6]}" stone)" -lt "$(field "${UI[5]}" stone)" ] || fail "no road laid: ${UI[6]}"
[ "$(field "${UI[0]}" food_rate)" -lt 0 ] || fail "food a minute should be the upkeep with no food coming in: ${UI[0]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 5 ] || fail "missing screenshots"
echo "UI economy screenshot test OK: $OUT"
