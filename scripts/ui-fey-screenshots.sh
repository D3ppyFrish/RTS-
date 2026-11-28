#!/usr/bin/env bash
# Scripted-input screenshot test for the Fey, tricksters of old magic. First the start screen: the Fey are picked (eight
# factions to choose from), and the match starts with their own units by your town hall in the west: workers, sprites,
# wisps and enchanters, no spearmen, riders, archers or mages. Then, in test mode as the Fey: a close-up of a sprite, a
# wisp, an enchanter and a fey worker, and a fairy ring put down in a ring of trees, which channels mana from them with
# no keeper (its panel shown). Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-fey-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/fey}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI Fey test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234: the west start (player 0) has its town hall at 128..134 x 381..387.
STEPS="
wait 10
clickui start_fey
wait 3
print
shot $OUT/ui-fey-start-screen.png
clickui start_button
waitmatch
wait 10
camera 131,391 25 40 13
wait 5
print
shot $OUT/ui-fey-start.png
"
LOG="$OUT/ui-fey.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --menu --seed=1234 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" faction)" = fey ] || fail "the Fey were not picked: ${UI[0]}"
[ "$(field "${UI[1]}" screen)" = match ] || fail "the match did not start: ${UI[1]}"
[ "$(field "${UI[1]}" faction)" = fey ] || fail "you are not playing the Fey: ${UI[1]}"

# Test mode as the Fey, on open grass south-east of the town hall (152..176 x 393..412): one of each of their three units
# and a worker in a row for a close look, then a ring of eight trees round 166,402 with a fairy ring in the middle,
# clicked to show its panel.
STEPS="
wait 20
clickui test_tab_world
spawn sprite 155,398 1
spawn wisp 157,398 1
spawn enchanter 159,398 1
spawn worker 161,398 1
ticks 20
camera 158,398 20 22 6
wait 5
print
shot $OUT/ui-fey-close.png
tree 158,395
tree 166,393
tree 174,395
tree 157,402
tree 175,402
tree 158,409
tree 166,411
tree 174,409
building fairy_ring 165,401
ticks 2
print
ticks 200
print
camera 166,402 15 62 34
wait 3
clickworld 166,g,402
wait 5
print
shot $OUT/ui-fey-fairy-ring.png
"
LOG2="$OUT/ui-fey-test.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=fey,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG2"

! grep -q '^Input script' "$LOG2" || fail "a scripted step failed: $(grep '^Input script' "$LOG2")"
mapfile -t UI < <(grep '^UI ' "$LOG2")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" faction)" = fey ] || fail "you are not playing the Fey: ${UI[0]}"
[ "$(field "${UI[1]}" fairy_rings)" -eq 1 ] || fail "no fairy ring: ${UI[1]}"
[ "$(field "${UI[1]}" ring_trees)" -ge 6 ] || fail "the fairy ring does not count the trees round it: ${UI[1]}"
[ "$(field "${UI[2]}" mana)" -gt "$(field "${UI[1]}" mana)" ] || fail "the fairy ring channelled no mana: ${UI[1]} / ${UI[2]}"
[ "$(field "${UI[3]}" building)" = fairy_ring ] || fail "the fairy ring was not selected: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG" "$LOG2" | awk -F: '{s+=$2} END {print s}')" -eq 4 ] || fail "missing screenshots"
echo "UI Fey screenshot test OK: $OUT"
