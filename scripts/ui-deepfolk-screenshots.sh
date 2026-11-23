#!/usr/bin/env bash
# Scripted-input screenshot test for the Deepfolk, the insect hive faction. First the start screen: the Deepfolk are
# picked (three factions to choose from), and the match starts with the hive's units by your town hall in the west:
# workers, skitterlings, spitters and burrowers, no spearmen. Then, in test mode as the Deepfolk: a brood chamber is
# put down by the town hall and selected, a batch of skitterlings is hatched (four from one training), and a swarm
# is put down in a trench roofed over with stone next to Settler spearmen; the roof is dug away and the swarm bursts
# out on them, striking harder. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-deepfolk-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/deepfolk}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI Deepfolk test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234: the west start (player 0) has its town hall at 128..134 x 381..387.
STEPS="
wait 10
clickui start_deepfolk
wait 3
print
shot $OUT/ui-deepfolk-start-screen.png
clickui start_button
waitmatch
wait 10
camera 131,391 25 40 13
wait 5
print
shot $OUT/ui-deepfolk-hive.png
"
LOG="$OUT/ui-deepfolk.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --menu --seed=1234 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" faction)" = deepfolk ] || fail "the Deepfolk were not picked: ${UI[0]}"
[ "$(field "${UI[1]}" screen)" = match ] || fail "the match did not start: ${UI[1]}"
[ "$(field "${UI[1]}" faction)" = deepfolk ] || fail "you are not playing the Deepfolk: ${UI[1]}"
[ "$(field "${UI[1]}" batches)" = "skitterling:8,spitter:8" ] || fail "the hive did not start with its own units: ${UI[1]}"

# Test mode as the Deepfolk. Open grass south-east of the town hall (152..176 x 393..412).
STEPS="
wait 20
key plus*3
clickui test_tab_place
clickui test_cat_building
select test_item brood_chamber
clickui test_place
clickworld 150,g,396
key escape
ticks 4
clickworld 150,g,396
wait 3
print
clickui train_skitterling
ticks 440
camera 151,400 30 42 15
wait 5
print
shot $OUT/ui-deepfolk-hatched.png
blocks 160,g-2,404 172,g,405 Air
ticks 4
spawn skitterling 162,404 3
spawn skitterling 165,405 3
spawn skitterling 168,404 3
spawn spitter 171,405 2
ticks 4
blocks 159,g,403 173,g,406 Stone
spawn spearman 166,409 6 1
ticks 12
print
blocks 159,g,403 173,g,406 Air
ticks 6
camera 166,406 20 40 13
wait 3
print
shot $OUT/ui-deepfolk-burst.png
"
LOG2="$OUT/ui-deepfolk-test.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=deepfolk,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG2"

! grep -q '^Input script' "$LOG2" || fail "a scripted step failed: $(grep '^Input script' "$LOG2")"
mapfile -t UI < <(grep '^UI ' "$LOG2")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" building)" = brood_chamber ] || fail "the brood chamber is not selected: ${UI[0]}"
[ "$(field "${UI[1]}" batches)" = "skitterling:12,spitter:8" ] || fail "a batch of four skitterlings did not hatch: ${UI[1]}"
[ "$(field "${UI[2]}" bursting)" -eq 0 ] || fail "units under the roof are bursting: ${UI[2]}"
[ "$(field "${UI[3]}" bursting)" -ge 5 ] || fail "the swarm did not burst out when the roof was dug away: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG" "$LOG2" | awk -F: '{s+=$2} END {print s}')" -eq 4 ] || fail "missing screenshots"
echo "UI Deepfolk screenshot test OK: $OUT"
