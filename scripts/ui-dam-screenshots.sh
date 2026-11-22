#!/usr/bin/env bash
# Scripted-input screenshot test for dams, in test mode: a channel of water two deep is dug east into open grass
# south-east of your town hall, and four workers are put down on its north bank. Shift+O draws a dam across its
# east end from bank to bank; the workers build it of rammed earth from the bed up, with a timber sluice in the
# middle. Then a dry channel is dug on from the dam: the dam holds the water back. The Open the dam's sluice
# button takes the boards out, and the water pours through into the dry channel. Saves PNGs to OUT_DIR and fails
# if the UI state is not as expected.
# Usage: scripts/ui-dam-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/dams}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387 and open grass south-east of it. The channel is
# 150..161 x 400..404, four deep with water two deep in it; the dam goes across x = 161 from the bank at
# z = 399 to the bank at z = 405 (5 columns of 4 blocks); the dry channel dug after it is 162..175.
STEPS="
wait 20
blocks 150,g-3,400 161,g-2,404 Water
ticks 2
blocks 150,g-1,400 161,g,404 Air
ticks 2
spawn worker 157,397 4
ticks 6
key f1
camera 163,402 180 40 15
wait 5
drag 920,450 1100,600
wait 2
camera 167,402 180 40 15
wait 3
wait 2
print
key shift+o
wait 2
mouseworld 161,g,399
down here
mouseworld 161,g,405
wait 3
print
shot $OUT/ui-dam-drag.png
up here
ticks 4
print
key plus*3
ticks 1200
key minus*3
wait 2
print
clickworld 158,g,396 right
ticks 100
blocks 162,g-3,400 175,g,404 Air
ticks 60
wait 5
print
shot $OUT/ui-dam-holding.png
clickui dam_sluice
ticks 4
print
ticks 240
wait 5
print
shot $OUT/ui-dam-open.png
"
LOG="$OUT/ui-dam.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI dam test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 7 ] || fail "expected 7 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" own)" -ge 4 ] || fail "the workers are not selected: ${UI[0]}"
[ "$(field "${UI[1]}" tool)" = Dam ] && [ "$(field "${UI[1]}" dam_top)" -gt 0 ] && [ "$(field "${UI[1]}" dam_blocks)" -eq 20 ] \
  || fail "the dam being drawn does not span the channel: ${UI[1]}"
[ "$(field "${UI[2]}" tool)" = Select ] && [ "$(field "${UI[2]}" dams)" -eq 1 ] && [ "$(field "${UI[2]}" dam_builders)" -gt 0 ] \
  || fail "nobody is building the dam: ${UI[2]}"
[ "$(field "${UI[3]}" dams)" -eq 0 ] && [ "$(field "${UI[3]}" dams_done)" -eq 1 ] && [ "$(field "${UI[3]}" dam_builders)" -eq 0 ] \
  || fail "the dam is not finished: ${UI[3]}"
[ "$(field "${UI[3]}" stone)" -le "$(( $(field "${UI[2]}" stone) - 15 ))" ] || fail "the dam cost no stone: ${UI[2]} / ${UI[3]}"
[ "$(field "${UI[4]}" near_dam)" -gt 0 ] && [ "$(field "${UI[4]}" sluice_open)" -eq 0 ] || fail "no shut sluice to open near the workers: ${UI[4]}"
[ "$(field "${UI[5]}" sluice_open)" -eq 1 ] || fail "the sluice did not open: ${UI[5]}"
[ "$(field "${UI[6]}" sluice_water)" -gt "$(( $(field "${UI[4]}" sluice_water) + 20 ))" ] || fail "no water poured through the sluice: ${UI[6]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI dam screenshot test OK: $OUT"
