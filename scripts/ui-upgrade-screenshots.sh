#!/usr/bin/env bash
# Scripted-input screenshot test for building upgrades: every worker is made automatic (Shift+J), the house
# north of the town hall is selected and upgraded (U) to a large house, which grows a column out on each side
# once the workers are done; then the town hall is selected and upgraded (Ctrl+U, since U trains there) to a
# great hall. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-upgrade-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/upgrades}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387; --houses=1 puts a house just north of it (129..132 x
# 372..375). --goods=600 pays for both upgrades.
# The army (F2) first clears the wolf pack whose den (171,384) is by the stone the workers dig.
# The first, smaller trees (--trees=1): the steps click at places the bigger trees now stand on.
STEPS="
wait 20
camera 131,380 34 50 32
wait 5
key f2
clickworld 168,g,385 right ctrl
wait 2
key shift+j
clickworld 130,g,373
wait 3
mouse 800,200
print
shot $OUT/ui-upgrade-house.png
key u
ticks 2
print
shot $OUT/ui-upgrade-started.png
key plus*2
# Few workers build on the 768 map at first (the rest are out gathering), so give the house time.
ticks 1800
print
shot $OUT/ui-upgrade-large-house.png
clickworld 131,g,384
wait 3
key ctrl+u
ticks 2
print
ticks 3000
mouse 800,200
print
shot $OUT/ui-upgrade-great-hall.png
"
LOG="$OUT/ui-upgrade.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --trees=1 --houses=1 --goods=600 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI upgrade test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" building)" = house ] || fail "the house is not selected: ${UI[0]}"
[ "$(field "${UI[1]}" upgrading)" -eq 1 ] || fail "U did not start the house's upgrade: ${UI[1]}"
[ "$(field "${UI[2]}" building)" = large_house ] && [ "$(field "${UI[2]}" upgrading)" -eq 0 ] || fail "the house did not become a large house: ${UI[2]}"
[ "$(field "${UI[3]}" building)" = town_hall ] && [ "$(field "${UI[3]}" upgrading)" -eq 1 ] || fail "Ctrl+U did not start the town hall's upgrade: ${UI[3]}"
[ "$(field "${UI[4]}" building)" = great_hall ] || fail "the town hall did not become a great hall: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 4 ] || fail "missing screenshots"
echo "UI upgrade screenshot test OK: $OUT"
