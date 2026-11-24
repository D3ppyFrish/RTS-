#!/usr/bin/env bash
# Scripted-input screenshot test for repair (issue #150; no tech needed, every faction), in test mode: you get every
# tech and goods; the test panel puts up a short stone wall of yours running north and south, the top levels of
# its middle are knocked out (test mode's damage, as a siege would), three of your workers are put down beside it, selected and
# right-clicked onto the gap: they repair it, putting the blocks back a block at a time, and go on along the wall.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-repair-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/repair}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI repair test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: open grass south-east of the town hall (152..176 x 393..412). The wall runs along z at
# x = 166 from z = 396 to 404; the top two to four blocks of 398..402 are knocked out, the workers stand east of it.
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
clickui test_goods_0
clickui test_tab_place
clickui test_cat_building
select test_item stone_wall
"
for z in 396 397 398 399 400 401 402 403 404; do
  STEPS+="clickui test_place
clickworld 166,g,$z
"
done
STEPS+="key escape
ticks 10
damage 166,398 80
damage 166,399 150
damage 166,400 160
damage 166,401 150
damage 166,402 80
ticks 10
spawn worker 169,399
spawn worker 169,400
spawn worker 169,401
ticks 10
camera 167,400 250 40 18
wait 3
print
shot $OUT/ui-repair-damaged.png
clickworld 169,g,399
wait 2
clickworld 169,g,400 shift
wait 2
clickworld 169,g,401 shift
wait 2
print
clickworld 166,g,400 right
ticks 30
camera 167,400 250 40 18
wait 3
print
shot $OUT/ui-repair-working.png
ticks 1500
print
"
LOG="$OUT/ui-repair.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" walls)" -ge 9 ] || fail "the wall was not put up: ${UI[0]}"
[ "$(field "${UI[0]}" damaged)" -ge 5 ] || fail "the wall was not knocked about: ${UI[0]}"
[ "$(field "${UI[1]}" selected)" -ge 3 ] || fail "the workers were not selected: ${UI[1]}"
[ "$(field "${UI[2]}" repairing)" -ge 1 ] || fail "nobody is repairing the wall: ${UI[2]}"
[ "$(field "${UI[2]}" damaged)" -ge 1 ] || fail "the wall was whole before the shot: ${UI[2]}"
[ "$(field "${UI[3]}" damaged)" -eq 0 ] || fail "the wall is still damaged: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI repair screenshot test OK: $OUT"
