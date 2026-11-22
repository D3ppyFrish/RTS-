#!/usr/bin/env bash
# Scripted-input screenshot test for battering rams, in test mode: you get every tech, goods and instant training;
# the test panel puts up a siege workshop of yours, which is selected and trains a battering ram from its panel; then
# an Orc stone gate with Orc archers behind it is put down, two more rams of yours are put down, box-selected and sent
# at the gate, which they break in a few blows while the archers' arrows hardly hurt them. Saves PNGs to OUT_DIR and fails if the UI state is
# not as expected.
# Usage: scripts/ui-ram-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ram}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI ram test failed: $1" >&2; exit 1; }
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
select test_item siege_workshop
clickui test_place
clickworld 156,g,398
key escape
ticks 10
print
clickworld 156,g,398
wait 2
clickui train_battering_ram
ticks 60
print
camera 157,401 25 45 20
wait 5
shot $OUT/ui-ram-workshop.png
camera 176,401 25 45 22
wait 3
clickui test_cat_building
select test_owner orcs
select test_item stone_gate
clickui test_place
clickworld 172,g,400
clickui test_cat_unit
select test_item archer
spin test_size 3
clickui test_place
clickworld 175,g,400
select test_owner settlers
select test_item battering_ram
spin test_size 1
clickui test_place
clickworld 166,g,398
clickworld 166,g,401
key escape
ticks 10
print
dragworld 162,g,394 169,g,405
wait 2
clickworld 172,g,400 right
ticks 30
print
camera 172,401 25 40 18
wait 3
shot $OUT/ui-ram-battering.png
ticks 200
print
shot $OUT/ui-ram-after.png
"
LOG="$OUT/ui-ram.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 6 ] || fail "expected 6 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" rams)" -eq 0 ] || fail "nobody starts with a ram: ${UI[0]}"
[ "$(field "${UI[1]}" siege_workshops)" -eq 1 ] || fail "no siege workshop: ${UI[1]}"
[ "$(field "${UI[2]}" rams)" -eq 1 ] || fail "the siege workshop did not build a ram: ${UI[2]}"
[ "$(field "${UI[3]}" rams)" -eq 3 ] || fail "two more rams not put down: ${UI[3]}"
ENEMY="$(field "${UI[3]}" enemy_buildings)"
[ "$(field "${UI[3]}" enemies)" -gt "$(field "${UI[2]}" enemies)" ] || fail "the Orc archers were not put down: ${UI[3]}"
[ "$(field "${UI[4]}" selected)" -ge 2 ] && [ "$(field "${UI[4]}" fighting)" -ge 1 ] || fail "no rams selected and sent at the gate: ${UI[4]}"
[ "$(field "${UI[5]}" enemy_buildings)" -lt "$ENEMY" ] || fail "the rams did not break the gate: ${UI[5]} (was $ENEMY)"
[ "$(field "${UI[5]}" rams)" -eq 3 ] || fail "the archers' arrows wrecked a ram while it broke the gate: ${UI[5]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI ram screenshot test OK: $OUT"
