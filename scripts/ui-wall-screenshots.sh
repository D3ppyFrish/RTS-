#!/usr/bin/env bash
# Scripted-input screenshot test for walls and gates (issue #44): the starting units are selected, O places
# a gate by the start and Y draws a wall through it. The workers build the wall a column at a time, leaving
# the gate in it. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
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
"
LOG="$OUT/ui-wall.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI wall test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" own)" -gt 8 ] || fail "the starting units are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" gates)" -eq 1 ] || fail "no gate placed: ${UI[1]}"
[ "$(field "${UI[2]}" tool)" = Wall ] && [ "$(field "${UI[2]}" wall_preview)" -eq 11 ] || fail "the wall being drawn is not 11 columns round the gate: ${UI[2]}"
[ "$(field "${UI[3]}" tool)" = Select ] && [ "$(field "${UI[3]}" walls)" -eq 11 ] || fail "no wall laid: ${UI[3]}"
[ "$(field "${UI[4]}" walls)" -eq 11 ] && [ "$(field "${UI[4]}" gates)" -eq 1 ] || fail "the wall or gate is gone: ${UI[4]}"
[ "$(field "${UI[4]}" sites)" -eq 0 ] || fail "the wall is not finished: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI wall screenshot test OK: $OUT"
