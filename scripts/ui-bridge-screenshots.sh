#!/usr/bin/env bash
# Scripted-input screenshot test for bridges: the starting units are selected and ' draws a bridge across the
# ravine north-east of the west start. The workers walk there and lay the deck from the near bank, a column at
# a time. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-bridge-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/bridges}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. The deep
# river crosses z = 300 between x = 415 and 427 (north of the shadowed wilds and their shade stalker dens), its
# banks level at x = 413 and 429: the deck spans it. The units walk north to 150,300 first, so the builders keep
# well clear of the wolf den 40 columns east of the start.
STEPS="
wait 20
camera 141,392 30 50 20
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
clickworld 150,g,300 right
ticks 600
camera 421,300 10 45 18
wait 5
key apostrophe
wait 2
mouseworld 413,g,300
down here
mouseworld 429,g,300
wait 3
print
shot $OUT/ui-bridge-drag.png
up here
ticks 4
print
key plus*3
ticks 1500
wait 5
shot $OUT/ui-bridge-building.png
ticks 3000
wait 5
print
shot $OUT/ui-bridge-done.png
"
LOG="$OUT/ui-bridge.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI bridge test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" own)" -gt 8 ] || fail "the starting units are not selected: ${UI[0]}"
PLAN="$(field "${UI[1]}" wall_preview)"
[ "$(field "${UI[1]}" tool)" = Bridge ] && [ "$PLAN" -ge 6 ] || fail "the bridge being drawn has no deck across the river: ${UI[1]}"
[ "$(field "${UI[2]}" tool)" = Select ] && [ "$(field "${UI[2]}" bridges)" -eq "$PLAN" ] || fail "the bridge laid is not the one drawn: ${UI[2]}"
[ "$(field "${UI[3]}" bridges)" -eq "$PLAN" ] && [ "$(field "${UI[3]}" sites)" -eq 0 ] || fail "the bridge is not finished: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI bridge screenshot test OK: $OUT"
