#!/usr/bin/env bash
# Close-up look check for the sculpted, finely detailed models (game/scripts/Detail/Sculpted.cs): --showcase
# stands a stone giant (Sawyer's Meshy AI model), an orc spearman, an orc worker, two wolves, a wolf rider and
# two knights and a row of troops imported from GLBs (Meshy AI) together, and the shots go from far (plain models) through the middle level to the finest voxels.
# Saves PNGs to OUT_DIR, and the same shots with detail off (the plain models) as *-plain.png.
# Usage: scripts/ui-sculpt-screenshots.sh [OUT_DIR]
# CI: skip (close-up look checks, run by hand when the looks change)
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/sculpt}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234: open grass round block 106,279, with the camera to the south of it.
shots() {
  local suffix=$1
  cat <<EOF
wait 20
camera 106,281 15 22 12
wait 5
shot $OUT/sculpt-group$suffix.png
camera 106,283 20 15 9
wait 5
shot $OUT/sculpt-giant$suffix.png
camera 104,284 30 18 6
wait 5
shot $OUT/sculpt-spearman$suffix.png
camera 107,285 -25 18 6
wait 5
shot $OUT/sculpt-rider$suffix.png
camera 106,290 -8 14 8
wait 5
shot $OUT/sculpt-imported$suffix.png
camera 106,281 15 35 45
wait 5
shot $OUT/sculpt-far$suffix.png
EOF
}
run() {
  xvfb-run -a -s "-screen 0 1600x900x24" \
    godot --path game --rendering-driver opengl3 -- --world=512x64x512 --units=0 --focus=100,276 --showcase=106,279 "$@" 2>&1 \
    | grep -E "^(Saved|Input script|Showcase|Detailed units)"
}
LOG="$OUT/ui-sculpt.log"
{ run --input="$(shots "")"; run --detail=off --input="$(shots -plain)"; } | tee "$LOG"

fail() { echo "UI sculpt test failed: $1" >&2; exit 1; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 12 ] || fail "missing screenshots"
echo "UI sculpt screenshot test OK: $OUT"
