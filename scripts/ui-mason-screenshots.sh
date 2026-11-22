#!/usr/bin/env bash
# Scripted-input screenshot test for the mason's yard: Better tools and then Masonry are researched from the tech
# tree, the starting workers put up a mason's yard beside the town hall from the build panel, and one is sent to work
# it: they bring it stone from the town hall (--goods gives some), it cuts the stone, and they carry the cut stone
# back, so cut stone shows in the top bar. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-mason-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/mason}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. A 4 by 4
# mason's yard fits east of the hall with its middle at 140,394 (where the economy test puts its house).
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
key k
wait 3
clickui tech_tools
mouse 1500,820
ticks 40
print
key k
ticks 900
key k
wait 3
clickui tech_masonry
mouse 1500,820
ticks 40
print
key k
ticks 900
print
clickui build_mason_yard
mouseworld 140,g,394
wait 3
print
shot $OUT/ui-mason-place.png
clickworld 140,g,394
ticks 4
print
key plus*2
ticks 900
print
clickworld 140,g,394 right
ticks 900
print
camera 138,392 40 50 16
clickworld 140,g,394
wait 5
shot $OUT/ui-mason-cutting.png
"
LOG="$OUT/ui-mason.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --goods=100 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI mason test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 8 ] || fail "expected 8 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected_workers)" -ge 3 ] || fail "the workers are not selected: ${UI[0]}"
[ "$(field "${UI[0]}" cut_stone)" -ge 100 ] || fail "--goods should give cut stone: ${UI[0]}"
START_CUT="$(field "${UI[0]}" cut_stone)"
[ "$(field "${UI[1]}" research)" = tools ] || fail "better tools not being researched: ${UI[1]}"
[ "$(field "${UI[2]}" research)" = masonry ] || fail "masonry not being researched: ${UI[2]}"
[ "$(field "${UI[3]}" research)" = none ] && [ "$(field "${UI[3]}" techs)" -ge 2 ] || fail "masonry not researched: ${UI[3]}"
[ "$(field "${UI[4]}" tool)" = Build ] && [ "$(field "${UI[4]}" ghost)" = Ok ] || fail "the mason's yard should fit beside the hall: ${UI[4]}"
[ "$(field "${UI[5]}" sites)" -eq 1 ] || fail "mason's yard site not placed: ${UI[5]}"
[ "$(field "${UI[6]}" sites)" -eq 0 ] && [ "$(field "${UI[6]}" mason_yards)" -eq 1 ] || fail "mason's yard not built: ${UI[6]}"
[ "$(field "${UI[7]}" masons)" -eq 1 ] || fail "one worker should work the mason's yard: ${UI[7]}"
[ "$(field "${UI[7]}" cut_stone)" -gt "$START_CUT" ] || fail "no stone was cut and stored: ${UI[7]} (started with $START_CUT)"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI mason screenshot test OK: $OUT"
