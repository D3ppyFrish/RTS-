#!/usr/bin/env bash
# Scripted-input screenshot test for the forester's lodge: the starting workers put one up on open grass south-west
# of the town from the build panel, then two are sent to work it. They plant saplings round it, and two minutes
# later those grow into trees. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-forester-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. Open grass at
# 120,400: a 4 by 4 lodge fits with the mouse on its middle.
STEPS="
wait 20
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
clickui build_forester_lodge
mouseworld 120,g,400
wait 3
print
clickworld 120,g,400
ticks 4
print
key plus*2
ticks 900
print
clickworld 120,g,400 right
ticks 1300
camera 120,400 45 55 24
wait 5
print
shot $OUT/ui-forester-saplings.png
ticks 2500
wait 5
print
shot $OUT/ui-forester-grown.png
"
LOG="$OUT/ui-forester.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI forester test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 6 ] || fail "expected 6 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected_workers)" -ge 3 ] || fail "the workers are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Build ] && [ "$(field "${UI[1]}" ghost)" = Ok ] || fail "the lodge should fit on open grass: ${UI[1]}"
[ "$(field "${UI[2]}" sites)" -eq 1 ] || fail "lodge site not placed: ${UI[2]}"
[ "$(field "${UI[3]}" sites)" -eq 0 ] && [ "$(field "${UI[3]}" quarries)" -eq 1 ] || fail "lodge not built: ${UI[3]}"
[ "$(field "${UI[4]}" saplings)" -ge 2 ] || fail "the foresters should have planted saplings: ${UI[4]}"
[ "$(field "${UI[5]}" trees_grown)" -ge 2 ] || fail "the saplings should have grown into trees: ${UI[5]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI forester screenshot test OK: $OUT"
