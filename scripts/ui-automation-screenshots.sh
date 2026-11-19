#!/usr/bin/env bash
# Scripted-input screenshot test for automation: the starting units are selected, their workers made
# automatic (J) and their soldiers set to hold their ground (V); the town hall trains a worker on repeat
# (U, P); then every worker is made automatic (Shift+J), and since there is no room for the worker,
# automation puts up a house. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-automation-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, the starting units (four of each type, so four
# workers) in rows just south of it, which is more people than the hall houses.
STEPS="
wait 20
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
key j
key v
ticks 60
print
shot $OUT/ui-auto-workers.png
clickworld 131,g,384
key u
key p
ticks 4
print
shot $OUT/ui-repeat.png
key shift+j
key plus*2
ticks 900
camera 131,384 30 60 40
wait 5
print
shot $OUT/ui-auto-house.png
"
LOG="$OUT/ui-automation.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI automation test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" auto)" -eq 4 ] && [ "$(field "${UI[0]}" working)" -eq 4 ] || fail "the workers are not working on their own: ${UI[0]}"
[ "$(field "${UI[0]}" stance)" = Hold ] || fail "the soldiers are not holding their ground: ${UI[0]}"
[ "$(field "${UI[1]}" building)" = town_hall ] && [ "$(field "${UI[1]}" repeat)" -eq 1 ] && [ "$(field "${UI[1]}" queue)" -eq 1 ] \
  || fail "the town hall is not training on repeat: ${UI[1]}"
[ "$(field "${UI[2]}" auto_all)" -eq 1 ] || fail "not all workers are automatic: ${UI[2]}"
# A house went up and was finished. (Its 30 wood is not a sure sign: felled trees bring wood in fast
# enough to make it back by then.)
[ "$(field "${UI[2]}" buildings)" -ge 2 ] && [[ "$(field "${UI[2]}" alert_kinds)" == *BuildingFinished* ]] \
  || fail "automation did not put up a house: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI automation screenshot test OK: $OUT"
