#!/usr/bin/env bash
# Scripted-input screenshot test for the dig tool (issue #40): the starting units are selected, Z picks the
# dig tool, ] makes it dig 3 deep, and an area is dragged out by the start. Its workers and sappers dig it
# out a layer at a time; the workers carry what they dig to the town hall. Saves PNGs to OUT_DIR and fails
# if the UI state is not as expected.
# Usage: scripts/ui-dig-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 84..90 x 253..259, starting units in rows just south of it, and open
# ground at 92..103 x 262..269.
STEPS="
wait 20
camera 87,261 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
key z
key ]
wait 2
print
mouseworld 92,g,262
down here
mouseworld 103,g,269
wait 3
print
shot $OUT/ui-dig-drag.png
up here
ticks 4
print
key plus*2
ticks 300
camera 97,266 30 50 18
wait 5
print
shot $OUT/ui-dig-working.png
ticks 3000
print
shot $OUT/ui-dig-done.png
"
LOG="$OUT/ui-dig.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI dig test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 6 ] || fail "expected 6 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" own)" -gt 8 ] || fail "the starting units are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Dig ] && [ "$(field "${UI[1]}" dig_depth)" -eq 3 ] || fail "no dig tool 3 deep: ${UI[1]}"
[ "$(field "${UI[2]}" dig_preview)" -eq 96 ] || fail "the area being dragged out is not 12 x 8: ${UI[2]}"
[ "$(field "${UI[3]}" tool)" = Select ] && [ "$(field "${UI[3]}" dig_areas)" -eq 1 ] || fail "no dig area marked out: ${UI[3]}"
[ "$(field "${UI[3]}" digging)" -eq 8 ] || fail "the 4 workers and 4 sappers are not digging: ${UI[3]}"
[ "$(field "${UI[4]}" digging)" -gt 0 ] || fail "nobody is digging any more: ${UI[4]}"
[ "$(field "${UI[5]}" dig_areas)" -eq 0 ] && [ "$(field "${UI[5]}" digging)" -eq 0 ] || fail "the area is not dug out: ${UI[5]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI dig screenshot test OK: $OUT"
