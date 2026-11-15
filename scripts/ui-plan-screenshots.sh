#!/usr/bin/env bash
# Scripted-input screenshot test for building ghosts and planner mode (issue #92): with plan mode on (Tab) a
# house is planned (cyan: it can be paid for) and a town hall too (violet: not enough goods); with it off a
# house is built by the selected workers (amber ghost of the blocks still to come); a right click starts the
# house plan, and the town hall plan is selected and removed. Saves PNGs to OUT_DIR and fails if the UI state
# is not as expected.
# Usage: scripts/ui-plan-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/plans}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 84..90 x 253..259 holding 150 wood and 50 stone, the starting units in
# rows just south of it; open ground for houses at 94..97 x 264..267 and 80..83 x 245..248, and for a town
# hall at 94..100 x 245..251.
STEPS="
wait 20
camera 87,261 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
key tab
key h
mouseworld 96,g,266
wait 3
print
shot $OUT/ui-plan-cursor.png
clickworld 96,g,266
ticks 4
print
key n
mouseworld 97,g,248
wait 2
clickworld 97,g,248
ticks 4
print
key tab
key h
mouseworld 82,g,247
wait 2
clickworld 82,g,247
ticks 4
print
ticks 50
camera 85,253 25 45 17
wait 3
shot $OUT/ui-plan-colours.png
camera 82,247 30 45 11
wait 2
shot $OUT/ui-plan-site.png
camera 89,256 20 45 22
key escape
clickworld 96,g,266 right
ticks 4
print
wait 3
shot $OUT/ui-plan-started.png
clickworld 97,g,248
wait 3
print
shot $OUT/ui-plan-selected.png
key delete
ticks 4
print
"
LOG="$OUT/ui-plan.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI plan test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 7 ] || fail "expected 7 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" plan_mode)" -eq 1 ] && [ "$(field "${UI[0]}" ghost)" = Ok ] || fail "plan mode not on with a house to place: ${UI[0]}"
[ "$(field "${UI[1]}" plans)" -eq 1 ] && [ "$(field "${UI[1]}" plans_ready)" -eq 1 ] && [ "$(field "${UI[1]}" wood)" -eq 150 ] \
  || fail "house not planned for free: ${UI[1]}"
[ "$(field "${UI[2]}" plans)" -eq 2 ] && [ "$(field "${UI[2]}" plans_ready)" -eq 1 ] || fail "town hall not planned (unaffordable): ${UI[2]}"
[ "$(field "${UI[3]}" plan_mode)" -eq 0 ] && [ "$(field "${UI[3]}" sites)" -eq 1 ] && [ "$(field "${UI[3]}" wood)" -eq 120 ] \
  || fail "house not built with plan mode off: ${UI[3]}"
[ "$(field "${UI[4]}" plans)" -eq 1 ] && [ "$(field "${UI[4]}" wood)" -eq 90 ] || fail "right click did not start the house plan: ${UI[4]}"
[ "$(field "${UI[5]}" building)" = town_hall ] || fail "town hall plan not selected: ${UI[5]}"
[ "$(field "${UI[6]}" plans)" -eq 0 ] || fail "town hall plan not removed: ${UI[6]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 5 ] || fail "missing screenshots"
echo "UI plan screenshot test OK: $OUT"
