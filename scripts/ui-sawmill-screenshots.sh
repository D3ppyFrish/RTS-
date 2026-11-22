#!/usr/bin/env bash
# Scripted-input screenshot test for the sawmill: the starting workers put up a sawmill beside the town hall from
# the build panel, and one is sent to work it: they bring it wood from the town hall, it saws the wood into planks,
# and they carry the planks back, so planks show in the top bar. Saves PNGs to OUT_DIR and fails if the UI state is
# not as expected.
# Usage: scripts/ui-sawmill-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/sawmill}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. A 4 by 4
# sawmill fits east of the hall with its middle at 140,394 (where the economy test puts its house).
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
clickui build_sawmill
mouseworld 140,g,394
wait 3
print
shot $OUT/ui-sawmill-place.png
clickworld 140,g,394
ticks 4
print
key plus*2
ticks 700
print
clickworld 140,g,394 right
ticks 900
print
camera 138,392 40 50 16
clickworld 140,g,394
wait 5
shot $OUT/ui-sawmill-sawing.png
"
LOG="$OUT/ui-sawmill.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI sawmill test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected_workers)" -ge 3 ] || fail "the workers are not selected: ${UI[0]}"
[ "$(field "${UI[0]}" planks)" -eq 0 ] || fail "nobody starts with planks: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Build ] && [ "$(field "${UI[1]}" ghost)" = Ok ] || fail "the sawmill should fit beside the hall: ${UI[1]}"
[ "$(field "${UI[2]}" sites)" -eq 1 ] || fail "sawmill site not placed: ${UI[2]}"
[ "$(field "${UI[3]}" sites)" -eq 0 ] && [ "$(field "${UI[3]}" sawmills)" -eq 1 ] || fail "sawmill not built: ${UI[3]}"
WOOD="$(field "${UI[3]}" wood)"
[ "$(field "${UI[4]}" sawyers)" -eq 1 ] || fail "one worker should work the sawmill: ${UI[4]}"
[ "$(field "${UI[4]}" planks)" -gt 0 ] || fail "no planks were sawn and stored: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI sawmill screenshot test OK: $OUT (wood $WOOD before sawing)"
