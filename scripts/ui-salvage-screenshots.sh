#!/usr/bin/env bash
# Scripted-input screenshot test for salvage (Craft guilds), in test mode: you get every tech (Craft guilds among them)
# and goods; the test panel puts up a house of yours on open grass, it is selected and its Take down button pressed,
# three of your workers are put down beside it, selected and right-clicked onto it: they take it down, and it is gone.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-salvage-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/salvage}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI salvage test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: open grass south-east of the town hall (152..176 x 393..412). The house goes at (164, 398).
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
clickui test_goods_0
clickui test_tab_place
clickui test_cat_building
select test_item house
clickui test_place
clickworld 164,g,398
key escape
ticks 10
spawn worker 169,397
spawn worker 169,398
spawn worker 169,399
ticks 10
camera 165,399 250 40 18
wait 3
clickworld 165,g,399
wait 2
print
clickui salvage
ticks 2
wait 2
print
shot $OUT/ui-salvage-marked.png
clickworld 169,g,397
wait 2
clickworld 169,g,398 shift
wait 2
clickworld 169,g,399 shift
wait 2
clickworld 165,g,399 right
ticks 12
camera 165,399 250 40 18
wait 3
print
shot $OUT/ui-salvage-working.png
ticks 1500
wait 2
print
shot $OUT/ui-salvage-done.png
"
LOG="$OUT/ui-salvage.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" building)" != "none" ] || fail "the house was not selected: ${UI[0]}"
[ "$(field "${UI[1]}" salvage_marked)" -eq 1 ] || fail "the house was not marked: ${UI[1]}"
[ "$(field "${UI[2]}" salvaging)" -ge 1 ] || fail "nobody is taking the house down: ${UI[2]}"
[ "$(field "${UI[3]}" salvage_marked)" -eq 0 ] || fail "the house is still standing: ${UI[3]}"
[ "$(field "${UI[3]}" buildings)" -lt "$(field "${UI[0]}" buildings)" ] || fail "the house was not taken down: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI salvage screenshot test OK: $OUT"
