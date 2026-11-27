#!/usr/bin/env bash
# Scripted-input screenshot test for the Dwarves, the mountain clans. First the start screen: the Dwarves are picked
# (five factions to choose from), and the match starts with their own units by your town hall in the west: workers,
# ironshields, crossbowmen and runesmiths, no spearmen, riders, archers or mages. Then, in test mode as the Dwarves: a
# stretch of stonework is laid, ironshields, crossbowmen and runesmiths stand by it, and a Settler spearman comes at
# it; the wall is selected (dwarf stonework). Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-dwarves-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/dwarves}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI Dwarves test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234: the west start (player 0) has its town hall at 128..134 x 381..387.
STEPS="
wait 10
clickui start_dwarves
wait 3
print
shot $OUT/ui-dwarves-start-screen.png
clickui start_button
waitmatch
wait 10
camera 131,391 25 40 13
wait 5
print
shot $OUT/ui-dwarves-start.png
"
LOG="$OUT/ui-dwarves.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --menu --seed=1234 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" faction)" = dwarves ] || fail "the Dwarves were not picked: ${UI[0]}"
[ "$(field "${UI[1]}" screen)" = match ] || fail "the match did not start: ${UI[1]}"
[ "$(field "${UI[1]}" faction)" = dwarves ] || fail "you are not playing the Dwarves: ${UI[1]}"

# Test mode as the Dwarves. Open grass south-east of the town hall (152..176 x 393..412): stonework along z = 400,
# the Dwarves' soldiers behind it, a Settler spearman in front coming at it.
STEPS="
wait 20
key plus*3
clickui test_tab_place
clickui test_cat_building
select test_item dwarf_stonework
clickui test_place
clickworld 156,g,400
clickworld 157,g,400
clickworld 158,g,400
clickworld 159,g,400
clickworld 160,g,400
clickworld 161,g,400
clickworld 162,g,400
clickworld 163,g,400
clickworld 164,g,400
key escape
spawn ironshield 158,396 4
spawn crossbowman 161,396 4
spawn runesmith 164,396 2
spawn spearman 160,404 1 1
ticks 4
clickworld 159,g,400
wait 2
print
camera 160,399 22 38 14
wait 5
shot $OUT/ui-dwarves-stonework.png
camera 160,397 10 30 12
wait 5
shot $OUT/ui-dwarves-close.png
"
LOG2="$OUT/ui-dwarves-test.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=dwarves,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG2"

! grep -q '^Input script' "$LOG2" || fail "a scripted step failed: $(grep '^Input script' "$LOG2")"
mapfile -t UI < <(grep '^UI ' "$LOG2")
[ "${#UI[@]}" -eq 1 ] || fail "expected 1 UI line, got ${#UI[@]}"
[ "$(field "${UI[0]}" building)" = stonework ] || fail "the stonework is not selected: ${UI[0]}"
[ "$(grep -c '^Saved screenshot' "$LOG" "$LOG2" | awk -F: '{s+=$2} END {print s}')" -eq 4 ] || fail "missing screenshots"
echo "UI Dwarves screenshot test OK: $OUT"
