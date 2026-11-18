#!/usr/bin/env bash
# CI: skip
# Screenshots for the bigger-trees experiment (--trees=N, see VoxelWorld.TreeScale): the same views of seed
# 1234's west start at each tree size, so they can be put side by side. The town hall and starting units
# with the start's grove beside them from the usual camera, the grove close up and low, a forest further
# off, and the workers sent to cut the grove. Saves trees-<size>-<view>.png to OUT_DIR.
# Usage: scripts/ui-bigtree-screenshots.sh [OUT_DIR] [SIZES]   (SIZES defaults to "1 2 3")
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/bigtrees}")"
SIZES="${2:-1 2 3}"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

for s in $SIZES; do
STEPS="
wait 20
camera 87,261 45 55 22
wait 10
shot $OUT/trees-$s-town.png
camera 92,252 35 40 34
wait 10
shot $OUT/trees-$s-town-wide.png
camera 106,241 20 30 16
wait 10
shot $OUT/trees-$s-grove.png
camera 140,200 30 30 26
wait 10
shot $OUT/trees-$s-forest.png
camera 87,261 45 55 22
wait 10
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
clickworld 103,20,242 right
ticks 300
camera 100,246 200 35 18
wait 10
shot $OUT/trees-$s-workers.png
"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --trees=$s --input="$STEPS" 2>&1 | grep -E "^(Saved|Screenshot|Input script)" || true
done
echo "Big tree screenshots: $OUT"
