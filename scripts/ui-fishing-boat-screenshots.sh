#!/usr/bin/env bash
# Scripted-input screenshot test for fishing boats, in test mode: you get goods, instant training and units that
# cannot be hurt (wolves swim this river); the test panel puts up a house, for room, and a dock of yours on the west
# bank of the deep river, which is selected and builds two fishing boats from its panel. They sail out onto open water
# on their own, fish there and bring the catch home to the dock; both are box-selected, sent off up the river (which
# stops their fishing) and sent out again with the Go fishing button. Saves PNGs to OUT_DIR and fails if the UI state
# is not as expected.
# Usage: scripts/ui-fishing-boat-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/fishing-boats}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI fishing boat test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234: north of the shadowed wilds the deep river runs between low banks; at z = 270 it is open water from
# x = 403 to 421. The dock goes up on the west bank (its footprint's corner at 398,270), as in ui-ship-screenshots.sh.
STEPS="
wait 20
camera 408,272 24 50 30
wait 5
print
clickui test_tab_world
clickui test_goods_0
clickui test_tab_cheats
clickui test_cheat_instanttrain_0
clickui test_cheat_invulnerable_0
clickui test_tab_place
clickui test_cat_building
select test_item house
clickui test_place
clickworld 392,g,281
key escape
ticks 5
select test_item dock
clickui test_place
clickworld 400,g,272
key escape
ticks 10
clickworld 400,g,272
wait 2
clickui train_fishing_boat
ticks 40
clickui train_fishing_boat
ticks 40
print
ticks 300
camera 407,272 13 50 30
wait 3
print
shot $OUT/ui-fishing-boats-fishing.png
ticks 700
print
key escape
dragworld 402,g,250 424,g,295
wait 2
print
shot $OUT/ui-fishing-boat-selected.png
clickworld 412,g,240 right
ticks 20
print
clickui go_fishing
ticks 20
print
"
LOG="$OUT/ui-fishing-boat.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 7 ] || fail "expected 7 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" fishing_boats)" -eq 0 ] || fail "nobody starts with a fishing boat: ${UI[0]}"
[ "$(field "${UI[1]}" fishing_boats)" -eq 2 ] || fail "the dock did not build two fishing boats: ${UI[1]}"
[ "$(field "${UI[2]}" boats_fishing)" -ge 1 ] || fail "no boat is fishing: ${UI[2]}"
CAUGHT0="$(field "${UI[2]}" food_caught)"
[ "$(field "${UI[3]}" food_caught)" -gt "$CAUGHT0" ] || fail "no catch came in: ${UI[3]}"
[ "$(field "${UI[4]}" selected)" -eq 2 ] && [ "$(field "${UI[4]}" unit)" = fishing_boat ] || fail "the boats are not selected: ${UI[4]}"
[ "$(field "${UI[5]}" boats_fishing)" -eq 0 ] || fail "a move order did not stop the fishing: ${UI[5]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI fishing boat screenshot test OK: $OUT"
