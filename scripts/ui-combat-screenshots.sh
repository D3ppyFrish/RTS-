#!/usr/bin/env bash
# Scripted-input screenshot test for combat: two armies face off west of the map centre (--battle); a sapper
# tunnels under an enemy house while the rest of your army, box-selected and recalled as a control group,
# attack-moves through the enemy; then a close look at the house with part of its wall fallen in. Saves
# PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-combat-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west of the map centre (the centre itself is the shadowed wilds' gloom forest): open, level
# ground. With --battle=16 your army stands in two ranks at x 253..254, z 409..423 (a sapper at 254,415),
# the enemy's at x 266..267, and the enemy house at 256..259 x 402..405. With the first, smaller trees (--trees=1).
STEPS="
wait 20
camera 260,416 0 55 22
wait 5
print
dragworld 248,g,406 257,g,427
wait 2
clickworld 254,g,415 shift
key ctrl+2
wait 2
print
clickworld 254,g,415
key x
wait 3
print
shot $OUT/ui-combat-tunnel-tool.png
clickworld 257,g,403
ticks 4
print
key 2
clickworld 274,g,420 right ctrl
ticks 50
print
shot $OUT/ui-combat-fight.png
key plus*2
ticks 700
print
shot $OUT/ui-combat-after.png
clickworld 258,g,403
camera 257,405 340 40 12
wait 5
print
shot $OUT/ui-combat-house.png
"
LOG="$OUT/ui-combat.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --trees=1 --focus=260,416 --battle=16 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI combat test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 7 ] || fail "expected 7 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" enemy_buildings)" -eq 2 ] || fail "no enemy house: ${UI[0]}"
[ "$(field "${UI[1]}" own)" -eq 15 ] || fail "the army without its sapper is not selected: ${UI[1]}"
[ "$(field "${UI[2]}" tool)" = Tunnel ] && [ "$(field "${UI[2]}" own)" -eq 1 ] || fail "no tunnel tool with the sapper: ${UI[2]}"
[ "$(field "${UI[3]}" tunnelling)" -eq 1 ] && [ "$(field "${UI[3]}" tool)" = Select ] || fail "the sapper is not tunnelling: ${UI[3]}"
[ "$(field "${UI[4]}" own)" -ge 12 ] && [ "$(field "${UI[4]}" fighting)" -ge 5 ] || fail "the army is not fighting: ${UI[4]}"
# A real battle: at least 8 units fell on the two sides together, 3 or more of them enemies. (Who wins an
# even fight depends on the frame the order lands on, which varies from run to run.)
fell=$(( $(field "${UI[0]}" units) + $(field "${UI[0]}" enemies) - $(field "${UI[5]}" units) - $(field "${UI[5]}" enemies) ))
[ "$fell" -ge 8 ] && [ "$(field "${UI[5]}" enemies)" -le "$(( $(field "${UI[0]}" enemies) - 3 ))" ] || fail "no real battle: ${UI[5]}"
[ "$(field "${UI[5]}" tunnelling)" -eq 0 ] || fail "the tunnel is not finished: ${UI[5]}"
[ "$(field "${UI[6]}" building)" = house ] || fail "the house is not selected: ${UI[6]}"
h="$(field "${UI[6]}" health)"
[ "$h" -gt 0 ] && [ "$h" -lt 298 ] || fail "the tunnel did not bring down part of the house: ${UI[6]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 4 ] || fail "missing screenshots"
echo "UI combat screenshot test OK: $OUT"
