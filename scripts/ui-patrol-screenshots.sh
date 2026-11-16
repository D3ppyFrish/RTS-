#!/usr/bin/env bash
# Scripted-input screenshot test for patrols: your army (--battle) is box-selected and sent on a patrol with
# Alt+right click to a point behind it; it walks there, turns and comes back, and is still patrolling a
# while later. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-patrol-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/patrol}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, map centre: open ground at levels 21 and 22. With --battle=16 your army stands in two ranks
# at x 241..242, z 211..225, the enemy's at x 254..255 (they stand their ground until something comes near).
STEPS="
wait 20
camera 248,218 0 55 22
wait 5
dragworld 236,g,208 245,g,229
wait 2
print
clickworld 234,g,228 right alt
ticks 30
print
shot $OUT/ui-patrol-out.png
ticks 300
print
shot $OUT/ui-patrol-later.png
"
LOG="$OUT/ui-patrol.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --focus=248,218 --battle=16 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI patrol test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
own="$(field "${UI[0]}" own)"
[ "$own" -ge 12 ] || fail "the army is not selected: ${UI[0]}"
[ "$(field "${UI[0]}" patrolling)" -eq 0 ] || fail "patrolling before the order: ${UI[0]}"
# Every soldier selected patrols (the sapper too: it can fight); they keep at it after turning round.
[ "$(field "${UI[1]}" patrolling)" -ge 12 ] || fail "the army is not patrolling: ${UI[1]}"
[ "$(field "${UI[2]}" patrolling)" -ge 12 ] || fail "the army stopped patrolling: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI patrol screenshot test OK: $OUT"
