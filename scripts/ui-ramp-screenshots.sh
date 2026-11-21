#!/usr/bin/env bash
# Scripted-input screenshot test for ramps: the starting units are sent north to the foot of a cliff 7 levels
# high, and Shift+X draws a ramp up its face. The workers heap it from the foot up, a column at a time, and
# then walk up it onto the cliff top. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-ramp-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ramps}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. North of
# it a cliff face runs along x = 103 to 104 from z = 108 to 144; at z = 124 its foot is at level 82 four columns
# out (x = 96..97) and its top at 89 (x = 104): a ramp of 23 blocks from x = 96 climbs it.
STEPS="
wait 20
camera 141,392 30 50 20
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
clickworld 94,g,124 right
key plus*3
ticks 2400
key minus*3
camera 100,124 20 32 26
wait 5
key shift+x
wait 2
mouseworld 96,g,124
down here
mouseworld 104,g,124
wait 3
print
shot $OUT/ui-ramp-drag.png
up here
ticks 4
print
key plus*3
ticks 600
wait 5
shot $OUT/ui-ramp-building.png
ticks 2400
wait 5
print
clickworld 108,g,124 right
ticks 900
wait 5
print
shot $OUT/ui-ramp-done.png
"
LOG="$OUT/ui-ramp.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI ramp test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" own)" -gt 8 ] || fail "the starting units are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Ramp ] && [ "$(field "${UI[1]}" ramp_top)" -gt 0 ] && [ "$(field "${UI[1]}" wall_preview)" -ge 5 ] \
  || fail "the ramp being drawn does not reach the cliff top: ${UI[1]}"
[ "$(field "${UI[2]}" tool)" = Select ] && [ "$(field "${UI[2]}" ramps)" -eq 1 ] && [ "$(field "${UI[2]}" ramp_builders)" -gt 0 ] \
  || fail "nobody is heaping the ramp: ${UI[2]}"
[ "$(field "${UI[3]}" ramps)" -eq 0 ] && [ "$(field "${UI[3]}" ramp_builders)" -eq 0 ] || fail "the ramp is not finished: ${UI[3]}"
[ "$(field "${UI[3]}" stone)" -lt "$(field "${UI[2]}" stone)" ] || fail "the ramp cost no stone: ${UI[2]} / ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI ramp screenshot test OK: $OUT"
