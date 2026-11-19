#!/usr/bin/env bash
# Screenshot test for the bigger, wilder default map (issue #45; the other UI scripts play on it too since
# issue #67). Looks at a patch of poison bog in the
# swamp, a stone giant in the hills (clicked, so its panel shows), a herd of wild boar, a pair of bog horrors
# in the swamp, a pair of giant crabs on a beach, and (issue #65) the cavern under the hills cut open by the
# height slice, with a cave crawler nest and the cave wyrm at its far end, and (issue #66) a sinkhole's pale
# crust in the grassland, and (issue #67) one of the old stone ruins in the open country. Saves PNGs to
# OUT_DIR and fails if the map is not the default size or its lairs are missing.
# Usage: scripts/ui-wilds-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/wilds}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234 on the 768 map: the starts are at 131,384 and 655,384. Poison bog lies around 472..496,192 in
# the swamp north of the river's middle; a stone giant's den is at 337,631 in the hills (the giant stands
# two columns north of it), a boar herd's den at 453,386, a bog horrors' den at 414,174 and a giant crabs'
# den at 103,67. Under the hills the cavern's crawler nests are at 351,625, 352,645 and 333,667 and the
# cave wyrm's den at 316,672, at levels 5 to 9 (the ground above is at about 26). A sinkhole's crust is
# centred on 365,350 (its top at 20). A ruin stands round 574,157 (its floor at 19).
STEPS="
wait 20
print
camera 484,192 30 40 30
wait 15
shot $OUT/ui-wilds-bog.png
camera 337,629 210 30 22
wait 15
clickworld 337,g,629
wait 3
print
shot $OUT/ui-wilds-giant.png
camera 453,386 30 35 22
wait 15
shot $OUT/ui-wilds-boars.png
camera 414,173 30 20 9
wait 15
shot $OUT/ui-wilds-horrors.png
camera 103,66 30 20 7
wait 15
shot $OUT/ui-wilds-crabs.png
camera 365,350 30 45 16
wait 15
shot $OUT/ui-wilds-sinkhole.png
camera 574,157 30 55 16
wait 15
shot $OUT/ui-wilds-ruin.png
camera 317,671 30 60 9
wait 5
key pagedown*20
wait 20
shot $OUT/ui-wilds-cavern.png
"
LOG="$OUT/ui-wilds.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --focus=484,192 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI wilds test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" world)" = 768x64x768 ] || fail "the default map is not 768 by 768: ${UI[0]}"
lairs="$(field "${UI[0]}" lair_kinds)"
for kind in boar stone_giant wolf bog_horror giant_crab cave_crawler cave_wyrm; do [[ ",$lairs," == *",$kind:"* ]] || fail "no $kind lair on the map: $lairs"; done
[ "$(field "${UI[1]}" unit)" = stone_giant ] || fail "the stone giant was not selected: ${UI[1]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 8 ] || fail "missing screenshots"
echo "UI wilds screenshot test OK: $OUT"
