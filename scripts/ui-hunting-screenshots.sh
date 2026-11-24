#!/usr/bin/env bash
# Scripted-input screenshot test for hunting: the starting workers put up a hunter's lodge on the grassland north of
# the town, near the west start's own herd of deer, from the build panel; then two are sent to work it. They stalk
# and shoot deer and carry the meat home. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-hunting-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. The start's herd
# grazes round 116,305; the lodge goes at 120,330 with the mouse on its middle, within its reach of the herd.
# That is more than 32 columns from the town hall, a far site: its builders carry its wood out first (issue #149).
STEPS="
wait 20
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
camera 117,306 30 35 9
wait 5
shot $OUT/ui-hunting-deer.png
camera 122,345 45 55 22
wait 5
clickui build_hunting_lodge
mouseworld 120,g,330
wait 3
print
clickworld 120,g,330
ticks 4
key plus*2
ticks 2400
print
clickworld 120,g,330 right
ticks 900
camera 118,318 45 50 26
wait 5
print
shot $OUT/ui-hunting-stalk.png
ticks 1800
camera 110,316 45 50 22
wait 5
print
shot $OUT/ui-hunting-lodge.png
"
LOG="$OUT/ui-hunting.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI hunting test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected_workers)" -ge 3 ] || fail "the workers are not selected: ${UI[0]}"
[ "$(field "${UI[0]}" herds)" -ge 2 ] || fail "the map should have herds of deer: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Build ] && [ "$(field "${UI[1]}" ghost)" = Ok ] || fail "the lodge should fit on open grass: ${UI[1]}"
[ "$(field "${UI[2]}" sites)" -eq 0 ] && [ "$(field "${UI[2]}" quarries)" -eq 1 ] || fail "lodge not built: ${UI[2]}"
[ "$(field "${UI[4]}" hunted)" -ge 2 ] || fail "the hunters should have brought deer home: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI hunting screenshot test OK: $OUT"
