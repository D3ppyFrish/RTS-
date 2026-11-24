#!/usr/bin/env bash
# Scripted-input screenshot test for Molting (issue #153), in test mode playing the Deepfolk: you get every tech
# (Molting among them). Four skitterlings and three spitters are put down on open grass, sent walking west (so none
# molts by itself, as an idle one would), and wounded down to a fifth of their life (test mode's Wound). Selected,
# Ctrl+M has them molt: they stop and shudder, pale, healing back to full; after 15 seconds they are whole again but
# soft (still pale) and walk on. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-molt-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/molt}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI molt test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: open grass south-east of the town hall (152..176 x 393..412). The hive units stand at
# 166..172 x 398..402 and are sent west to 152,400.
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
spawn skitterling 168,398 4
spawn spitter 170,402 3
ticks 10
camera 166,400 330 40 16
wait 3
dragworld 164,g,395 174,g,405
wait 2
clickworld 152,g,400 right
ticks 2
clickui test_tab_units
clickui test_wound
ticks 2
print
key ctrl+m
ticks 3
print
ticks 60
wait 3
camera 164,400 330 35 12
wait 3
shot $OUT/ui-molt-molting.png
ticks 260
print
ticks 20
camera 160,400 330 35 14
wait 3
shot $OUT/ui-molt-soft.png
"
LOG="$OUT/ui-molt.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=deepfolk,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" molting)" -eq 0 ] || fail "units molted before the order (walking ones should not): ${UI[0]}"
[ "$(field "${UI[1]}" molting)" -ge 5 ] || fail "the wounded hive units did not molt on Ctrl+M: ${UI[1]}"
[ "$(field "${UI[2]}" molting)" -eq 0 ] || fail "still molting after 15 seconds: ${UI[2]}"
[ "$(field "${UI[2]}" soft)" -ge 5 ] || fail "the molted units are not soft: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI molt screenshot test OK: $OUT"
