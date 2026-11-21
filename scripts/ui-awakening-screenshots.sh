#!/usr/bin/env bash
# Scripted-input screenshot test for the Awakening, the dark power under the Heartscar: a match started with
# --awakening --awaken=2 has the dark power at its second stage from the first tick. The top bar's dark power button
# counts down to the next stage and alerts say it stirs and that broods rise; clicking the button looks at the
# Heartscar, where the corrupted ground has spread and the first broods (blight hounds and brutes) are clawing up. The
# match runs on and the broods head off for the town halls. Saves PNGs to OUT_DIR and fails if the UI state is not as
# expected.
# Usage: scripts/ui-awakening-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/awakening}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

STEPS="
wait 20
ticks 2
wait 5
print
clickui dark_power_button
mouse 800,700
wait 20
shot $OUT/ui-heartscar.png
ticks 200
wait 10
print
shot $OUT/ui-broods.png
"
LOG="$OUT/ui-awakening.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --awakening --awaken=2 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI awakening test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" dark_stage)" -eq 2 ] || fail "the dark power should be at its second stage: ${UI[0]}"
[ "$(field "${UI[0]}" broods)" -ge 1 ] || fail "broods should have risen: ${UI[0]}"
[[ "$(field "${UI[0]}" alert_kinds)" == *BroodRising* ]] || fail "the brood should come up as an alert: ${UI[0]}"
[ "$(field "${UI[1]}" dark_next)" -lt "$(field "${UI[0]}" dark_next)" ] || fail "the clock to the next stage should run: ${UI[1]}"
[ "$(field "${UI[1]}" brood_beasts)" -ge 1 ] || fail "the brood should still be out: ${UI[1]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI awakening screenshot test OK: $OUT"
