#!/usr/bin/env bash
# Scripted-input screenshot test for the Chasm spell: your army (--battle) is box-selected, the Chasm tool
# (Shift+Z) is drawn as a line under the enemy's ranks, and its three mages walk into reach and open the ground
# there together, three columns wide. The enemy soldiers standing on it fall in, and the ground closes and lifts
# them out when its time is up. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-chasm-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/chasm}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west of the map centre (the centre is the gloom forest): open ground. With --battle=16 your army stands in two ranks
# at x 253..254, z 409..423, the enemy's at x 266..267 (they stand their ground until something comes near).
# The chasm runs north to south at x 266 (three wide: x 265..267), under both enemy ranks; the mages walk a
# block or two to have it in reach.
STEPS="
wait 20
camera 260,416 300 50 20
wait 5
dragworld 248,g,406 257,g,427
wait 2
print
key shift+z
wait 2
mouseworld 266,g,412
down here
mouseworld 266,g,419
wait 3
print
shot $OUT/ui-chasm-drag.png
up here
ticks 80
print
camera 266,416 250 35 16
wait 5
shot $OUT/ui-chasm-open.png
ticks 640
print
camera 266,416 250 35 16
wait 5
shot $OUT/ui-chasm-closed.png
"
LOG="$OUT/ui-chasm.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --trees=1 --focus=260,416 --battle=16 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI chasm test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
# The army is selected, with mages among it that can open chasms.
[ "$(field "${UI[0]}" chasm_casters)" -ge 2 ] && [ "$(field "${UI[0]}" caster_ready)" -eq 1 ] || fail "no ready mages selected: ${UI[0]}"
# Dragging the tool previews the line's 8 columns, three wide for three mages; nothing has opened yet.
[ "$(field "${UI[1]}" tool)" = Chasm ] || fail "the Chasm tool is not on: ${UI[1]}"
[ "$(field "${UI[1]}" wall_preview)" -eq 24 ] || fail "the preview does not show the line: ${UI[1]}"
[ "$(field "${UI[1]}" chasms)" -eq 0 ] || fail "the ground opened before letting go: ${UI[1]}"
# Letting go opens the chasm, enemy soldiers fall in, and the tool is put away.
[ "$(field "${UI[2]}" chasms)" -eq 1 ] && [ "$(field "${UI[2]}" chasm_open)" -ge 60 ] || fail "the chasm did not open: ${UI[2]}"
[ "$(field "${UI[2]}" in_chasm)" -ge 4 ] || fail "nobody fell in: ${UI[2]}"
[ "$(field "${UI[2]}" tool)" = Select ] || fail "the tool stayed on: ${UI[2]}"
# The ground closes when its time is up.
[ "$(field "${UI[3]}" chasms)" -eq 0 ] && [ "$(field "${UI[3]}" in_chasm)" -eq 0 ] || fail "the chasm did not close: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI chasm screenshot test OK: $OUT"
