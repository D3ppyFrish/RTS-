#!/usr/bin/env bash
# Scripted-input screenshot test for siege ladders: the starting units are sent north to the foot of a cliff 6
# levels high, and Shift+K places a ladder against its face. The workers raise it a rung at a time, and then the
# units climb it onto the cliff top (a close look at the ladder against the face on the way). Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-ladder-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ladders}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. North of
# it a cliff face runs along x = 103 to 104 from z = 108 to 144; at z = 122 to 124 the ground at its foot (x = 103) is at
# level 83 and its top (x = 104) at 89: a ladder of 6 rungs, placed by dragging from its foot east onto the top.
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
clickworld 97,g,124 right
key plus*3
ticks 2400
key minus*3
camera 100,124 20 32 26
wait 5
key shift+k
wait 2
mouseworld 103,g,122
down here
mouseworld 107,g,122
wait 3
print
shot $OUT/ui-ladder-drag.png
up here
ticks 4
print
ticks 90
wait 5
shot $OUT/ui-ladder-raising.png
key plus*3
ticks 600
wait 5
print
shot $OUT/ui-ladder-up.png
camera 103,122 110 25 9
wait 5
shot $OUT/ui-ladder-close.png
camera 100,124 20 32 26
wait 5
clickworld 108,g,122 right
ticks 900
wait 5
print
shot $OUT/ui-ladder-climbed.png
"
LOG="$OUT/ui-ladder.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI ladder test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" own)" -gt 8 ] || fail "the starting units are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Ladder ] && [ "$(field "${UI[1]}" ladder_top)" -gt 0 ] && [ "$(field "${UI[1]}" ladder_rungs)" -ge 2 ] \
  && [ "$(field "${UI[1]}" wall_preview)" -eq 2 ] || fail "the ladder being placed does not reach the cliff top: ${UI[1]}"
[ "$(field "${UI[2]}" tool)" = Select ] && [ "$(field "${UI[2]}" ladders)" -eq 1 ] || fail "nobody is raising the ladder: ${UI[2]}"
[ "$(field "${UI[3]}" ladders)" -eq 0 ] && [ "$(field "${UI[3]}" ladders_up)" -eq 1 ] || fail "the ladder is not up: ${UI[3]}"
[ "$(field "${UI[3]}" wood)" -lt "$(field "${UI[2]}" wood)" ] || fail "the ladder cost no wood: ${UI[2]} / ${UI[3]}"
[ "$(field "${UI[4]}" ladders_up)" -eq 1 ] || fail "the ladder fell: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 5 ] || fail "missing screenshots"
echo "UI ladder screenshot test OK: $OUT"
