#!/usr/bin/env bash
# Scripted-input screenshot test for town governors: the town hall is selected and given a Defence governor
# (its Defence button). With no other orders, the governor makes the starting workers automatic, trains
# more, puts up a barracks, researches archery and puts up a watchtower at each corner of the town. Then
# Shift+U steps the governor on to off. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-governor-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/governor}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 84..90 x 253..259, starting units in rows just south of it. --goods=600
# pays for the barracks, the towers and the soldiers without waiting on the gatherers.
STEPS="
wait 20
camera 87,256 46 55 34
wait 5
clickworld 87,g,256
wait 3
clickui governor_defence
mouse 800,200
ticks 2
print
shot $OUT/ui-governor-on.png
key plus*2
ticks 2400
print
shot $OUT/ui-governor-building.png
ticks 6000
print
shot $OUT/ui-governor-towers.png
key shift+u
ticks 2
print
"
LOG="$OUT/ui-governor.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --goods=600 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI governor test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" building)" = town_hall ] || fail "the town hall is not selected: ${UI[0]}"
[ "$(field "${UI[0]}" governor)" = defence ] || fail "the governor is not set: ${UI[0]}"
[ "$(field "${UI[0]}" idle)" -ge 1 ] && [ "$(field "${UI[1]}" idle)" -eq 0 ] || fail "the idle workers were not put to work: ${UI[0]} ${UI[1]}"
[ "$(field "${UI[1]}" barracks)" -eq 1 ] || fail "no barracks: ${UI[1]}"
[ "$(field "${UI[2]}" towers)" -eq 4 ] || fail "expected four watchtowers: ${UI[2]}"
[ "$(field "${UI[2]}" techs)" -ge 2 ] || fail "farming and archery were not researched: ${UI[2]}"
[ "$(field "${UI[3]}" governor)" = none ] || fail "Shift+U did not step the governor to off: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI governor screenshot test OK: $OUT"
