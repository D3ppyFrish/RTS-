#!/usr/bin/env bash
# Scripted-input screenshot test for the fishing hut: the starting workers put one up at the edge of the lake
# west of the town from the build panel (out on open grass it shows red: it must stand right beside water),
# then are sent to fish there. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-fishing-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. The water's
# edge west of it: a 4 by 4 hut fits with its corner at 82,383 (the mouse on its middle, 84,385).
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
clickui build_fishing_hut
mouseworld 120,g,400
wait 3
print
camera 86,386 45 55 22
wait 5
mouseworld 84,g,385
wait 3
print
shot $OUT/ui-fishing-place.png
clickworld 84,g,385
ticks 4
print
key plus*2
ticks 900
print
clickworld 84,g,385 right
ticks 300
print
shot $OUT/ui-fishing-working.png
"
LOG="$OUT/ui-fishing.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI fishing test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 6 ] || fail "expected 6 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected_workers)" -ge 3 ] || fail "the workers are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Build ] && [ "$(field "${UI[1]}" ghost)" = NoWater ] || fail "the hut should not fit away from water: ${UI[1]}"
[ "$(field "${UI[2]}" ghost)" = Ok ] || fail "the hut should fit at the water's edge: ${UI[2]}"
[ "$(field "${UI[3]}" sites)" -eq 1 ] || fail "fishing hut site not placed: ${UI[3]}"
[ "$(field "${UI[4]}" sites)" -eq 0 ] && [ "$(field "${UI[4]}" quarries)" -eq 1 ] || fail "fishing hut not built: ${UI[4]}"
[ "$(field "${UI[5]}" fishing)" -eq 2 ] || fail "two workers should fish at the hut: ${UI[5]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI fishing screenshot test OK: $OUT"
