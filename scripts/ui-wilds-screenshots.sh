#!/usr/bin/env bash
# Screenshot test for the bigger, wilder default map (issue #45), the one UI script on the default map
# (the others keep the 512 map their coordinates were written for). Looks at a patch of poison bog in the
# swamp, a stone giant in the hills (clicked, so its panel shows) and a herd of wild boar. Saves PNGs to
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
# two columns north of it), and a boar herd's den at 453,386.
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
for kind in boar stone_giant wolf; do [[ ",$lairs," == *",$kind:"* ]] || fail "no $kind lair on the map: $lairs"; done
[ "$(field "${UI[1]}" unit)" = stone_giant ] || fail "the stone giant was not selected: ${UI[1]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI wilds screenshot test OK: $OUT"
