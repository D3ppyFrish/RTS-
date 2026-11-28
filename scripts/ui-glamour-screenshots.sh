#!/usr/bin/env bash
# Scripted-input screenshot test for the Fey's Glamour. In test mode as the Fey with every tech: two enchanters stand
# on open grass with Settler spearmen coming at them, the enchanters are selected, the Glamour button weaves illusory
# sprites round them (shimmering, to their own side), and the spearmen fall on the decoys, which fade at the first blow.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-glamour-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/glamour}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI glamour test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Open grass south-east of the west town hall (152..176 x 393..412).
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
spawn enchanter 160,400 2
ticks 20
camera 160,401 330 40 14
wait 3
dragworld 157,g,397 163,g,403
wait 2
print
clickui glamour
ticks 3
print
camera 160,401 330 38 12
wait 3
shot $OUT/ui-glamour-woven.png
spawn spearman 170,401 3 1
ticks 120
print
camera 162,401 330 40 14
wait 3
shot $OUT/ui-glamour-decoys.png
"
LOG="$OUT/ui-glamour.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=fey,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected)" -ge 1 ] || fail "no enchanter selected: ${UI[0]}"
[ "$(field "${UI[0]}" illusions_woven)" -eq 0 ] || fail "illusions woven before the button: ${UI[0]}"
[ "$(field "${UI[1]}" illusions_woven)" -ge 3 ] || fail "the Glamour button wove nothing: ${UI[1]}"
[ "$(field "${UI[1]}" illusions)" -ge 3 ] || fail "no illusions standing: ${UI[1]}"
[ "$(field "${UI[2]}" illusions_broken)" -ge 1 ] || fail "no illusion ended by a blow: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI glamour screenshot test OK: $OUT"
