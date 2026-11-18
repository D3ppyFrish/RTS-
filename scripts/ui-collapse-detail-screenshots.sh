#!/usr/bin/env bash
# Screenshot test for buildings coming down at character scale: two armies face off (--battle) and yours is
# sent at the enemy house. As it collapses its pieces fall with their fine detail (windows, frame, thatch),
# throw out chips where they land, and leave lumpy heaps of rubble. Saves PNGs to OUT_DIR and fails if the
# UI state is not as expected.
# Usage: scripts/ui-collapse-detail-screenshots.sh [OUT_DIR]
# CI: skip (close-up look checks, run by hand when the looks change)
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/collapse}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, map centre: with --battle=16 your army stands at x 241..242, z 211..225 and the enemy house
# at 248..251 x 204..207. Sent at it about tick 96, the army brings it down at about tick 288, which the
# shots a tick apart catch.
STEPS="
wait 20
camera 248,218 0 55 22
wait 5
dragworld 236,g,208 245,g,229
wait 2
clickworld 249,g,206 right
key plus*2
ticks 100
key minus*2
camera 249,207 340 40 12
wait 3
print
shot $OUT/ui-collapse-damaged.png
ticks 64
print
ticks 1
shot $OUT/ui-collapse-1.png
ticks 1
shot $OUT/ui-collapse-2.png
ticks 1
shot $OUT/ui-collapse-3.png
ticks 1
shot $OUT/ui-collapse-4.png
ticks 1
shot $OUT/ui-collapse-5.png
ticks 1
shot $OUT/ui-collapse-6.png
ticks 80
print
shot $OUT/ui-collapse-rubble.png
camera 249,206 200 30 7
wait 5
shot $OUT/ui-collapse-rubble-close.png
"
LOG="$OUT/ui-collapse.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --focus=248,218 --battle=16 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI collapse test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" enemy_buildings)" -eq 2 ] || fail "the enemy house fell too soon: ${UI[0]}"
[ "$(field "${UI[2]}" enemy_buildings)" -eq 1 ] || fail "the enemy house did not come down: ${UI[2]}"
[ "$(field "${UI[2]}" falling)" -eq 0 ] || fail "pieces are still falling: ${UI[2]}"
[ "$(field "${UI[2]}" dust)" -ge 1 ] || fail "no dust or chips where the house came down: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 9 ] || fail "missing screenshots"
echo "UI collapse screenshot test OK: $OUT"
