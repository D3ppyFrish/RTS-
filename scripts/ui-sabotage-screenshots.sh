#!/usr/bin/env bash
# Scripted-input screenshot test for Clan Kage's saboteurs. In test mode as Clan Kage with every tech: an enemy barracks
# stands on open grass, two kunoichi are selected, the Sabotage button is pressed and the barracks clicked; they slip up
# to it and work at it, and then it lies wrecked (its panel says it does no work for a while).
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-sabotage-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/sabotage}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI sabotage test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Open grass south-east of the west town hall (152..176 x 393..412): the enemy's barracks covers 166..171 x 394..398.
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
building barracks 166,394 1
spawn kunoichi 158,404 2
ticks 40
camera 163,400 330 40 18
wait 3
dragworld 156,g,402 161,g,406
wait 2
print
clickui sabotage
wait 1
clickworld 168,g,396
ticks 50
print
camera 166,399 330 38 14
wait 3
shot $OUT/ui-sabotage-working.png
ticks 160
clickworld 168,g,396
wait 2
print
camera 166,399 330 38 14
wait 3
shot $OUT/ui-sabotage-wrecked.png
"
LOG="$OUT/ui-sabotage.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=kage,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" saboteurs)" -ge 1 ] || fail "no saboteur selected: ${UI[0]}"
[ "$(field "${UI[0]}" sabotages)" -eq 0 ] || fail "a sabotage before the button: ${UI[0]}"
[ "$(field "${UI[1]}" sabotaging)" -ge 1 ] || fail "the Sabotage button sent nobody: ${UI[1]}"
[ "$(field "${UI[2]}" sabotages)" -ge 1 ] || fail "the barracks was not sabotaged: ${UI[2]}"
[ "$(field "${UI[2]}" stalled)" -ge 1 ] || fail "no building stalled: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI sabotage screenshot test OK: $OUT"
