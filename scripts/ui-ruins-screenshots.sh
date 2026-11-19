#!/usr/bin/env bash
# Scripted-input screenshot test for searching the old ruins: the marker floating over an unsearched ruin south of
# the west start, then the starting units are drag-selected and sent there with a right click on the ruin. They walk
# in and search it, and the find (goods for the town hall) comes up as an alert. Saves PNGs to OUT_DIR and fails if
# the UI state is not as expected.
# Usage: scripts/ui-ruins-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ruins}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. The ruin nearest
# to the south has its middle at 176,539 (floor at level 19), about 160 blocks away.
STEPS="
wait 20
camera 176,539 30 35 14
wait 10
shot $OUT/ui-ruin-marker.png
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
camera 176,539 30 35 14
wait 5
clickworld 176,g,539 right
ticks 2
print
ticks 450
wait 5
print
shot $OUT/ui-ruin-searching.png
ticks 120
wait 5
print
shot $OUT/ui-ruin-found.png
"
LOG="$OUT/ui-ruins.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI ruins test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected_workers)" -ge 3 ] && [ "$(field "${UI[0]}" ruins_found)" -eq 0 ] || fail "the workers are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" ruin_searching)" -ge 3 ] || fail "the right click on the ruin should send them to search it: ${UI[1]}"
[ "$(field "${UI[2]}" ruin_near)" -ge 0 ] && [ "$(field "${UI[2]}" ruins_found)" -eq 0 ] || fail "the units should be searching the ruin: ${UI[2]}"
[ "$(field "${UI[3]}" ruins_found)" -eq 1 ] && [ "$(field "${UI[3]}" ruin_searching)" -eq 0 ] || fail "the ruin should have been searched: ${UI[3]}"
[[ "$(field "${UI[3]}" alert_kinds)" == *RuinSearched* ]] || fail "the find should come up as an alert: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI ruins screenshot test OK: $OUT"
