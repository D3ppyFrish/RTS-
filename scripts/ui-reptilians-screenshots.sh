#!/usr/bin/env bash
# Scripted-input screenshot test for the Reptilians, the swamp brood. First the start screen: the Reptilians are picked
# (six factions to choose from), and the match starts with their own units by your town hall in the west: workers,
# scale guards, blowpipers and marsh shamans, no spearmen, riders, archers or mages. Then, in test mode as the
# Reptilians: two marsh shamans flood a line of grass into a shallow pool (Ctrl+Y), scale guards lie hidden in it, and
# Settler spearmen come at them. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-reptilians-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/reptilians}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI Reptilians test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234: the west start (player 0) has its town hall at 128..134 x 381..387.
STEPS="
wait 10
clickui start_reptilians
wait 3
print
shot $OUT/ui-reptilians-start-screen.png
clickui start_button
waitmatch
wait 10
camera 131,391 25 40 13
wait 5
print
shot $OUT/ui-reptilians-start.png
"
LOG="$OUT/ui-reptilians.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --menu --seed=1234 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" faction)" = reptilians ] || fail "the Reptilians were not picked: ${UI[0]}"
[ "$(field "${UI[1]}" screen)" = match ] || fail "the match did not start: ${UI[1]}"
[ "$(field "${UI[1]}" faction)" = reptilians ] || fail "you are not playing the Reptilians: ${UI[1]}"

# Test mode as the Reptilians. Open grass south-east of the town hall (152..176 x 393..412): two marsh shamans flood
# a line along z = 402 (two of them: two columns wide), scale guards walk into the pool and lie in it, and Settler
# spearmen come from the south.
STEPS="
wait 20
clickui test_tab_world
spawn marsh_shaman 158,396 2
ticks 10
camera 160,398 0 45 16
wait 3
dragworld 153,g,391 164,g,401
wait 2
key ctrl+y
wait 2
mouseworld 155,g,402
down here
mouseworld 165,g,402
wait 3
print
shot $OUT/ui-reptilians-flood-drag.png
up here
ticks 10
print
spawn scale_guard 157,402 5
spawn blowpiper 160,398 2
ticks 40
print
camera 160,401 18 38 14
wait 5
shot $OUT/ui-reptilians-pool.png
spawn spearman 160,411 3 1
ticks 120
print
camera 160,403 18 38 14
wait 5
shot $OUT/ui-reptilians-ambush.png
camera 158,399 200 25 7
wait 5
shot $OUT/ui-reptilians-close.png
"
LOG2="$OUT/ui-reptilians-test.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=reptilians,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG2"

! grep -q '^Input script' "$LOG2" || fail "a scripted step failed: $(grep '^Input script' "$LOG2")"
mapfile -t UI < <(grep '^UI ' "$LOG2")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" tool)" = Flood ] || fail "the Flood tool is not on: ${UI[0]}"
[ "$(field "${UI[0]}" flood_casters)" -eq 2 ] || fail "the shamans are not selected: ${UI[0]}"
[ "$(field "${UI[0]}" floods)" -eq 0 ] || fail "the ground flooded before letting go: ${UI[0]}"
[ "$(field "${UI[1]}" floods)" -eq 1 ] && [ "$(field "${UI[1]}" flood_water)" -ge 12 ] || fail "the ground did not flood: ${UI[1]}"
[ "$(field "${UI[2]}" lurking)" -ge 3 ] || fail "no scale guards lie in the pool: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG" "$LOG2" | awk -F: '{s+=$2} END {print s}')" -eq 6 ] || fail "missing screenshots"
echo "UI Reptilians screenshot test OK: $OUT"
