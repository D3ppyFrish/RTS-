#!/usr/bin/env bash
# Scripted-input screenshot test for the shield wall (issue #145), in test mode: you get every tech (Drilled
# ranks among them); six of your spearmen are put down in a line on open grass, box-selected, and raise a shield
# wall with Ctrl+V; then four Orc archers are put down nine columns in front of them and shoot at the wall. A
# second Orc force of archers shoots at six spearmen of yours standing loose beside it, for comparison. Saves
# PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-shieldwall-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/shieldwall}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI shield wall test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: open grass south-east of the town hall (152..176 x 393..412). The wall stands at
# z = 396 from x = 154 to 164 and, with no enemy in sight when it is raised, faces +z (south), where the
# archers come; the loose spearmen stand at z = 396 from x = 168 to 178, with archers of their own to the south.
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
spawn spearman 154,396
spawn spearman 156,396
spawn spearman 158,396
spawn spearman 160,396
spawn spearman 162,396
spawn spearman 164,396
spawn spearman 168,396
spawn spearman 170,396
spawn spearman 172,396
spawn spearman 174,396
spawn spearman 176,396
spawn spearman 178,396
ticks 5
camera 166,402 0 55 30
wait 3
dragworld 152,g,394 165,g,398
wait 2
key ctrl+v
ticks 5
print
dragworld 167,g,394 180,g,398
wait 2
key v
ticks 3
dragworld 152,g,394 165,g,398
wait 2
print
spawn archer 156,405 1 1
spawn archer 159,405 1 1
spawn archer 162,405 1 1
spawn archer 164,405 1 1
spawn archer 170,405 1 1
spawn archer 173,405 1 1
spawn archer 176,405 1 1
spawn archer 178,405 1 1
ticks 140
dragworld 152,g,394 165,g,398
wait 2
camera 166,400 330 40 22
wait 5
print
shot $OUT/ui-shieldwall-fire.png
camera 159,397 20 30 10
wait 5
shot $OUT/ui-shieldwall-close.png
"
LOG="$OUT/ui-shieldwall.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" own)" -eq 6 ] || fail "the six spearmen of the wall are not selected: ${UI[0]}"
[ "$(field "${UI[0]}" shield_wall)" -eq 6 ] || fail "they did not raise the shield wall: ${UI[0]}"
[ "$(field "${UI[1]}" shield_wall)" -eq 6 ] || fail "the wall did not stay up when other units were given orders: ${UI[1]}"
[ "$(field "${UI[2]}" own)" -ge 5 ] || fail "the wall did not stand up to the archers: ${UI[2]}"
[ "$(field "${UI[2]}" shield_wall)" -eq "$(field "${UI[2]}" own)" ] || fail "spearmen left the wall under fire: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI shield wall screenshot test OK: $OUT"
