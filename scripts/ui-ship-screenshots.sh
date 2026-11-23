#!/usr/bin/env bash
# Scripted-input screenshot test for ships, in test mode: you get goods and instant training; the test panel puts up a
# dock of yours on the west bank of the deep river, which is selected and builds a ship from its panel; four spearmen
# are put down by the bank, box-selected and sent aboard with a right-click on the ship; the ship is selected and
# right-clicked on the far bank, sails across and puts them ashore there. Saves PNGs to OUT_DIR and fails if the UI
# state is not as expected.
# Usage: scripts/ui-ship-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ships}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI ship test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234: north of the shadowed wilds the deep river runs between low banks; at z = 270 it is open water from
# x = 403 to 421 with land beyond it (421..425) before a side channel. The dock goes up on the west bank (its
# footprint's corner at 398,270); the ship is launched onto the river beside it.
STEPS="
wait 20
camera 408,272 24 50 30
wait 5
print
clickui test_tab_world
clickui test_goods_0
clickui test_tab_cheats
clickui test_cheat_instanttrain_0
clickui test_tab_place
clickui test_cat_building
select test_item dock
clickui test_place
clickworld 400,g,272
key escape
ticks 10
print
clickworld 400,g,272
wait 2
clickui train_ship
ticks 60
print
clickui test_cat_unit
select test_item spearman
spin test_size 4
clickui test_place
clickworld 395,g,278
key escape
ticks 10
dragworld 391,g,274 399,g,283
wait 2
print
shot $OUT/ui-ship-dock.png
clickworld 403,g,271 right
ticks 200
print
camera 405,272 14 50 25
wait 3
shot $OUT/ui-ship-boarded.png
clickworld 403,g,271
wait 2
print
clickworld 423,g,272 right
ticks 80
camera 412,272 16 50 25
wait 3
shot $OUT/ui-ship-sailing.png
ticks 400
print
camera 418,272 16 50 25
wait 3
shot $OUT/ui-ship-ashore.png
"
LOG="$OUT/ui-ship.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 7 ] || fail "expected 7 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" ships)" -eq 0 ] || fail "nobody starts with a ship: ${UI[0]}"
[ "$(field "${UI[1]}" docks)" -eq 1 ] || fail "no dock: ${UI[1]}"
[ "$(field "${UI[2]}" ships)" -eq 1 ] || fail "the dock did not build a ship: ${UI[2]}"
[ "$(field "${UI[3]}" selected)" -eq 4 ] || fail "the spearmen are not selected: ${UI[3]}"
[ "$(field "${UI[4]}" aboard)" -eq 4 ] || fail "the spearmen did not board the ship: ${UI[4]}"
[ "$(field "${UI[5]}" selected)" -eq 1 ] && [ "$(field "${UI[5]}" unit)" = ship ] || fail "the ship is not selected: ${UI[5]}"
[ "$(field "${UI[6]}" aboard)" -eq 0 ] || fail "the spearmen were not put ashore: ${UI[6]}"
AT="$(field "${UI[6]}" ship_at)"
[ "${AT%%,*}" -gt 410 ] || fail "the ship did not sail across: ${UI[6]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 4 ] || fail "missing screenshots"
echo "UI ship screenshot test OK: $OUT"
