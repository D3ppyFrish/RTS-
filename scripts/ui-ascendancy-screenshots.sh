#!/usr/bin/env bash
# Scripted-input screenshot test for Ascendancy, the great works' win: a match started with --great-work has the
# Settlers' Hall of Legends finished in the deep caves' chamber nearest the west start, in a hall dug out for it. The
# top bar's crown button counts down ("You win in 5:00") and an alert says the great work is finished; clicking the
# crown looks at it with the height slice cutting the land away (the land over the chamber is cut off). The match is run on until the clock runs out, and
# the end of match panel says it was won by the great work. Saves PNGs to OUT_DIR and fails if the UI state is not as
# expected.
# Usage: scripts/ui-ascendancy-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ascendancy}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

STEPS="
wait 20
ticks 2
wait 5
print
clickui great_work_button
mouse 800,700
wait 20
shot $OUT/ui-great-work.png
key plus*3
ticks 1000
wait 5
print
ticks 1000
ticks 1000
ticks 1000
ticks 1000
ticks 1000
ticks 1000
wait 20
print
shot $OUT/ui-great-work-won.png
"
LOG="$OUT/ui-ascendancy.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --great-work --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI ascendancy test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" great_works)" -eq 1 ] && [ "$(field "${UI[0]}" great_work_owner)" -eq 0 ] || fail "your great work should stand finished: ${UI[0]}"
[[ "$(field "${UI[0]}" alert_kinds)" == *GreatWorkFinished* ]] || fail "its being finished should come up as an alert: ${UI[0]}"
[ "$(field "${UI[1]}" great_work_left)" -lt "$(field "${UI[0]}" great_work_left)" ] || fail "its clock should run: ${UI[1]}"
[ "$(field "${UI[2]}" by_ascendancy)" -eq 1 ] || fail "the match should be won by the great work: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI ascendancy screenshot test OK: $OUT"
