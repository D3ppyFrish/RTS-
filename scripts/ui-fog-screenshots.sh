#!/usr/bin/env bash
# Scripted-input screenshot test for fog of war (issue #39). From the start screen, where the Fog of war
# switch is on by default and turns off and on again, a match with two armies facing each other at the map
# centre (--battle) and no computer player: at first the enemy army stands out of sight and is not drawn,
# and from above most of the map is dark; your army attack-moves east and the enemy comes into sight. Then
# the same battle without fog of war, started from the command line: every enemy unit is drawn from the
# start. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-fog-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI fog of war test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, map centre: with --battle=16 your army stands in two ranks at x 241..242, z 211..225, and the
# enemy's twelve columns east at x 254..255, beyond the sight of every soldier in your front rank.
STEPS="
wait 10
print
clickui start_fog
wait 2
print
clickui start_fog
clickui start_computer
wait 2
print
clickui start_button
waitmatch
wait 20
camera 248,218 0 55 22
wait 5
print
shot $OUT/ui-fog-start.png
camera 248,218 0 70 150
wait 5
shot $OUT/ui-fog-above.png
camera 248,218 0 55 22
wait 3
dragworld 236,g,208 245,g,229
wait 2
clickworld 262,g,222 right ctrl
ticks 60
print
shot $OUT/ui-fog-contact.png
"
LOG="$OUT/ui-fog.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --menu --seed=1234 --focus=248,218 --battle=16 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" screen)" = start ] && [ "$(field "${UI[0]}" fog)" -eq 1 ] || fail "fog of war is not on by default: ${UI[0]}"
[ "$(field "${UI[1]}" fog)" -eq 0 ] || fail "the switch did not turn fog of war off: ${UI[1]}"
[ "$(field "${UI[2]}" fog)" -eq 1 ] && [ "$(field "${UI[2]}" computer)" -eq 0 ] || fail "the switches are not as set: ${UI[2]}"
[ "$(field "${UI[3]}" screen)" = match ] && [ "$(field "${UI[3]}" fog)" = on ] || fail "the match has no fog of war: ${UI[3]}"
enemies="$(field "${UI[3]}" enemies)"
[ "$enemies" -ge 16 ] && [ "$(field "${UI[3]}" enemies_shown)" -eq 0 ] || fail "enemy units out of sight are drawn: ${UI[3]}"
seen="$(field "${UI[3]}" seen)"
# Your town hall and the army have seen something, but far from the whole 512 x 512 map.
[ "$seen" -gt 500 ] && [ "$seen" -lt 30000 ] || fail "unexpected explored area: ${UI[3]}"
[ "$(field "${UI[4]}" enemies_shown)" -ge 4 ] && [ "$(field "${UI[4]}" seen)" -gt "$seen" ] \
  || fail "the enemy army did not come into sight: ${UI[4]}"
[ "$(field "${UI[4]}" enemies_shown)" -lt "$(field "${UI[4]}" enemies)" ] || fail "enemy units far away are drawn: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"

# Without fog of war (the default from the command line), every enemy unit shows from the start.
STEPS="
wait 20
camera 248,218 0 55 22
wait 5
print
shot $OUT/ui-fog-off.png
"
LOG="$OUT/ui-fog-off.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --seed=1234 --focus=248,218 --battle=16 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 1 ] || fail "expected 1 UI line without fog of war, got ${#UI[@]}"
[ "$(field "${UI[0]}" fog)" = off ] && [ "$(field "${UI[0]}" enemies_shown)" -eq "$(field "${UI[0]}" enemies)" ] \
  && [ "$(field "${UI[0]}" enemies)" -eq "$enemies" ] || fail "without fog of war not every enemy unit shows: ${UI[0]}"
echo "UI fog of war screenshot test OK: $OUT"
