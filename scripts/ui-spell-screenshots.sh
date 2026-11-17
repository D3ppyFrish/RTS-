#!/usr/bin/env bash
# Scripted-input screenshot test for terrain magic: your army (--battle) is box-selected, the Earth Wall tool
# (Shift+Y) is drawn as a line between the two armies and a mage raises a wall of earth along it. A second
# wall goes up from the next mage, then both crumble back into the ground when their time is up. Saves PNGs to
# OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-spell-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/spells}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, map centre: open ground at levels 21 and 22. With --battle=16 your army stands in two ranks
# at x 241..242, z 211..225, the enemy's at x 254..255 (they stand their ground until something comes near).
# The first wall runs north to south at x 248, the second at x 250, both well within a mage's reach.
STEPS="
wait 20
camera 248,218 300 50 20
wait 5
dragworld 236,g,208 245,g,229
wait 2
print
key shift+y
wait 2
mouseworld 248,g,214
down here
mouseworld 248,g,221
wait 3
print
shot $OUT/ui-earth-wall-drag.png
up here
ticks 4
print
wait 3
shot $OUT/ui-earth-wall-raised.png
key shift+y
wait 2
dragworld 250,g,213 250,g,222
ticks 4
print
camera 248,218 210 40 22
wait 5
shot $OUT/ui-earth-wall-behind.png
ticks 920
print
camera 248,218 300 50 26
wait 5
shot $OUT/ui-earth-wall-crumbled.png
"
LOG="$OUT/ui-spell.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --focus=248,218 --battle=16 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI spell test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
# The army is selected, with at least two mages among it, both ready.
[ "$(field "${UI[0]}" own)" -ge 12 ] || fail "the army is not selected: ${UI[0]}"
[ "$(field "${UI[0]}" casters)" -ge 2 ] && [ "$(field "${UI[0]}" caster_ready)" -eq 1 ] || fail "no ready mages selected: ${UI[0]}"
# Dragging the tool previews the line's 8 columns; nothing has risen yet.
[ "$(field "${UI[1]}" tool)" = EarthWall ] || fail "the Earth Wall tool is not on: ${UI[1]}"
[ "$(field "${UI[1]}" wall_preview)" -eq 8 ] || fail "the preview does not show the line: ${UI[1]}"
[ "$(field "${UI[1]}" earth_walls)" -eq 0 ] || fail "earth rose before letting go: ${UI[1]}"
# Letting go raises the wall: 8 columns, 3 blocks each, and the tool is put away.
[ "$(field "${UI[2]}" earth_walls)" -eq 1 ] && [ "$(field "${UI[2]}" earth_blocks)" -eq 24 ] || fail "the wall did not rise: ${UI[2]}"
[ "$(field "${UI[2]}" tool)" = Select ] || fail "the tool stayed on: ${UI[2]}"
# The next mage raises a second wall.
[ "$(field "${UI[3]}" earth_walls)" -eq 2 ] && [ "$(field "${UI[3]}" earth_blocks)" -eq 48 ] || fail "the second wall did not rise: ${UI[3]}"
# Both crumble when their time is up.
[ "$(field "${UI[4]}" earth_walls)" -eq 0 ] && [ "$(field "${UI[4]}" earth_blocks)" -eq 0 ] || fail "the walls did not crumble: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 4 ] || fail "missing screenshots"
echo "UI spell screenshot test OK: $OUT"
