#!/usr/bin/env bash
# Scripted-input screenshot test for dominion, the ancient sites' win: a match started with --dominion has a pale
# banner over the ruin south of the west start and a "Sites 0/6" button in the top bar. The starting units are
# drag-selected and sent into the ruin with a right click; the soldiers among them stand alone inside, so the site
# is being taken (a line over the banner says how far), and after 15 seconds it is theirs: the banner takes their
# colour, the top bar says "Sites 1/6" and an alert says so. Saves PNGs to OUT_DIR and fails if the UI state is not
# as expected.
# Usage: scripts/ui-dominion-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/dominion}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. The ruin nearest
# to the south has its middle at 176,539 (floor at level 19), about 160 blocks away.
STEPS="
wait 20
print
camera 176,539 30 35 14
wait 10
shot $OUT/ui-site-unheld.png
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
camera 176,539 30 35 14
wait 5
clickworld 176,g,539 right
ticks 2
ticks 500
wait 5
print
shot $OUT/ui-site-taking.png
ticks 400
wait 5
print
shot $OUT/ui-site-held.png
"
LOG="$OUT/ui-dominion.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --dominion --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI dominion test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" sites_active)" -eq 1 ] && [ "$(field "${UI[0]}" sites_held)" -eq 0 ] || fail "dominion should be on with no site held: ${UI[0]}"
[ "$(field "${UI[1]}" sites_claim)" -gt 0 ] && [ "$(field "${UI[1]}" sites_held)" -eq 0 ] || fail "the soldiers should be taking the site: ${UI[1]}"
[ "$(field "${UI[2]}" sites_held)" -eq 1 ] && [ "$(field "${UI[2]}" sites_leader)" -eq -1 ] || fail "the site should be held: ${UI[2]}"
[[ "$(field "${UI[2]}" alert_kinds)" == *SiteTaken* ]] || fail "taking it should come up as an alert: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI dominion screenshot test OK: $OUT"
