#!/usr/bin/env bash
# Scripted-input screenshot test for truces between rivals. Seed 1234 with a computer opponent and two armies
# facing each other at the map centre (--battle): the top bar's button for the rival offers a truce, the computer
# (no stronger than you) accepts, and a "Truce with ..." alert shows. Your army is ordered to attack-move through
# theirs: at peace nobody fights (no under attack alert, nobody lost). Then the truce is broken off from the same
# button: both sides are warned, and 30 seconds later it is over and the fighting can start.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-truce-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI truce test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# The battle armies stand round 376,360 (as in ui-alerts-screenshots.sh): yours west, theirs east.
STEPS="
wait 20
camera 376,360 0 55 22
wait 5
print
shot $OUT/ui-truce-offer.png
clickui truce_1
ticks 60
wait 3
print
shot $OUT/ui-truce-peace.png
dragworld 364,g,350 373,g,371
wait 2
clickworld 390,g,364 right ctrl
ticks 300
wait 3
print
shot $OUT/ui-truce-no-fight.png
clickui truce_1
ticks 20
wait 3
print
shot $OUT/ui-truce-broken.png
ticks 600
wait 3
print
"
LOG="$OUT/ui-truce.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --seed=1234 --ai --focus=376,360 --battle=16 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" truce)" = open ] || fail "a truce cannot be offered at the start: ${UI[0]}"
[ "$(field "${UI[1]}" truce)" = peace ] || fail "the computer did not accept the truce: ${UI[1]}"
[[ ",$(field "${UI[1]}" alert_kinds)," == *,TruceStarted,* ]] || fail "no alert for the truce: ${UI[1]}"
army="$(field "${UI[1]}" army)"
[ "$(field "${UI[2]}" truce)" = peace ] || fail "the truce did not hold: ${UI[2]}"
[[ ",$(field "${UI[2]}" alert_kinds)," != *,UnitsAttacked,* ]] || fail "units fought at peace: ${UI[2]}"
[ "$(field "${UI[2]}" army)" -eq "$army" ] || fail "units were lost at peace: ${UI[1]} / ${UI[2]}"
[ "$(field "${UI[3]}" truce)" = ending ] || fail "breaking the truce off did not start its warning: ${UI[3]}"
[[ ",$(field "${UI[3]}" alert_kinds)," == *,TruceEnding,* ]] || fail "no warning that the truce ends: ${UI[3]}"
[ "$(field "${UI[4]}" truce)" = wait ] || fail "the truce did not end, or another can be agreed at once: ${UI[4]}"
[[ ",$(field "${UI[4]}" alert_kinds)," == *,TruceEnded,* ]] || fail "no alert that the truce is over: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 4 ] || fail "missing screenshots"
echo "UI truce screenshot test OK: $OUT"
