#!/usr/bin/env bash
# Scripted-input screenshot test for siege workshops and catapults, in test mode: you get every tech, goods and
# instant training; the test panel puts up a siege workshop of yours, which is selected and trains a catapult from
# its panel; then two more catapults of yours are put down, box-selected and sent at an Orc barracks some 14 blocks off,
# which their stones knock down. Saves PNGs to OUT_DIR and fails if the UI state is not
# as expected.
# Usage: scripts/ui-siege-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/siege}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI siege test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: town hall at 128..134 x 381..387 and open grass south-east of it (152..176 x 393..412).
STEPS="
wait 20
camera 158,400 20 50 30
wait 5
print
clickui test_tab_world
clickui test_techs_0
clickui test_goods_0
clickui test_tab_cheats
clickui test_cheat_instanttrain_0
clickui test_tab_place
clickui test_cat_building
select test_item siege workshop
clickui test_place
clickworld 156,g,398
key escape
ticks 10
print
clickworld 156,g,398
wait 2
clickui train_catapult
ticks 60
print
camera 157,401 25 45 20
wait 5
shot $OUT/ui-siege-workshop.png
clickui test_cat_building
select test_owner orcs
select test_item barracks
clickui test_place
clickworld 175,g,398
clickui test_cat_unit
select test_item spearman
spin test_size 3
clickworld 172,g,406
select test_owner settlers
select test_item catapult
spin test_size 1
clickworld 161,g,403
clickworld 161,g,407
clickworld 150,g,410 right
ticks 10
print
dragworld 158,g,401 164,g,409
wait 2
clickworld 175,g,398 right
ticks 140
print
camera 168,403 350 40 26
wait 3
shot $OUT/ui-siege-firing.png
key plus*2
ticks 3200
print
shot $OUT/ui-siege-after.png
"
LOG="$OUT/ui-siege.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 6 ] || fail "expected 6 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" catapults)" -eq 0 ] || fail "nobody starts with a catapult: ${UI[0]}"
[ "$(field "${UI[1]}" siege_workshops)" -eq 1 ] || fail "no siege workshop: ${UI[1]}"
[ "$(field "${UI[2]}" catapults)" -eq 1 ] || fail "the siege workshop did not build a catapult: ${UI[2]}"
[ "$(field "${UI[3]}" catapults)" -eq 3 ] || fail "two more catapults not put down: ${UI[3]}"
ENEMY="$(field "${UI[3]}" enemy_buildings)"
[ "$(field "${UI[4]}" selected)" -ge 1 ] && [ "$(field "${UI[4]}" fighting)" -ge 1 ] || fail "no catapult selected and sent at the barracks: ${UI[4]}"
[ "$(field "${UI[5]}" enemy_buildings)" -lt "$ENEMY" ] || fail "the catapults did not bring the barracks down: ${UI[5]} (was $ENEMY)"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI siege screenshot test OK: $OUT"
