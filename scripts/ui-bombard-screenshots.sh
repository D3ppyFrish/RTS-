#!/usr/bin/env bash
# Scripted-input screenshot test for the bombard ship, in test mode: you get every tech, goods and instant training;
# your units are invulnerable (the river's banks have wild things on them); the test panel puts up a dock of yours on the west bank of the deep river, which is selected and builds a bombard
# ship from its panel; three Orc spearmen are put down on the far bank; the ship is selected and right-clicked on one,
# sails into range and shells them, blowing craters in the bank. Saves PNGs to OUT_DIR and fails if the UI state is
# not as expected.
# Usage: scripts/ui-bombard-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/bombard}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI bombard test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234: as in ui-ship-screenshots.sh, the deep river at z = 270 is open water from x = 403 to 421 with land
# beyond it (421..425). The dock goes up on the west bank (its corner at 398,270), the spearmen on the far bank.
STEPS="
wait 20
camera 408,272 24 50 30
wait 5
print
clickui test_tab_world
clickui test_techs_0
clickui test_goods_0
clickui test_tab_cheats
clickui test_cheat_instanttrain_0
clickui test_cheat_invulnerable_0
clickui test_tab_place
clickui test_cat_building
select test_item dock
clickui test_place
clickworld 400,g,272
key escape
ticks 10
clickworld 400,g,272
wait 2
clickui train_bombard_ship
ticks 60
print
clickui test_cat_unit
select test_owner orcs
select test_item spearman
spin test_size 3
clickui test_place
clickworld 423,g,266
key escape
ticks 10
camera 408,270 30 50 30
wait 3
shot $OUT/ui-bombard-launched.png
clickworld 403,g,271
wait 2
print
clickworld 423,g,266 right
ticks 300
print
camera 414,268 18 50 25
wait 3
shot $OUT/ui-bombard-firing.png
key plus*2
ticks 1600
key minus*2
print
camera 421,266 14 55 20
wait 3
shot $OUT/ui-bombard-craters.png
"
LOG="$OUT/ui-bombard.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" ships)" -eq 0 ] || fail "nobody starts with a ship: ${UI[0]}"
[ "$(field "${UI[1]}" ships)" -eq 1 ] || fail "the dock did not build a bombard ship: ${UI[1]}"
[ "$(field "${UI[2]}" selected)" -eq 1 ] && [ "$(field "${UI[2]}" unit)" = bombard_ship ] || fail "the bombard ship is not selected: ${UI[2]}"
[ "$(field "${UI[4]}" craters)" -ge 1 ] || fail "the bombard ship's shells left no craters: ${UI[4]}"
[ "$(field "${UI[4]}" enemies)" -lt "$(field "${UI[2]}" enemies)" ] || fail "the bombard ship killed nobody: ${UI[4]} (was ${UI[2]})"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI bombard screenshot test OK: $OUT"
