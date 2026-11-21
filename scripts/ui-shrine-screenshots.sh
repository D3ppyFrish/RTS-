#!/usr/bin/env bash
# Scripted-input screenshot test for crystal and mana: Basic magic is researched from the tech tree, the starting
# workers put up a shrine beside the town hall from the build panel, and one is sent to keep it: it brings the
# shrine crystal from the town hall (--goods gives some), and the mana in the top bar rises as the shrine channels
# it. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-shrine-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/shrine}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. A 3 by 3
# shrine fits east of the hall with its middle at 140,394 (where the economy test puts its house).
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
clickui tech_basic_magic
mouse 1500,820
ticks 40
print
key k
ticks 900
print
clickui build_shrine
mouseworld 140,g,394
wait 3
print
shot $OUT/ui-shrine-place.png
clickworld 140,g,394
ticks 4
print
key plus*2
ticks 700
print
clickworld 140,g,394 right
ticks 600
print
camera 140,392 45 50 16
wait 5
shot $OUT/ui-shrine-channelling.png
"
LOG="$OUT/ui-shrine.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --goods=100 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI shrine test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 7 ] || fail "expected 7 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected_workers)" -ge 3 ] || fail "the workers are not selected: ${UI[0]}"
[ "$(field "${UI[0]}" crystal)" -ge 100 ] || fail "--goods should give crystal: ${UI[0]}"
START_MANA="$(field "${UI[0]}" mana)"
[ "$(field "${UI[1]}" research)" = basic_magic ] || fail "basic magic not being researched: ${UI[1]}"
[ "$(field "${UI[2]}" research)" = none ] && [ "$(field "${UI[2]}" techs)" -ge 1 ] || fail "basic magic not researched: ${UI[2]}"
[ "$(field "${UI[3]}" tool)" = Build ] && [ "$(field "${UI[3]}" ghost)" = Ok ] || fail "the shrine should fit beside the hall: ${UI[3]}"
[ "$(field "${UI[4]}" sites)" -eq 1 ] || fail "shrine site not placed: ${UI[4]}"
[ "$(field "${UI[5]}" sites)" -eq 0 ] && [ "$(field "${UI[5]}" shrines)" -eq 1 ] || fail "shrine not built: ${UI[5]}"
[ "$(field "${UI[6]}" keepers)" -eq 1 ] || fail "one worker should keep the shrine: ${UI[6]}"
[ "$(field "${UI[6]}" mana)" -gt "$START_MANA" ] || fail "the shrine channelled no mana: ${UI[6]} (started with $START_MANA)"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI shrine screenshot test OK: $OUT"
