#!/usr/bin/env bash
# Scripted-input screenshot test for the city-states: the goblin town is picked from the top bar (neutral,
# its totem in plain wood and six wild guards round it). The starting units clear the wolf lair on the
# road east, then march on the goblin town and break its totem: the town is yours, its guards change sides
# and the totem stands again in your colour. A goblin of each kind is hired at its market, and a caravan is
# trained at the town hall and sent to trade at the town, while tribute (gold) comes in. Last the ogres' hold
# (issue #46) is picked from the top bar: neutral, its stone idol plain and four ogre guards round it, and then
# the kobolds' warren, dug into a chamber of the deep caves, seen through the height slice: neutral, its great
# crystal plain and five kobold guards round it, and last the troll bridge: neutral, its standing stone plain, four
# troll guards and the stone bridge it built over the river, and last the centaur camp: neutral, its horse-tail
# standard plain and five centaur guards round it. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-citystate-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234: the goblin town's market at 393,20,296 (the totem on it), the west start's town hall at
# 128..134 x 381..387 with the starting units in rows just south of it, and the wolf lair 40 blocks east
# (den at 171,384), which a lone caravan would not get past. --goods gives enough for the hires and the
# caravan; --houses makes room for them behind the hall (the start has more people than its hall houses).
# The ogres' hold has its market at 393,632, the kobolds' warren at 418,46,437 (under the forest), and the
# troll bridge at 389,180, its bridge west over the river (columns 364 to 381, rows 179 to 181), and the
# centaur camp at 329,86,476 on the grassland.
STEPS="
wait 20
clickui city_goblins
wait 5
print
camera 393,296 20 40 24
wait 5
shot $OUT/ui-city.png
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
camera 165,385 45 50 30
wait 5
clickworld 168,g,385 right ctrl
key plus*2
ticks 510
print
camera 393,296 20 40 30
wait 5
clickworld 393,g,296 right
ticks 3000
print
camera 393,296 20 40 24
wait 5
shot $OUT/ui-city-yours.png
clickui city_goblins
wait 2
clickui hire_goblin_cutthroat
clickui hire_goblin_slinger
ticks 6
print
camera 394,297 -30 40 9
wait 5
shot $OUT/ui-goblins.png
camera 131,389 45 55 22
wait 5
clickworld 131,g,384
wait 2
clickui train_caravan
ticks 420
camera 131,388 70 30 7
wait 5
clickworld 131,g+1,388
wait 2
print
shot $OUT/ui-caravan.png
camera 393,296 20 50 40
wait 5
clickworld 395,g,299 right
ticks 4
print
ticks 2400
print
camera 393,296 20 50 30
wait 5
shot $OUT/ui-caravan-trade.png
clickui city_ogres
wait 5
print
camera 393,632 20 40 26
wait 5
shot $OUT/ui-ogres.png
camera 394,633 -30 40 11
wait 5
shot $OUT/ui-ogres-close.png
clickui city_kobolds
wait 5
key home
camera 418,437 20 75 30
wait 3
key pagedown*34
camera 418,437 20 60 30
wait 20
print
shot $OUT/ui-kobolds.png
camera 419,440 -30 55 14
wait 10
shot $OUT/ui-kobolds-close.png
key home
clickui city_trolls
wait 5
print
camera 380,180 30 45 30
wait 10
shot $OUT/ui-trolls.png
camera 386,180 -120 35 14
wait 5
shot $OUT/ui-trolls-close.png
clickui city_centaurs
wait 5
print
camera 329,476 30 45 30
wait 10
shot $OUT/ui-centaurs.png
camera 329,476 210 30 14
wait 5
shot $OUT/ui-centaurs-close.png
"
LOG="$OUT/ui-citystate.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --goods=1000 --houses=4 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script|City-state)" | tee "$LOG"

fail() { echo "UI city-state test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 11 ] || fail "expected 11 UI lines, got ${#UI[@]}"
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
# The goblins' tribute is gold, and their trade brings back iron: either counts.
GOT_BEFORE=$(( $(field "${UI[5]}" iron) + $(field "${UI[5]}" gold) ))
GOT_AFTER=$(( $(field "${UI[6]}" iron) + $(field "${UI[6]}" gold) ))
[ "$(field "${UI[6]}" holder)" -eq 0 ] && [ "$(field "${UI[6]}" caravans)" -eq 1 ] && [ "$GOT_AFTER" -gt "$GOT_BEFORE" ] \
  || fail "no tribute or trade came in from the goblins: ${UI[6]}"
[ "$(field "${UI[7]}" city)" = ogres ] && [ "$(field "${UI[7]}" holder)" -eq -1 ] && [ "$(field "${UI[7]}" totem_owner)" -eq 7 ] \
  && [ "$(field "${UI[7]}" guards)" -eq 4 ] || fail "the ogres' hold is not selected, or it is not neutral with its idol and guards: ${UI[7]}"
[ "$(field "${UI[8]}" city)" = kobolds ] && [ "$(field "${UI[8]}" holder)" -eq -1 ] && [ "$(field "${UI[8]}" totem_owner)" -eq 7 ] \
  && [ "$(field "${UI[8]}" guards)" -eq 5 ] || fail "the kobolds' warren is not selected, or it is not neutral with its crystal and guards: ${UI[8]}"
[ "$(field "${UI[9]}" city)" = trolls ] && [ "$(field "${UI[9]}" holder)" -eq -1 ] && [ "$(field "${UI[9]}" totem_owner)" -eq 7 ] \
  && [ "$(field "${UI[9]}" guards)" -eq 4 ] && [ "$(field "${UI[9]}" bridge)" -eq 1 ] \
  || fail "the troll bridge is not selected, or it is not neutral with its stone, guards and bridge: ${UI[9]}"
[ "$(field "${UI[10]}" city)" = centaurs ] && [ "$(field "${UI[10]}" holder)" -eq -1 ] && [ "$(field "${UI[10]}" totem_owner)" -eq 7 ] \
  && [ "$(field "${UI[10]}" guards)" -eq 5 ] || fail "the centaur camp is not selected, or it is not neutral with its standard and guards: ${UI[10]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 13 ] || fail "missing screenshots"
echo "UI city-state screenshot test OK: $OUT"
