#!/usr/bin/env bash
# Scripted-input screenshot test for heroes: the game starts with --xp=500, so every starting unit of yours
# (Settlers) is at the second level. The starting units are selected and the spearmen become knights; one
# knight is then selected on its own and becomes the Marshal, the knights' hero form. With all of them
# selected again, the other knights' Marshal button says you have one already and none of them can take it.
# Then an archer is selected on its own, becomes a longbowman and at once the Bowmaster, the longbowmen's hero,
# a rider becomes a lancer and the Horselord, the lancers' hero, a sapper becomes a demolisher and the
# Breachmaster, the demolishers' hero, a mage becomes a battle mage and the Archmage, the battle mages' hero,
# and a worker becomes a foreman and the Master Builder, the foremen's hero.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-hero-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, the 24 starting units (four of each of the six
# trained types, in turn: worker, spearman, archer, rider, sapper, mage) in rows from 126,388, so there is
# a spearman at 127,388, an archer at 128,388, a rider at 129,388, a sapper at 130,388, a mage at 131,388 and a worker at 126,388.
STEPS="
wait 20
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
ticks 2
clickui evolve_knight
ticks 2
print
camera 130,389 20 40 9
wait 5
clickworld 127,g+1,388
wait 2
print
clickui evolve_marshal
ticks 2
wait 2
print
shot $OUT/ui-hero-marshal.png
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
ticks 2
print
shot $OUT/ui-hero-one-only.png
camera 130,389 20 40 9
wait 5
clickworld 128,g+1,388
wait 2
ticks 2
clickui evolve_longbowman
ticks 2
wait 2
clickui evolve_bowmaster
ticks 2
wait 2
print
shot $OUT/ui-hero-bowmaster.png
clickworld 129,g+1,388
wait 2
ticks 2
clickui evolve_lancer
ticks 2
wait 2
clickui evolve_horselord
ticks 2
wait 2
print
shot $OUT/ui-hero-horselord.png
clickworld 130,g+1,388
wait 2
ticks 2
clickui evolve_demolisher
ticks 2
wait 2
clickui evolve_breachmaster
ticks 2
wait 2
print
shot $OUT/ui-hero-breachmaster.png
clickworld 131,g+1,388
wait 2
ticks 2
clickui evolve_battle_mage
ticks 2
wait 2
clickui evolve_archmage
ticks 2
wait 2
print
shot $OUT/ui-hero-archmage.png
clickworld 126,g+1,388
wait 2
ticks 2
clickui evolve_foreman
ticks 2
wait 2
clickui evolve_master_builder
ticks 2
wait 2
print
shot $OUT/ui-hero-master-builder.png
"
LOG="$OUT/ui-hero.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --xp=500 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI hero test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 9 ] || fail "expected 9 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" evolved)" -eq 4 ] && [ "$(field "${UI[0]}" heroes)" -eq 0 ] \
  || fail "the spearmen did not become knights: ${UI[0]}"
[ "$(field "${UI[1]}" own)" -eq 1 ] && [ "$(field "${UI[1]}" unit)" = knight ] && [ "$(field "${UI[1]}" can_evolve)" -eq 1 ] \
  || fail "a knight ready for its next form is not selected on its own: ${UI[1]}"
[ "$(field "${UI[2]}" unit)" = marshal ] && [ "$(field "${UI[2]}" heroes)" -eq 1 ] \
  || fail "the knight did not become the Marshal: ${UI[2]}"
[ "$(field "${UI[3]}" heroes)" -eq 1 ] && [ "$(field "${UI[3]}" own)" -eq 24 ] && [ "$(field "${UI[3]}" can_evolve)" -eq 20 ] \
  || fail "the starting units are not selected again, or there is not exactly one Marshal: ${UI[3]}"
[ "$(field "${UI[4]}" own)" -eq 1 ] && [ "$(field "${UI[4]}" unit)" = bowmaster ] && [ "$(field "${UI[4]}" heroes)" -eq 2 ] \
  || fail "the archer did not become the Bowmaster beside the Marshal: ${UI[4]}"
[ "$(field "${UI[5]}" own)" -eq 1 ] && [ "$(field "${UI[5]}" unit)" = horselord ] && [ "$(field "${UI[5]}" heroes)" -eq 3 ] \
  || fail "the rider did not become the Horselord beside the other heroes: ${UI[5]}"
[ "$(field "${UI[6]}" own)" -eq 1 ] && [ "$(field "${UI[6]}" unit)" = breachmaster ] && [ "$(field "${UI[6]}" heroes)" -eq 4 ] \
  || fail "the sapper did not become the Breachmaster beside the other heroes: ${UI[6]}"
[ "$(field "${UI[7]}" own)" -eq 1 ] && [ "$(field "${UI[7]}" unit)" = archmage ] && [ "$(field "${UI[7]}" heroes)" -eq 5 ] \
  || fail "the mage did not become the Archmage beside the other heroes: ${UI[7]}"
[ "$(field "${UI[8]}" own)" -eq 1 ] && [ "$(field "${UI[8]}" unit)" = master_builder ] && [ "$(field "${UI[8]}" heroes)" -eq 6 ] \
  || fail "the worker did not become the Master Builder beside the other heroes: ${UI[8]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 7 ] || fail "missing screenshots"
echo "UI hero screenshot test OK: $OUT"
