#!/usr/bin/env bash
# Scripted-input screenshot test for bleeding (issue #72): two armies face off at the map centre (--battle);
# yours attack-moves through the enemy, and wounded units drip blood as they fight and walk. A close look
# mid-fight (bleeding units with drops), then the stained field after it. Saves PNGs to OUT_DIR and fails if
# there is no blood or no one bled.
# Usage: scripts/ui-blood-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/blood}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, map centre: open ground at levels 21 and 22 (see ui-combat-screenshots.sh). With --battle=16
# your army stands at x 241..242, z 211..225 and the enemy's at x 254..255.
STEPS="
wait 20
camera 248,218 0 55 22
wait 5
dragworld 236,g,208 245,g,229
wait 2
clickworld 262,g,218 right ctrl
ticks 120
camera 249,218 20 40 12
wait 3
print
shot $OUT/ui-blood-fight.png
ticks 400
camera 249,218 20 55 18
wait 3
print
shot $OUT/ui-blood-field.png
"
LOG="$OUT/ui-blood.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --focus=248,218 --battle=16 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI blood test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" bleeding)" -gt 0 ] || fail "no one bleeds mid-fight: ${UI[0]}"
[ "$(field "${UI[0]}" blood)" -gt 10 ] || fail "no blood on the ground mid-fight: ${UI[0]}"
[ "$(field "${UI[1]}" blood)" -gt "$(field "${UI[0]}" blood)" ] || fail "the field did not get bloodier: ${UI[1]}"
echo "UI blood test passed"
