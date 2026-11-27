#!/usr/bin/env bash
# Scripted-input screenshot test for forest fires, in test mode (a clear, dry day). A grove of trees is put down on
# open grass with a house of yours against its east end; a worker of yours is box-selected and, with the set-fire
# tool (Ctrl+I), sets the westmost tree alight. The fire spreads through the grove and scorches the house; once it
# has burned out the trees are charred stumps. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-fire-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/fire}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI fire test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: open grass south-east of the town hall (152..176 x 393..412). Big trees (the map's size, two
# blocks a block) eight columns apart, at 156..172 along 398 and 160..168 along 406, their crowns touching; the house's
# corner at 179,394, by the east end; the worker at 150,398, west of the first tree.
STEPS="
wait 20
clickui test_tab_place
clickui test_cat_tree
select test_item broad
clickui test_place
clickworld 156,g,398
wait 3
clickworld 164,g,398
wait 3
clickworld 172,g,398
wait 3
clickworld 160,g,406
wait 3
clickworld 168,g,406
wait 3
ticks 3
key escape
building house 179,394
spawn worker 150,398
ticks 5
camera 164,402 20 50 30
wait 3
dragworld 148,g,396 151,g,400
wait 2
key ctrl+i
wait 2
clickworld 156,g,400
wait 3
ticks 3
print
ticks 360
camera 164,402 20 45 30
wait 5
key f1
wait 3
print
shot $OUT/ui-fire-spreading.png
ticks 3000
wait 5
print
shot $OUT/ui-fire-burnt.png
"
LOG="$OUT/ui-fire.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" kindlers)" -eq 1 ] || fail "the worker cannot set a fire: ${UI[0]}"
[ "$(field "${UI[0]}" kindling)" -eq 1 ] || fail "the worker is not setting the tree alight: ${UI[0]}"
[ "$(field "${UI[1]}" burning)" -ge 20 ] || fail "the fire is not burning: ${UI[1]}"
[ "$(field "${UI[2]}" burning)" -eq 0 ] || fail "the fire has not burned out: ${UI[2]}"
[ "$(field "${UI[2]}" charred)" -ge 6 ] || fail "the grove did not burn down to charred stumps: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI fire screenshot test OK: $OUT"
