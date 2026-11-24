#!/usr/bin/env bash
# Scripted-input screenshot test for orchards (Crop rotation, issue #148), in test mode: you get every tech (Crop
# rotation among them); the starting workers put an orchard up on open grass south-west of the town from the build
# panel. It is planted as saplings, which grow into fruit trees four minutes later. Saves PNGs to OUT_DIR and fails if
# the UI state is not as expected.
# Usage: scripts/ui-orchard-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/orchard}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. Open grass at
# 120,400 (where the forester's lodge test puts its lodge): a 6 by 6 orchard fits with the mouse on its middle.
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
print
clickui build_orchard
mouseworld 120,g,400
wait 3
print
clickworld 120,g,400
ticks 4
print
key plus*2
ticks 900
camera 120,400 40 50 20
wait 5
print
shot $OUT/ui-orchard-saplings.png
ticks 4900
wait 5
print
shot $OUT/ui-orchard-grown.png
camera 120,400 20 30 14
wait 5
shot $OUT/ui-orchard-close.png
"
LOG="$OUT/ui-orchard.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI orchard test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected_workers)" -ge 3 ] || fail "the workers are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Build ] && [ "$(field "${UI[1]}" ghost)" = Ok ] || fail "the orchard should fit on open grass: ${UI[1]}"
[ "$(field "${UI[2]}" sites)" -eq 1 ] || fail "orchard site not placed: ${UI[2]}"
[ "$(field "${UI[3]}" sites)" -eq 0 ] && [ "$(field "${UI[3]}" orchards)" -eq 1 ] && [ "$(field "${UI[3]}" orchards_grown)" -eq 0 ] \
  || fail "orchard not built, or grown too soon: ${UI[3]}"
[ "$(field "${UI[4]}" orchards_grown)" -eq 1 ] || fail "the orchard should have grown: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI orchard screenshot test OK: $OUT"
