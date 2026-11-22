#!/usr/bin/env bash
# Scripted-input screenshot test for carts: the starting workers build a storehouse a little way from the town
# hall; it is told to keep 50 stone (two clicks on its Stone button); the town hall is selected and trains a cart
# (paid for in part with planks from --goods), which comes out automatic, takes supply duty at the storehouse on
# its own and hauls the stone over from the town hall 20 at a time, until it holds what it should. No worker is
# made automatic, so only the cart hauls. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-cart-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/cart}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it, open
# ground for a storehouse to the south-east at 140..144 x 394..398 (its roof is at 1100,600 on screen from this
# camera, as in the supply test). --goods=200 pays for it and the cart, and fills the town hall with stone.
STEPS="
wait 20
camera 136,390 40 55 26
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
key g
clickworld 142,g,396
ticks 4
print
key plus*2
ticks 700
click 1100,600
wait 3
clickui supply_stone
clickui supply_stone
mouse 800,200
ticks 2
print
key escape
clickworld 131,g,384
wait 3
clickui train_cart
mouse 800,200
ticks 2
print
shot $OUT/ui-cart-train.png
ticks 900
print
shot $OUT/ui-cart-hauling.png
click 1100,600
wait 3
ticks 1500
print
shot $OUT/ui-cart-stocked.png
"
LOG="$OUT/ui-cart.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --goods=200 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI cart test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" sites)" -eq 1 ] || fail "no storehouse site placed: ${UI[0]}"
[ "$(field "${UI[0]}" carts)" -eq 0 ] || fail "nobody starts with a cart: ${UI[0]}"
[ "$(field "${UI[1]}" building)" = storehouse ] || fail "the storehouse is not selected: ${UI[1]}"
[ "$(field "${UI[1]}" supply)" = "Stone:0/50" ] || fail "the supply rule is not set: ${UI[1]}"
[ "$(field "${UI[2]}" building)" = town_hall ] || fail "the town hall is not selected: ${UI[2]}"
[ "$(field "${UI[2]}" queue)" -eq 1 ] || fail "no cart is queued: ${UI[2]}"
[ "$(field "${UI[3]}" carts)" -eq 1 ] || fail "the town hall did not train a cart: ${UI[3]}"
[ "$(field "${UI[3]}" carts_on_supply)" -eq 1 ] || fail "the cart did not go on supply duty: ${UI[3]}"
# Its loads are 20, 20 and the last 10 (only what is still missing); a worker's would be 5.
[ "$(field "${UI[3]}" cart_load)" -ge 10 ] || fail "the cart is not hauling a cart's load: ${UI[3]}"
[ "$(field "${UI[3]}" on_supply)" -eq "$(field "${UI[3]}" carts_on_supply)" ] || fail "someone but the carts is on supply duty: ${UI[3]}"
[ "$(field "${UI[4]}" building)" = storehouse ] || fail "the storehouse is not selected at the end: ${UI[4]}"
[ "$(field "${UI[4]}" supply)" = "Stone:50/50" ] || fail "the cart did not stock the storehouse: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI cart screenshot test OK: $OUT"
