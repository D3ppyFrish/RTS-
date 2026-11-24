#!/usr/bin/env bash
# Scripted-input screenshot test for beast pens and the stampede (issue #152), in test mode playing the Orcs: a beast pen
# is put down on open grass with four of your wolves beside it. The wolves are box-selected and right-click the pen: they
# walk into its yard and are kept there. Then a knot of enemy spearmen is put down east of the pen, the pen is selected,
# and Ctrl+S drags a line from the pen through them (the spearmen are put down just before, so they have not wandered off): the wolves burst out and run along it, trampling the spearmen.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-stampede-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/stampede}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI stampede test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: open grass south-east of the town hall (152..176 x 393..412). The pen's yard covers 152..157 x
# 394..399; the wolves stand at 159..162, 404. The spearmen stand at 167..171, 396..398, on the line the stampede runs
# along, z = 397 from x = 158 to 180.
STEPS="
wait 20
building beast_pen 152,394
spawn wolf 159,404
spawn wolf 160,404
spawn wolf 161,404
spawn wolf 162,404
ticks 5
camera 158,400 0 55 30
wait 3
dragworld 158,g,403 163,g,405
wait 2
clickworld 154,g,396 right
ticks 120
print
camera 155,397 20 45 16
wait 5
shot $OUT/ui-pen-kept.png
camera 164,400 0 55 30
wait 3
clickworld 154,g,396
wait 2
key ctrl+s
wait 2
spawn spearman 167,397 1 1
spawn spearman 169,397 1 1
spawn spearman 168,398 1 1
spawn spearman 170,398 1 1
spawn spearman 171,397 1 1
dragworld 158,g,397 180,g,397
ticks 1
print
ticks 9
camera 167,398 20 42 16
wait 2
shot $OUT/ui-stampede-running.png
ticks 40
print
camera 172,398 330 45 22
wait 4
shot $OUT/ui-stampede-after.png
"
LOG="$OUT/ui-stampede.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=orcs,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" pens)" -eq 1 ] || fail "no beast pen: ${UI[0]}"
[ "$(field "${UI[0]}" penned)" -eq 4 ] || fail "the four wolves are not kept in the pen: ${UI[0]}"
[ "$(field "${UI[1]}" stampeding)" -eq 4 ] || fail "the wolves did not stampede: ${UI[1]}"
[ "$(field "${UI[1]}" penned)" -eq 0 ] || fail "wolves left in the pen: ${UI[1]}"
[ "$(field "${UI[2]}" trampled)" -ge 4 ] || fail "the stampede trampled too few spearmen: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI stampede screenshot test OK: $OUT"
