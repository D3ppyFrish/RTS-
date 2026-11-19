#!/usr/bin/env bash
# Scripted-input screenshot test for the wolf lairs. First as the Orcs (--player=1): the starting units are
# box-selected and attack-move to the wolf lair west of the east start; the wolves they beat stay on the
# field, now theirs. The Beaten button picks one, which a spearman rides (it becomes a wolf rider), then
# the next, which is recruited. The units around the cleared den are box-selected and make it a wolf lodge,
# which their workers build. Then as the Settlers: their starting units clear the lair east of the west
# start, and the wolves die and pay the lair's reward into the town hall; the units around the cleared den
# are box-selected and use it, and the spearman nearest it becomes a Wolfheart. Saves PNGs to OUT_DIR and fails
# if the UI state is not as expected.
# Usage: scripts/ui-monsters-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234: the start areas are at 131,384 (west, the Settlers) and 655,384 (east, the Orcs), each with
# its 24 starting units in rows from 5 columns west and 4 rows south of the start, and a wolf lair 40
# blocks towards the map centre: dens at 171,384 and 615,384, four wolves each. A third pack keeps the
# lair by the goblin town (den at 395,266), far from both.
ORC_STEPS="
wait 20
camera 655,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
ticks 2
print
camera 622,385 45 50 30
wait 5
clickworld 618,g,385 right ctrl
key plus*2
ticks 110
shot $OUT/ui-wolves-fight.png
ticks 400
print
clickui beaten_button
wait 3
print
camera 617,385 30 45 16
wait 5
shot $OUT/ui-wolves-beaten.png
clickui tame_ride
ticks 2
print
clickui beaten_button
wait 3
clickui tame_recruit
ticks 2
print
camera 617,385 30 40 12
wait 5
shot $OUT/ui-wolves-tamed.png
camera 617,385 30 50 34
wait 5
dragworld 605,g,373 629,g,397
wait 2
ticks 2
print
shot $OUT/ui-den-button.png
clickui den_use
ticks 2
print
ticks 500
camera 617,385 30 45 20
wait 5
print
shot $OUT/ui-den-lodge.png
"
SETTLER_STEPS="
wait 20
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
ticks 2
print
camera 165,385 45 50 30
wait 5
clickworld 168,g,385 right ctrl
key plus*2
ticks 510
print
shot $OUT/ui-wolves-settlers.png
camera 171,385 30 50 34
wait 5
dragworld 160,g,379 178,g,393
wait 2
ticks 2
print
shot $OUT/ui-den-wolfheart-button.png
clickui den_use
ticks 2
print
camera 171,385 30 35 10
wait 5
shot $OUT/ui-den-wolfheart.png
"
LOG="$OUT/ui-monsters.log"
run() {
  xvfb-run -a -s "-screen 0 1600x900x24" \
    godot --path game --rendering-driver opengl3 -- "$@" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)"
}
{ run --player=1 --focus=start1 --input="$ORC_STEPS"; run --player=0 --focus=start0 --input="$SETTLER_STEPS"; } | tee "$LOG"

fail() { echo "UI monsters test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 12 ] || fail "expected 12 UI lines, got ${#UI[@]}"
# Three wolf packs (one by each start and the goblins'), and the lairs spread across the map (issue #45).
[ "$(field "${UI[0]}" own)" -eq 24 ] && [ "$(field "${UI[0]}" wild)" -ge 12 ] && [ "$(field "${UI[0]}" lairs)" -ge 3 ] \
  || fail "the Orcs' starting units are not selected, or the three packs are not there: ${UI[0]}"
# The Orcs beat the whole pack: the four wolves are theirs, waiting, the lair is cleared and pays nothing.
[ "$(field "${UI[1]}" beaten)" -eq 4 ] && [ "$(field "${UI[1]}" wild)" -eq $(( $(field "${UI[0]}" wild) - 4 )) ] \
  && [ "$(field "${UI[1]}" lairs)" -eq $(( $(field "${UI[0]}" lairs) - 1 )) ] \
  && [ "$(field "${UI[1]}" food)" -eq "$(field "${UI[0]}" food)" ] || fail "the Orcs did not beat the pack: ${UI[1]}"
[ "$(field "${UI[2]}" own)" -eq 1 ] && [ "$(field "${UI[2]}" unit)" = wolf ] || fail "the Beaten button did not pick a wolf: ${UI[2]}"
[ "$(field "${UI[3]}" riders)" -eq 1 ] && [ "$(field "${UI[3]}" beaten)" -eq 3 ] || fail "no spearman rode the wolf: ${UI[3]}"
[ "$(field "${UI[4]}" monsters)" -eq 1 ] && [ "$(field "${UI[4]}" beaten)" -eq 2 ] && [ "$(field "${UI[4]}" own)" -eq 1 ] \
  && [ "$(field "${UI[4]}" unit)" = wolf ] || fail "the next wolf was not recruited: ${UI[4]}"
# The units around the cleared den can use it; the den becomes a wolf lodge, which they build.
[ "$(field "${UI[5]}" den_usable)" -eq 1 ] && [ "$(field "${UI[5]}" dens_used)" -eq 0 ] || fail "the cleared den cannot be used: ${UI[5]}"
[ "$(field "${UI[6]}" dens_used)" -eq 1 ] && [ "$(field "${UI[6]}" den_usable)" -eq 0 ] && [ "$(field "${UI[6]}" sites)" -ge 1 ] \
  || fail "the den did not become a lodge site: ${UI[6]}"
[ "$(field "${UI[7]}" den_buildings)" -eq 1 ] || fail "the wolf lodge was not built: ${UI[7]}"
# The Settlers kill the pack instead and get the lair's reward: 150 food and 20 iron.
[ "$(field "${UI[8]}" own)" -eq 24 ] && [ "$(field "${UI[8]}" lairs)" -eq "$(field "${UI[0]}" lairs)" ] || fail "the Settlers' starting units are not selected: ${UI[8]}"
[ "$(field "${UI[9]}" lairs)" -eq $(( $(field "${UI[8]}" lairs) - 1 )) ] && [ "$(field "${UI[9]}" wild)" -eq $(( $(field "${UI[8]}" wild) - 4 )) ] \
  && [ "$(field "${UI[9]}" beaten)" -eq 0 ] \
  && [ "$(field "${UI[9]}" food)" -eq $(( $(field "${UI[8]}" food) + 150 )) ] && [ "$(field "${UI[9]}" iron)" -eq $(( $(field "${UI[8]}" iron) + 20 )) ] \
  || fail "the Settlers did not clear the lair for its reward: ${UI[9]}"
# Then they use the den: one spearman becomes a Wolfheart, and the den is gone.
[ "$(field "${UI[10]}" den_usable)" -eq 1 ] && [ "$(field "${UI[10]}" den_forms)" -eq 0 ] || fail "the Settlers cannot use the cleared den: ${UI[10]}"
[ "$(field "${UI[11]}" den_forms)" -eq 1 ] && [ "$(field "${UI[11]}" dens_used)" -eq 1 ] && [ "$(field "${UI[11]}" den_usable)" -eq 0 ] \
  || fail "no spearman became a Wolfheart at the den: ${UI[11]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 8 ] || fail "missing screenshots"
echo "UI monsters screenshot test OK: $OUT"
