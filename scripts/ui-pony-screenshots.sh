#!/usr/bin/env bash
# Scripted-input screenshot test for pack ponies (Handcarts, issue #149), in test mode: you get every tech (Handcarts
# among them); the starting workers put a barracks up on open grass about 37 columns west of the town hall, a far
# site whose wood and stone have to be carried out to it. The town hall trains a pack pony, which comes out automatic and runs the
# wood out, 8 a run, while the builders build. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-pony-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/pony}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. Open grass to
# the west: a barracks at 94,390 is about 40 columns from the hall's middle, more than the 32 a site has its goods at hand.
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
camera 112,388 60 55 24
wait 5
key b
mouseworld 94,g,390
wait 2
clickworld 94,g,390
ticks 4
print
key escape
clickworld 131,g,384
wait 3
clickui train_pack_pony
mouse 800,200
ticks 2
print
key plus*2
ticks 650
camera 105,388 50 50 24
wait 5
print
shot $OUT/ui-pony-running.png
camera 95,392 22 40 16
wait 3
shot $OUT/ui-pony-site.png
ticks 3000
print
"
LOG="$OUT/ui-pony.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI pony test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" sites)" -eq 1 ] || fail "no barracks site placed: ${UI[0]}"
[ "$(field "${UI[0]}" due)" -eq 100 ] || fail "the far barracks should wait for its 80 wood and 20 stone: ${UI[0]}"
[ "$(field "${UI[1]}" building)" = town_hall ] && [ "$(field "${UI[1]}" queue)" -eq 1 ] || fail "no pony queued: ${UI[1]}"
[ "$(field "${UI[2]}" ponies)" -eq 1 ] || fail "the town hall did not train a pony: ${UI[2]}"
[ "$(field "${UI[2]}" due)" -lt 100 ] || fail "no wood has reached the site yet: ${UI[2]}"
[ "$(field "${UI[3]}" sites)" -eq 0 ] && [ "$(field "${UI[3]}" due)" -eq 0 ] || fail "the far barracks is not up: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI pony screenshot test OK: $OUT"
