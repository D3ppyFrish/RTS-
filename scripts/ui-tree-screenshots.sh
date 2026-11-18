#!/usr/bin/env bash
# Screenshot test for trees: the start's grove close up, the kinds of tree in a forest, the swamp's willows
# and giant mushrooms and the dusk tree in the hills, then the starting units sent to it and the game
# paused as the first trees come down and the workers clear the logs they leave, from two sides. Saves
# PNGs to OUT_DIR and fails if no wood came in.
# Usage: scripts/ui-tree-screenshots.sh [OUT_DIR]
# CI: skip (close-up look checks, run by hand when the looks change)
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/trees}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 84..90 x 253..259, starting units in rows just south of it, and the
# start's grove of eight trees at 97..115 x 236..244 (birches at 103 x 236 and 242, a pine at 115 x 236).
# Further off: a forest of all three kinds round 140 x 200, willows and mushrooms round 221 x 148 in the
# swamp, and the dusk tree at 281 x 385 in the hills.
STEPS="
wait 20
camera 106,241 20 30 16
wait 10
shot $OUT/trees-grove.png
camera 140,200 30 30 26
wait 10
shot $OUT/trees-forest.png
camera 222,150 120 35 14
wait 10
shot $OUT/trees-swamp.png
camera 281,385 30 30 24
wait 10
shot $OUT/trees-dusk.png
camera 87,261 45 55 22
wait 10
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
clickworld 103,20,242 right
ticks 250
key space
print
camera 105,241 200 35 14
wait 10
shot $OUT/trees-felled.png
camera 106,241 20 30 16
wait 10
shot $OUT/trees-felled-2.png
key space
ticks 1600
print
"
LOG="$OUT/ui-trees.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI tree test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
wood0=$(field "${UI[0]}" wood); wood2=$(field "${UI[2]}" wood)
[ "$(field "${UI[1]}" paused)" -eq 1 ] || fail "the game did not pause: ${UI[1]}"
[ -n "$wood0" ] && [ -n "$wood2" ] && [ "$wood2" -gt "$wood0" ] || fail "no wood came in: ${UI[0]} / ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 6 ] || fail "missing screenshots"
echo "UI tree screenshot test OK: $OUT"
