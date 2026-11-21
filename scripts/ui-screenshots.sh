#!/usr/bin/env bash
# Scripted-input screenshot test for the HUD and controls: box selection, a move order, the paused state
# and the height slice cutting a hill. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# The west start (seed 1234) has its 24 units in rows just south of the start; the cavern is under the hills.
STEPS="
wait 20
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
shot $OUT/ui-select-box.png
up 1080,640
wait 2
print
click 1150,330 right
wait 8
key space
wait 2
print
shot $OUT/ui-paused.png
key space
camera 347,625 30 50 48
wait 5
shot $OUT/ui-slice-off.png
key pagedown*14
wait 5
shot $OUT/ui-slice-hill.png
key pagedown*11
wait 5
print
shot $OUT/ui-slice-cavern.png
"
LOG="$OUT/ui.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI test failed: $1" >&2; exit 1; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[[ "${UI[0]}" =~ own=([0-9]+) ]] && [ "${BASH_REMATCH[1]}" -ge 20 ] || fail "box selection picked too few units: ${UI[0]}"
[[ "${UI[1]}" == *"paused=1"* ]] || fail "not paused: ${UI[1]}"
# The slice starts at the camera's ground on the hill over the cavern (level 39) and goes down 25: 14.
[[ "${UI[2]}" == *"slice=78"* ]] || fail "slice not at 78 (14 above the deep rock): ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 5 ] || fail "missing screenshots"
echo "UI screenshot test OK: $OUT"
