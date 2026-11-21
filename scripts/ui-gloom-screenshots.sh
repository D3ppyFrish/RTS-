#!/usr/bin/env bash
# Screenshot test for the shadowed wilds (see "The shadowed wilds" in docs/rules.md): the gloom forest round the
# middle of the default map, from above and at its edge, and an outcrop of shadow crystal.
# Saves PNGs to OUT_DIR and fails if the map has no wilds or the stalker dens are missing.
# Usage: scripts/ui-gloom-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/gloom}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234 on the 768 map: the wilds lie round 384,384 (the river runs through them, its middle ford too). Shade
# stalker dens are at 403,349, 446,346, 355,361 and 373,429 (ground about 85); outcrops of shadow crystal stand at
# 373,335, 450,360, 333,361, 373,400, 439,412 and 335,415. A boar herd's den in the forest just outside the wilds is
# at 453,386.
STEPS="
wait 20
key F1
print
camera 390,380 30 55 150
wait 20
shot $OUT/ui-gloom-wilds.png
camera 450,384 250 35 45
wait 15
shot $OUT/ui-gloom-edge.png
spawn shade_stalker 375,337 3 7
camera 374,337 200 50 20
wait 20
shot $OUT/ui-gloom-crystal.png
"
LOG="$OUT/ui-gloom.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --focus=390,380 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI gloom test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 1 ] || fail "expected 1 UI line, got ${#UI[@]}"
[ "$(field "${UI[0]}" wilds)" -eq 1 ] || fail "the default map should have shadowed wilds: ${UI[0]}"
[ "$(field "${UI[0]}" stalker_dens)" -eq 4 ] || fail "the wilds should have 4 shade stalker dens: ${UI[0]}"
[ "$(field "${UI[0]}" stalkers)" -eq 12 ] || fail "each den should start with a pack of 3: ${UI[0]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI gloom screenshot test OK: $OUT"
