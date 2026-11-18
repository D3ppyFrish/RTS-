#!/usr/bin/env bash
# Scripted-input screenshot test for the city-states: the goblin town is picked from the top bar (neutral,
# its totem in plain wood and six wild guards round it). The starting units clear the wolf lair on the
# road east, then march on the goblin town and break its totem: the town is yours, its guards change sides
# and the totem stands again in your colour. A goblin of each kind is hired at its market, and a caravan is
# trained at the town hall and sent to trade at the town, while tribute comes in. Last the ogres' hold
# (issue #46) is picked from the top bar: neutral, its stone idol plain and four ogre guards round it. Saves
# PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-citystate-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234: the goblin town's market at 261,21,204 (the totem on it), the west start's town hall at
# 84..90 x 253..259 with the starting units in rows just south of it, and the wolf lair 40 blocks east
# (den at 127,256), which a lone caravan would not get past. --goods gives enough for the hires and the
# caravan; --houses makes room for them behind the hall (the start has more people than its hall houses).
# The ogres' hold has its market at 261,456.
STEPS="
wait 20
clickui city_goblins
wait 5
print
camera 261,204 20 40 24
wait 5
shot $OUT/ui-city.png
camera 87,261 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
camera 121,257 45 50 30
wait 5
clickworld 124,g,257 right ctrl
key plus*2
ticks 510
print
camera 261,204 20 40 30
wait 5
clickworld 261,g,204 right
ticks 1800
print
camera 261,204 20 40 24
wait 5
shot $OUT/ui-city-yours.png
clickui city_goblins
wait 2
clickui hire_goblin_cutthroat
clickui hire_goblin_slinger
ticks 6
print
camera 262,205 -30 40 9
wait 5
shot $OUT/ui-goblins.png
camera 87,261 45 55 22
wait 5
clickworld 87,g,256
wait 2
clickui train_caravan
ticks 420
camera 87,260 70 30 7
wait 5
clickworld 87,g+1,260
wait 2
print
shot $OUT/ui-caravan.png
camera 261,204 20 50 40
wait 5
clickworld 263,g,207 right
ticks 4
print
ticks 1500
print
camera 261,204 20 50 30
wait 5
shot $OUT/ui-caravan-trade.png
clickui city_ogres
wait 5
print
camera 261,456 20 40 26
wait 5
shot $OUT/ui-ogres.png
camera 262,457 -30 40 11
wait 5
shot $OUT/ui-ogres-close.png
"
LOG="$OUT/ui-citystate.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --goods=1000 --houses=4 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script|City-state)" | tee "$LOG"

fail() { echo "UI city-state test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 8 ] || fail "expected 8 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" city)" = goblins ] && [ "$(field "${UI[0]}" holder)" -eq -1 ] && [ "$(field "${UI[0]}" totem_owner)" -eq 7 ] \
  && [ "$(field "${UI[0]}" guards)" -eq 6 ] && [ "$(field "${UI[0]}" guards_owner)" -eq 7 ] \
  || fail "the goblin town is not selected, or it is not neutral with its totem and guards: ${UI[0]}"
[ "$(field "${UI[1]}" lairs)" -eq $(( $(field "${UI[0]}" lairs) - 1 )) ] || fail "the starting units did not clear the wolf lair: ${UI[1]}"
[ "$(field "${UI[2]}" holder)" -eq 0 ] && [ "$(field "${UI[2]}" totem_owner)" -eq 0 ] && [ "$(field "${UI[2]}" guards_owner)" -eq 0 ] \
  || fail "the starting units did not take the goblin town: ${UI[2]}"
[ "$(field "${UI[3]}" mercs)" -eq 2 ] && [ "$(field "${UI[3]}" pool)" -eq 4 ] || fail "no goblins hired: ${UI[3]}"
[ "$(field "${UI[4]}" unit)" = caravan ] && [ "$(field "${UI[4]}" selected)" -eq 1 ] && [ "$(field "${UI[4]}" caravans)" -eq 1 ] \
  || fail "no caravan trained and selected: ${UI[4]}"
[ "$(field "${UI[5]}" trading)" -eq 1 ] || fail "the caravan is not trading: ${UI[5]}"
[ "$(field "${UI[6]}" holder)" -eq 0 ] && [ "$(field "${UI[6]}" caravans)" -eq 1 ] && [ "$(field "${UI[6]}" iron)" -gt "$(field "${UI[5]}" iron)" ] \
  || fail "no tribute or trade came in from the goblins: ${UI[6]}"
[ "$(field "${UI[7]}" city)" = ogres ] && [ "$(field "${UI[7]}" holder)" -eq -1 ] && [ "$(field "${UI[7]}" totem_owner)" -eq 7 ] \
  && [ "$(field "${UI[7]}" guards)" -eq 4 ] || fail "the ogres' hold is not selected, or it is not neutral with its idol and guards: ${UI[7]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 7 ] || fail "missing screenshots"
echo "UI city-state screenshot test OK: $OUT"
