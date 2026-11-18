#!/usr/bin/env bash
# Scripted-input screenshot test for quarries and mines (#50): the four starting workers put up a quarry from
# the build panel, then are sent to work it, and stone comes in from it with no stone dug anywhere. Saves PNGs
# to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-quarry-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 84..90 x 253..259, starting units in rows just south of it, and open
# ground at 94..98 x 271..275 for the quarry (the house's ground in ui-economy-screenshots.sh).
STEPS="
wait 20
camera 87,261 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
clickui build_quarry
mouseworld 96,g,273
wait 3
print
shot $OUT/ui-quarry-place.png
clickworld 96,g,273
ticks 4
print
key plus*2
ticks 400
print
clickworld 96,g,273 right
ticks 4
print
ticks 1200
print
shot $OUT/ui-quarry-working.png
"
LOG="$OUT/ui-quarry.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI quarry test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 6 ] || fail "expected 6 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected_workers)" -ge 3 ] || fail "the workers are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Build ] && [ "$(field "${UI[1]}" ghost)" = Ok ] || fail "no quarry to place: ${UI[1]}"
[ "$(field "${UI[2]}" sites)" -eq 1 ] || fail "quarry site not placed: ${UI[2]}"
[ "$(field "${UI[3]}" sites)" -eq 0 ] && [ "$(field "${UI[3]}" quarries)" -eq 1 ] || fail "quarry not built: ${UI[3]}"
[ "$(field "${UI[4]}" quarrying)" -eq 3 ] || fail "three workers should work the quarry: ${UI[4]}"
[ "$(field "${UI[5]}" stone)" -gt "$(field "${UI[4]}" stone)" ] || fail "no stone came in from the quarry: ${UI[5]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI quarry screenshot test OK: $OUT"
