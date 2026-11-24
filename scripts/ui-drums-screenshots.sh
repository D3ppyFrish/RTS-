#!/usr/bin/env bash
# Scripted-input screenshot test for the war drummer (issue #151), in test mode playing the Orcs: a war drummer and six
# orc spearmen are put down on open grass, the drummer 12 columns behind the spearmen. Three of the spearmen are struck
# dead (test mode's Kill), so the other three break; within earshot of the drum they run to the drummer instead of home
# and rally there. Then the drummer is selected and beats the charge with Ctrl+C. Saves PNGs to OUT_DIR and fails if
# the UI state is not as expected.
# Usage: scripts/ui-drums-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/drums}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI drums test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: open grass south-east of the town hall (152..176 x 393..412). The spearmen stand in two rows
# of three, at z = 396 and z = 400, x = 166 to 170: every one within 8 columns of the others, where a death shakes
# morale. The front row is struck dead. The drummer stands at 168,412, 12 columns behind the back row: in earshot.
STEPS="
wait 20
spawn war_drummer 168,412
spawn spearman 166,396
spawn spearman 168,396
spawn spearman 170,396
spawn spearman 166,400
spawn spearman 168,400
spawn spearman 170,400
ticks 5
camera 168,404 0 55 30
wait 3
dragworld 164,g,395 172,g,397
wait 2
clickui test_tab_units
clickui test_kill
ticks 3
dragworld 164,g,399 172,g,401
wait 2
print
ticks 25
camera 168,408 330 35 14
wait 5
shot $OUT/ui-drums-running.png
ticks 70
dragworld 165,g,408 171,g,414
wait 2
print
camera 168,410 330 35 12
wait 5
shot $OUT/ui-drums-rallied.png
clickworld 168,g,412
wait 2
key ctrl+c
ticks 3
print
wait 3
shot $OUT/ui-drums-charge.png
"
LOG="$OUT/ui-drums.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=orcs,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" routing)" -eq 3 ] || fail "the three spearmen left did not break: ${UI[0]}"
[ "$(field "${UI[0]}" drumming)" -eq 1 ] || fail "the drummer is not drumming the rally: ${UI[0]}"
[ "$(field "${UI[1]}" routing)" -eq 0 ] || fail "the broken spearmen did not rally at the drum: ${UI[1]}"
# The rout lasts 8 seconds (160 ticks) from the kill at about tick 105: no one running at tick 250 or so means they rallied.
[ "$(field "${UI[1]}" tick)" -lt 260 ] || fail "too late to tell a rally from the rout's end: ${UI[1]}"
[ "$(field "${UI[1]}" own)" -ge 2 ] || fail "the rallied spearmen are not round the drum: ${UI[1]}"
[ "$(field "${UI[2]}" drumming)" -eq 1 ] || fail "the drummer did not beat the charge: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI drums screenshot test OK: $OUT"
