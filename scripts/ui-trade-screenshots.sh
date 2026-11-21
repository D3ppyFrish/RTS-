#!/usr/bin/env bash
# Scripted-input screenshot test for trade deals between rivals. Seed 1234 with a computer opponent: the top bar's
# trade button for the rival offers a deal, the computer accepts, and a "Trade deal with ..." alert shows. A caravan
# is trained at the town hall and right-clicked onto the rival's town hall: it takes a load of food from storage
# and sets off to trade there. Then the deal is ended from the same button: the caravan takes its load home and stops.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-trade-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI trade test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234: the west start's town hall at 128..134 x 381..387 (as in ui-citystate-screenshots.sh), the east
# start's (the computer's) at 652..658 x 381..387. --goods pays for the caravan. The caravan is looked at while it
# loads and sets off: a lone caravan does not get past the wolf lair 40 blocks east of the hall.
STEPS="
wait 20
camera 131,389 45 55 22
wait 5
print
clickui deal_1
ticks 60
wait 3
print
shot $OUT/ui-trade-deal.png
clickworld 131,g,384
wait 2
clickui train_caravan
ticks 420
camera 131,388 70 30 7
wait 5
clickworld 131,g+1,388
wait 2
camera 655,384 45 55 30
wait 5
clickworld 655,g,384 right
ticks 4
print
ticks 100
camera 136,388 45 50 16
wait 5
print
shot $OUT/ui-trade-caravan.png
clickui deal_1
ticks 40
wait 3
print
shot $OUT/ui-trade-ended.png
ticks 400
print
"
LOG="$OUT/ui-trade.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --seed=1234 --ai --goods=1000 --houses=4 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 6 ] || fail "expected 6 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" deal)" = open ] || fail "a deal cannot be offered at the start: ${UI[0]}"
[ "$(field "${UI[1]}" deal)" = on ] || fail "the computer did not accept the deal: ${UI[1]}"
[[ ",$(field "${UI[1]}" alert_kinds)," == *,DealStarted,* ]] || fail "no alert for the deal: ${UI[1]}"
[ "$(field "${UI[2]}" caravans)" -eq 1 ] && [ "$(field "${UI[2]}" markets)" -eq 1 ] || fail "the caravan is not trading at the rival's hall: ${UI[2]}"
[ "$(field "${UI[3]}" markets)" -eq 1 ] || fail "the caravan stopped trading: ${UI[3]}"
[ "$(field "${UI[4]}" deal)" = wait ] || fail "the deal did not end: ${UI[4]}"
[[ ",$(field "${UI[4]}" alert_kinds)," == *,DealEnded,* ]] || fail "no alert that the deal is over: ${UI[4]}"
[ "$(field "${UI[5]}" markets)" -eq 0 ] && [ "$(field "${UI[5]}" caravans)" -eq 1 ] \
  || fail "the caravan did not take its load home and stop after the deal ended: ${UI[5]}"
[ "$(field "${UI[5]}" food)" -eq "$(( $(field "${UI[3]}" food) + 20 ))" ] || fail "the load did not come home: ${UI[3]} / ${UI[5]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI trade screenshot test OK: $OUT"
