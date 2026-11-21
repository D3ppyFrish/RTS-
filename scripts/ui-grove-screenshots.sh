#!/usr/bin/env bash
# Scripted-input screenshot test for the Grow Forest spell: your army (--battle) is box-selected, the Grow Forest
# tool (Shift+L) is drawn as a line between the two armies, and its mages grow trees along it together: three rows,
# the side rows' trees on the middle row's gaps. The trees stay. Saves PNGs to OUT_DIR and fails if the UI state is
# not as expected.
# Usage: scripts/ui-grove-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/grove}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west of the map centre (the centre is the gloom forest): open ground. With --battle=16 your army stands in two ranks
# at x 253..254, z 409..423, the enemy's at x 266..267 (they stand their ground until something comes near).
# The grove runs north to south at x 260, between them (rows at x 258, 260 and 262 for three mages).
STEPS="
wait 20
camera 260,416 300 50 20
wait 5
dragworld 248,g,406 257,g,427
wait 2
print
key shift+l
wait 2
mouseworld 260,g,412
down here
mouseworld 260,g,419
wait 3
print
shot $OUT/ui-grove-drag.png
up here
ticks 80
print
camera 260,416 250 35 18
wait 5
shot $OUT/ui-grove-grown.png
ticks 1200
print
camera 252,416 270 25 22
wait 5
shot $OUT/ui-grove-later.png
"
LOG="$OUT/ui-grove.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --trees=1 --focus=260,416 --battle=16 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI grove test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
# The army is selected, with mages among it that can grow forests.
[ "$(field "${UI[0]}" grove_casters)" -ge 2 ] && [ "$(field "${UI[0]}" caster_ready)" -eq 1 ] || fail "no ready mages selected: ${UI[0]}"
# Dragging the tool previews a tree on every other column of the line's 8, in three rows for three mages.
[ "$(field "${UI[1]}" tool)" = GrowForest ] || fail "the Grow Forest tool is not on: ${UI[1]}"
[ "$(field "${UI[1]}" wall_preview)" -eq 12 ] || fail "the preview does not show the grove: ${UI[1]}"
[ "$(field "${UI[1]}" grove_trees)" -eq 0 ] || fail "trees grew before letting go: ${UI[1]}"
# Letting go grows the trees, and the tool is put away.
[ "$(field "${UI[2]}" grove_trees)" -ge 8 ] || fail "the grove did not grow: ${UI[2]}"
[ "$(field "${UI[2]}" tool)" = Select ] || fail "the tool stayed on: ${UI[2]}"
# A minute later the trees are still there (nobody has felled them).
[ "$(field "${UI[3]}" grove_trees)" = "$(field "${UI[2]}" grove_trees)" ] || fail "the count changed: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI grove screenshot test OK: $OUT"
