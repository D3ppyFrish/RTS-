#!/usr/bin/env bash
# Screenshot test for tree sizes (--trees=N, see VoxelWorld.TreeScale; 2 is the default): the same views of seed
# 1234's west start at each tree size, so they can be put side by side. The town hall and starting units
# with the start's grove beside them from the usual camera, the grove close up and low, a forest further
# off, and the workers sent to cut the grove. Saves trees-<size>-<view>.png to OUT_DIR,
# and fails if no wood came in at any size.
# Usage: scripts/ui-bigtree-screenshots.sh [OUT_DIR] [SIZES]   (SIZES defaults to "1 2"; "1 2 3" adds the biggest)
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/bigtrees}")"
SIZES="${2:-1 2}"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

for s in $SIZES; do
STEPS="
wait 20
camera 131,389 45 55 22
wait 10
shot $OUT/trees-$s-town.png
camera 136,380 35 40 34
wait 10
shot $OUT/trees-$s-town-wide.png
camera 158,362 20 30 16
wait 10
shot $OUT/trees-$s-grove.png
camera 200,325 30 30 26
wait 10
shot $OUT/trees-$s-forest.png
camera 131,389 45 55 22
wait 10
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
print
camera 152,367 200 35 18
wait 10
clickworld 155,19,363 right
ticks 300
shot $OUT/trees-$s-workers.png
ticks 1600
print
"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --trees=$s --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" > "$OUT/ui-bigtree-$s.log" || true
grep -E "^(Saved|Input script)" "$OUT/ui-bigtree-$s.log" || true
fail() { echo "UI big tree test failed at size $s: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$OUT/ui-bigtree-$s.log" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$OUT/ui-bigtree-$s.log")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
wood0=$(field "${UI[0]}" wood); wood1=$(field "${UI[1]}" wood)
[ -n "$wood0" ] && [ -n "$wood1" ] && [ "$wood1" -gt "$wood0" ] || fail "no wood came in: wood $wood0 then $wood1"
echo "size $s: wood $wood0 -> $wood1"
done
echo "Big tree screenshots: $OUT"
