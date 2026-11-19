#!/usr/bin/env bash
# Scripted-input screenshot test for guard orders and rally points: the starting units are box-selected and
# told to guard the town hall with Alt+right click; then the town hall is selected, a rally point is set
# with a right click on the ground, and a worker trained there walks out to the flag. Saves PNGs to OUT_DIR
# and fails if the UI state is not as expected.
# Usage: scripts/ui-guard-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/guard}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it, open
# ground further south. --houses gives room to train, --goods the food.
STEPS="
wait 20
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
clickworld 131,g,384 right alt
ticks 120
print
shot $OUT/ui-guard-hall.png
clickworld 131,g,384
wait 2
clickworld 124,g,398 right
ticks 2
print
key u
ticks 260
print
shot $OUT/ui-rally-flag.png
"
LOG="$OUT/ui-guard.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --houses=4 --goods=200 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI guard test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
own="$(field "${UI[0]}" own)"
[ "$own" -ge 8 ] || fail "the starting units are not selected: ${UI[0]}"
[ "$(field "${UI[0]}" guarding)" -eq 0 ] || fail "guarding before the order: ${UI[0]}"
# Everyone selected that can fight guards the town hall (workers too: a guard order is an order to fight).
[ "$(field "${UI[1]}" guarding)" -ge 4 ] || fail "the soldiers are not guarding: ${UI[1]}"
[ "$(field "${UI[2]}" building)" = "town_hall" ] || fail "the town hall is not selected: ${UI[2]}"
[ "$(field "${UI[2]}" rally)" != "none" ] || fail "no rally point set: ${UI[2]}"
units="$(field "${UI[2]}" units)"
[ "$(field "${UI[3]}" units)" -gt "$units" ] || fail "no unit trained: ${UI[3]}"
[ "$(field "${UI[3]}" guarding)" -ge 4 ] || fail "the soldiers stopped guarding: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI guard screenshot test OK: $OUT"
