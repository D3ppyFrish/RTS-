#!/usr/bin/env bash
# Scripted-input screenshot test for Clan Kage's wall climbing. In test mode as Clan Kage with every tech: an enemy stone
# wall stands across open grass, three kunoichi are selected, the Climb button is pressed and the top of the wall
# clicked; they walk to its foot and climb its face with no ladder, and stand on top of it.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-climb-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/climb}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI climb test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Open grass south-east of the west town hall (152..176 x 393..412): the enemy's wall runs along x = 166 from z = 395 to 405.
WALL=""
for z in $(seq 395 405); do WALL+="building stone_wall 166,$z 1"$'\n'; done
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
${WALL}spawn kunoichi 158,399 3
ticks 40
camera 162,400 300 40 18
wait 3
dragworld 155,g,396 161,g,403
wait 2
print
clickui climb
wait 1
clickworld 166,g,400
ticks 30
print
camera 164,400 300 34 14
wait 3
shot $OUT/ui-climb-climbing.png
ticks 200
print
camera 164,400 300 34 14
wait 3
shot $OUT/ui-climb-on-wall.png
"
LOG="$OUT/ui-climb.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=kage,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" climbers)" -ge 1 ] || fail "no climber selected: ${UI[0]}"
[ "$(field "${UI[0]}" climbs)" -eq 0 ] || fail "a climb before the button: ${UI[0]}"
[ "$(field "${UI[1]}" climbing)" -ge 1 ] || fail "the Climb button sent nobody: ${UI[1]}"
[ "$(field "${UI[2]}" climbs)" -ge 1 ] || fail "nobody climbed the wall: ${UI[2]}"
[ "$(field "${UI[2]}" on_walls)" -ge 1 ] || fail "nobody stands on the wall: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI climb screenshot test OK: $OUT"
