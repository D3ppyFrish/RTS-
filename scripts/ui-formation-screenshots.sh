#!/usr/bin/env bash
# Scripted-input screenshot test for formations (issue #43). Seed 1234, a battle setup (--battle=16) at the
# map centre: you select your army, right-drag a line south-west of it (the places show while you drag),
# and the soldiers spread out along the line, melee in front and ranged behind, facing on from it. Then a
# plain right click moves the line on keeping its shape. Saves PNGs to OUT_DIR and fails if the UI state is
# not as expected.
# Usage: scripts/ui-formation-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI formation test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
# spread=min_x,min_z,max_x,max_z -> "width depth" in columns
size() { IFS=, read -r x0 z0 x1 z1 <<<"$1"; echo "$((x1 - x0 + 1)) $((z1 - z0 + 1))"; }

# Your army (16 soldiers of every type in turn) stands in two columns at x 241..242, z 211..225.
STEPS="
wait 20
camera 236,232 0 55 26
wait 5
dragworld 236,g,206 246,g,230
wait 2
print
mouseworld 226,g,238
down here right
mouseworld 230,g,238
mouseworld 236,g,238
mouseworld 242,g,238
wait 3
print
shot $OUT/ui-formation-drag.png
up here right
wait 2
ticks 320
print
shot $OUT/ui-formation-line.png
clickworld 234,g,246 right
ticks 260
print
shot $OUT/ui-formation-moved.png
"
LOG="$OUT/ui-formation.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --seed=1234 --focus=248,218 --battle=16 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" own)" -eq 16 ] || fail "the army was not selected: ${UI[0]}"
[ "$(field "${UI[1]}" line_preview)" -eq 16 ] || fail "no place shows for each unit while dragging the line: ${UI[1]}"
[ "$(field "${UI[2]}" formation)" -eq 16 ] && [ "$(field "${UI[2]}" line_preview)" -eq 0 ] \
  || fail "the units are not on a formation move after the drag: ${UI[2]}"
spread="$(field "${UI[2]}" spread)"
IFS=, read -r x0 z0 x1 z1 <<<"$spread"
read -r w d <<<"$(size "$spread")"
# The line runs west to east along z = 238 from x = 226 to 242: a wide, shallow block there (ranks behind it, north).
[ "$x0" -ge 224 ] && [ "$x1" -le 244 ] && [ "$w" -ge 10 ] || fail "the units did not spread out along the line: spread=$spread"
[ "$z1" -le 239 ] && [ "$z1" -ge 237 ] && [ "$d" -le 4 ] || fail "the units are not lined up on the line: spread=$spread"
read -r w2 d2 <<<"$(size "$(field "${UI[3]}" spread)")"
IFS=, read -r mx0 mz0 mx1 mz1 <<<"$(field "${UI[3]}" spread)"
[ "$mz1" -ge 244 ] && [ "$mz1" -le 249 ] || fail "the line did not move on to the click: ${UI[3]}"
[ "$((w2 - w))" -le 1 ] && [ "$((w - w2))" -le 1 ] && [ "$((d2 - d))" -le 1 ] && [ "$((d - d2))" -le 1 ] \
  || fail "the line lost its shape on a plain move: before $spread, after $(field "${UI[3]}" spread)"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI formation screenshot test OK: $OUT"
