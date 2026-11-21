#!/usr/bin/env bash
# Scripted-input screenshot test for discovery techs, with fog of war on: the starting units of the west start are
# sent to search the old ruin south of it. Searching it is a discovery: an alert says Wardens' charts can be
# researched, and the tech tree offers it. It is researched, and the far ruin to the south-east (526,656), never seen,
# shows through the fog. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-discovery-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/discovery}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. The ruin nearest
# to the south has its middle at 176,539 (see ui-ruins-screenshots.sh). Under the fog the ruin cannot be clicked on
# before it is seen, so the units are first sent there, and told to search it once they see it.
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
camera 176,539 30 35 14
wait 5
clickworld 176,g,539 right
ticks 900
wait 5
clickworld 176,g,539 right
ticks 2
print
ticks 300
wait 5
print
key k
wait 3
print
shot $OUT/ui-discovery-tree.png
clickui tech_wardens_charts
mouse 1500,820
ticks 2
print
key k
camera 526,656 30 55 70
wait 10
shot $OUT/ui-discovery-before.png
ticks 440
wait 10
print
shot $OUT/ui-discovery-charts.png
"
LOG="$OUT/ui-discovery.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --seed=1234 --fog --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI discovery test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 6 ] || fail "expected 6 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" fog)" = on ] && [ "$(field "${UI[0]}" selected_workers)" -ge 3 ] || fail "fog on and the workers selected: ${UI[0]}"
[ "$(field "${UI[1]}" ruin_searching)" -ge 3 ] || fail "the right click on the ruin should send them to search it: ${UI[1]}"
[ "$(field "${UI[2]}" ruins_found)" -eq 1 ] || fail "the ruin should have been searched: ${UI[2]}"
[[ "$(field "${UI[2]}" alert_kinds)" == *TechDiscovered* ]] || fail "the discovery should come up as an alert: ${UI[2]}"
[ "$(field "${UI[3]}" techtree)" -eq 1 ] || fail "the tech tree should be open: ${UI[3]}"
[ "$(field "${UI[4]}" research)" = wardens_charts ] || fail "Wardens' charts should be being researched: ${UI[4]}"
[ "$(field "${UI[5]}" research)" = none ] && [ "$(field "${UI[5]}" seen)" -gt $(( $(field "${UI[4]}" seen) + 600 )) ] \
  || fail "Wardens' charts should be researched and show more of the map: ${UI[4]} / ${UI[5]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI discovery screenshot test OK: $OUT"
