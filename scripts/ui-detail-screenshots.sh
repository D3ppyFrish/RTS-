#!/usr/bin/env bash
# Look check for the detail prototype (game/scripts/Detail): close-ups of the detailed Settlers units with
# detail on and off (F7), a battle with detailed units walking and fighting, and a detailed castle near
# and far. Saves PNGs to OUT_DIR.
# Usage: scripts/ui-detail-screenshots.sh [OUT_DIR]
# CI: skip (close-up look checks, run by hand when the looks change)
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/detail}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true
run() { xvfb-run -a -s "-screen 0 1600x900x24" godot --path game --rendering-driver opengl3 -- --world=512x64x512 "$@" 2>&1 \
  | grep -E "^(Saved|Screenshot|Input script|Keeps|Detailed)" | tee -a "$OUT/ui-detail.log"; }
: > "$OUT/ui-detail.log"

# Seed 1234, --lineup at 100,276: the Settlers' worker, spearman and archer stand at x 64, 67 and 70, z 267.
run --lineup --focus=100,276 --input="
wait 20
camera 67,268 25 22 5
wait 5
shot $OUT/units-detail.png
key f7
wait 3
shot $OUT/units-plain.png
key f7
camera 67,268 200 22 5
wait 3
shot $OUT/units-back.png
"

# The combat test's battle at the map centre: your army attack-moves into the enemy's.
run --focus=248,218 --battle=16 --input="
wait 20
camera 248,218 0 55 22
wait 5
dragworld 236,g,208 245,g,229
wait 2
clickworld 262,g,222 right ctrl
ticks 15
camera 246,216 300 30 8
wait 3
shot $OUT/units-walk.png
camera 248,218 0 55 22
ticks 25
camera 250,218 330 30 9
wait 3
shot $OUT/units-fight.png
key f7
wait 1
shot $OUT/units-fight-plain.png
"

# A castle next to the west start (its corner at 77,22,223), close and far.
run --focus=start0 --keeps=1 --input="
wait 20
camera 82,229 20 35 18
wait 5
shot $OUT/keep-front.png
camera 82,229 200 50 20
wait 5
shot $OUT/keep-back.png
camera 82,240 20 25 9
wait 5
shot $OUT/keep-gate.png
camera 82,229 60 60 45
wait 5
shot $OUT/keep-far.png
"

fail() { echo "UI detail test failed: $1" >&2; exit 1; }
! grep -q '^Input script' "$OUT/ui-detail.log" || fail "a scripted step failed: $(grep '^Input script' "$OUT/ui-detail.log")"
[ "$(grep -c '^Saved screenshot' "$OUT/ui-detail.log")" -eq 10 ] || fail "missing screenshots"
echo "UI detail screenshots OK: $OUT"
