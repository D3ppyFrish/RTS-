#!/usr/bin/env bash
# Scripted-input screenshot test for murder holes (Masonry, issue #147), in test mode: you get every tech (Masonry
# among them); the test panel puts up a short stone wall of yours running north and south with a stone gate in the
# middle, two of your archers are put down behind it, box-selected and right-clicked onto the wall beside the gate,
# so the gate is held; then Orc spearmen are put down in front of the gate and come at it, and stones drop on them
# through the gate's roof. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-murderholes-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/murderholes}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI murder holes test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: open grass south-east of the town hall (152..176 x 393..412). The wall runs along z at
# x = 166 from z = 396 to 404, the gate at (166, 400); the archers stand east of it, the Orcs come from the west.
# The sim is paused the moment the first stone drops, so the shot shows it falling among the Orcs at the gate.
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
clickui test_tab_place
clickui test_cat_building
select test_item stone_gate
clickui test_place
clickworld 166,g,400
key escape
select test_item stone_wall
clickui test_place
clickworld 166,g,396
clickui test_place
clickworld 166,g,397
clickui test_place
clickworld 166,g,398
clickui test_place
clickworld 166,g,399
clickui test_place
clickworld 166,g,401
clickui test_place
clickworld 166,g,402
clickui test_place
clickworld 166,g,403
clickui test_place
clickworld 166,g,404
key escape
spawn archer 170,398
spawn archer 170,402
ticks 10
camera 166,400 250 45 24
wait 3
print
dragworld 167,g,394 174,g,406
wait 2
clickworld 166,g,399 right
ticks 120
print
spawn spearman 158,399 3 1
spawn spearman 158,401 2 1
key f1
camera 164,400 90 35 14
untilstones 1
key space
wait 3
shot $OUT/ui-murderholes-falling.png
key space
ticks 200
print
camera 164,400 90 35 14
wait 3
shot $OUT/ui-murderholes-after.png
"
LOG="$OUT/ui-murderholes.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" gates)" -ge 1 ] && [ "$(field "${UI[0]}" walls)" -ge 8 ] || fail "the wall and gate were not put up: ${UI[0]}"
[ "$(field "${UI[0]}" stones_dropped)" -eq 0 ] || fail "stones dropped before anyone came: ${UI[0]}"
[ "$(field "${UI[1]}" posted)" -ge 1 ] || fail "the archers are not up on the wall: ${UI[1]}"
[ "$(field "${UI[2]}" stones_dropped)" -ge 2 ] || fail "the held gate dropped no stones on the Orcs: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI murder holes screenshot test OK: $OUT"
