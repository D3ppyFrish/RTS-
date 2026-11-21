#!/usr/bin/env bash
# Scripted-input screenshot test for floods: in test mode a pond is dug and filled beside a dry pit three
# deep, with nine spearmen standing in the pit. The bank between them is dug through: the water pours in and
# fills the pit to the brim. The spearmen near the edge are swept out onto the ground beside it, hurt; the
# ones in the middle, too far from any edge, drown, and you hear of it ("drowned in a flood"). Saves PNGs to
# OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-flood-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: flat open ground south-east of the town hall. The pond is
# 153..160 x 397..407, two deep; the bank is x 161; the pit is 162..172 x 397..407, three deep, and the
# spearmen stand round 169,402: those swept to ground within 3 columns live, the rest drown.
STEPS="
wait 20
blocks 153,g-1,397 160,g,407 Water
blocks 162,g-2,397 172,g,407 Air
ticks 2
spawn spearman 169,402 9
ticks 4
camera 164,402 210 42 26
wait 5
print
shot $OUT/ui-flood-dry.png
blocks 161,g-1,400 161,g,404 Air
ticks 60
print
shot $OUT/ui-flood-pouring.png
ticks 400
print
shot $OUT/ui-flood-after.png
"
LOG="$OUT/ui-flood.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI flood test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" swept)" -eq 0 ] || fail "someone was swept before the bank was dug: ${UI[0]}"
[ "$(field "${UI[2]}" swept)" -ge 1 ] || fail "nobody was swept out of the flood: ${UI[2]}"
[[ "$(field "${UI[2]}" alert_kinds)" == *UnitLostToHazard* ]] || fail "no alert for a spearman drowned: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI flood screenshot test OK: $OUT"
