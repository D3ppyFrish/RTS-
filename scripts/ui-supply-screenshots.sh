#!/usr/bin/env bash
# Scripted-input screenshot test for supply rules: the starting workers build a storehouse a little way from
# the town hall; it is selected and told to keep 50 stone (two clicks on its Stone button) and 25 wood (one
# click); every worker is made automatic (Shift+J), and workers on supply duty carry the stone and wood over
# from the town hall until the storehouse holds what it should. Saves PNGs to OUT_DIR and fails if the UI
# state is not as expected.
# Usage: scripts/ui-supply-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/supply}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 84..90 x 253..259, starting units in rows just south of it, open
# ground for a storehouse to the south-east at 96..100 x 266..270 (its roof is at 1100,600 on screen from this
# camera). --goods=200 pays for it and fills the
# town hall with stone and wood to send over.
STEPS="
wait 20
camera 92,262 40 55 26
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
key g
clickworld 98,g,268
ticks 4
print
key plus*2
ticks 700
click 1100,600
wait 3
clickui supply_stone
clickui supply_stone
clickui supply_wood
mouse 800,200
ticks 2
print
shot $OUT/ui-supply-rules.png
key shift+j
ticks 260
print
shot $OUT/ui-supply-hauling.png
ticks 1800
print
shot $OUT/ui-supply-stocked.png
"
LOG="$OUT/ui-supply.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --goods=200 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI supply test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" sites)" -eq 1 ] || fail "no storehouse site placed: ${UI[0]}"
[ "$(field "${UI[1]}" building)" = storehouse ] || fail "the storehouse is not selected: ${UI[1]}"
[ "$(field "${UI[1]}" supply)" = "Wood:0/25,Stone:0/50" ] || fail "the supply rules are not set: ${UI[1]}"
[ "$(field "${UI[2]}" on_supply)" -ge 1 ] || fail "nobody went on supply duty: ${UI[2]}"
# Stone comes only by supply; wood cutters also drop their wood at the new storehouse, so it may hold more.
[[ "$(field "${UI[3]}" supply)" =~ ^Wood:([0-9]+)/25,Stone:50/50$ ]] && [ "${BASH_REMATCH[1]}" -ge 25 ] \
  || fail "the storehouse was not stocked: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI supply screenshot test OK: $OUT"
