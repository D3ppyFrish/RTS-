#!/usr/bin/env bash
# Scripted-input screenshot test for the Ice Bridge: your army (--battle) stands on the west bank of the river,
# the enemy's on the east bank. The army is box-selected, the Ice Bridge tool (Shift+') is drawn from bank to
# bank and a mage freezes a level way of ice across. The army crosses it to fight, the ice cracks in its last
# seconds and then melts, dropping whoever is still on it into the river. Saves PNGs to OUT_DIR and fails if
# the UI state is not as expected.
# Usage: scripts/ui-ice-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ice}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234: at row z 230 the river runs from x 270 to 278, two blocks deep, between banks that step up to
# flat ground where units stand at level 22 (x 264..267 and x 281 on). With --battle=16 at x 273 your army
# stands at x 266..267, the enemy's at x 279..280 on the far bank's slope. The ice is drawn from x 265 to 283:
# 13 columns need it (x 268 to 280). The army's three mages freeze it together (group spells, issue #39):
# seven wide: 89 columns, where the ground is lower than the deck.
STEPS="
wait 20
camera 273,230 300 50 26
wait 5
dragworld 261,g,221 269,g,239
wait 2
print
key shift+apostrophe
wait 2
mouseworld 265,g,230
down here
mouseworld 283,g,230
wait 3
print
shot $OUT/ui-ice-drag.png
up here
ticks 4
print
wait 3
shot $OUT/ui-ice-frozen.png
clickworld 290,g,230 right
ticks 100
camera 273,230 230 40 22
wait 5
shot $OUT/ui-ice-crossing.png
ticks 360
print
camera 273,230 300 55 14
wait 5
shot $OUT/ui-ice-cracking.png
ticks 100
print
camera 273,230 300 50 26
wait 5
shot $OUT/ui-ice-melted.png
"
LOG="$OUT/ui-ice.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --focus=273,230 --battle=16 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI ice test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
# The army is selected, with mages among it that can freeze water, ready.
[ "$(field "${UI[0]}" own)" -ge 12 ] || fail "the army is not selected: ${UI[0]}"
[ "$(field "${UI[0]}" ice_casters)" -ge 2 ] && [ "$(field "${UI[0]}" caster_ready)" -eq 1 ] || fail "no ready mages selected: ${UI[0]}"
# Dragging the tool from bank to bank previews 13 columns of ice, seven wide; nothing is frozen yet.
[ "$(field "${UI[1]}" tool)" = IceBridge ] || fail "the Ice Bridge tool is not on: ${UI[1]}"
[ "$(field "${UI[1]}" wall_preview)" -eq 89 ] || fail "the preview does not span the river: ${UI[1]}"
[ "$(field "${UI[1]}" ice_bridges)" -eq 0 ] || fail "ice formed before letting go: ${UI[1]}"
# Letting go freezes the way across, and the tool is put away.
[ "$(field "${UI[2]}" ice_bridges)" -eq 1 ] && [ "$(field "${UI[2]}" ice_blocks)" -eq 89 ] || fail "the ice did not form: ${UI[2]}"
[ "$(field "${UI[2]}" tool)" = Select ] || fail "the tool stayed on: ${UI[2]}"
# In its last seconds it cracks, then it melts.
[ "$(field "${UI[3]}" ice_bridges)" -eq 1 ] && [ "$(field "${UI[3]}" ice_cracking)" -eq 1 ] || fail "the ice is not cracking: ${UI[3]}"
[ "$(field "${UI[4]}" ice_bridges)" -eq 0 ] && [ "$(field "${UI[4]}" ice_blocks)" -eq 0 ] || fail "the ice did not melt: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 5 ] || fail "missing screenshots"
echo "UI ice screenshot test OK: $OUT"
