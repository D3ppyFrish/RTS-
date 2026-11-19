#!/usr/bin/env bash
# Scripted-input screenshot test for falling ground (issue #41): a stone ledge is put up by the start, held
# by one pillar at its end, and the terrain tool digs out the block joining them. The ledge falls as one
# piece, breaks apart in a cloud of dust as it hits the ground, and the pieces fly on and land as heaps of
# stone rubble. Saves PNGs to OUT_DIR and fails if the
# UI state is not as expected.
# Usage: scripts/ui-physics-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: open ground at 136..147 x 390..397, east of the town hall. The pillar stands at
# 92, 265 and the ledge (9 x 5, six blocks up) reaches east from its top.
STEPS="
wait 20
place 136,g+1,393 136,g+6,393
place 136,g+6,393 144,g+6,397
ticks 4
camera 140,395 30 30 16
wait 5
print
shot $OUT/ui-collapse-ledge.png
key t
clickworld 136,g,393
ticks 7
key space
wait 3
print
shot $OUT/ui-collapse-falling.png
key space
ticks 10
key space
wait 3
print
shot $OUT/ui-collapse-breaking.png
key space
ticks 60
print
shot $OUT/ui-collapse-rubble.png
"
LOG="$OUT/ui-physics.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI physics test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" falling)" -eq 0 ] || fail "something fell before the dig: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Terrain ] || fail "no terrain tool: ${UI[1]}"
[ "$(field "${UI[1]}" falling)" -eq 1 ] || fail "the ledge is not falling: ${UI[1]}"
[ "$(field "${UI[2]}" falling)" -ge 2 ] || fail "the ledge did not break into pieces as it hit: ${UI[2]}"
[ "$(field "${UI[2]}" dust)" -ge 1 ] || fail "no dust where the ledge hit: ${UI[2]}"
[ "$(field "${UI[3]}" falling)" -eq 0 ] || fail "the pieces have not come down: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 4 ] || fail "missing screenshots"
echo "UI physics screenshot test OK: $OUT"
