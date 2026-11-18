#!/usr/bin/env bash
# Screenshot test for telling troops apart (issue #45): --lineup stands every unit type of both factions in
# two rows south of the west start, Settlers (blue) north and Orcs (red) south. Close-ups show each row;
# far shots show the type badges over every unit, which appear only when the camera is far out.
# Saves PNGs to OUT_DIR and fails if the badges are not as expected.
# Usage: scripts/ui-lineup-screenshots.sh [OUT_DIR]
# CI: skip (close-up look checks, run by hand when the looks change)
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/units}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234: focus 100,276, so the Settlers' 24 types stand at z=267, x=64..133 (every third column), and
# the Orcs' 11 types at z=285, x=84..114. They hold their ground, so nobody walks off.
STEPS="
wait 20
camera 75,268 25 25 9
wait 5
print
shot $OUT/units-settlers-1.png
camera 99,268 25 25 9
wait 5
shot $OUT/units-settlers-2.png
camera 122,268 25 25 9
wait 5
shot $OUT/units-settlers-3.png
camera 92,286 25 25 9
wait 5
shot $OUT/units-orcs-1.png
camera 106,288 0 40 10
wait 5
shot $OUT/units-orcs-2.png
camera 100,277 0 45 55
wait 5
print
shot $OUT/units-both.png
camera 100,277 0 55 95
wait 10
print
shot $OUT/units-icons.png
camera 100,277 0 60 180
wait 10
shot $OUT/units-icons-far.png
"
LOG="$OUT/ui-lineup.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --lineup --focus=100,276 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script|Lineup)" | tee "$LOG"

fail() { echo "UI lineup test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
! grep -q '^Lineup: no room' "$LOG" || fail "a unit type has no place in the lineup: $(grep '^Lineup: no room' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" icons)" -eq 0 ] || fail "badges show close up: ${UI[0]}"
[ "$(field "${UI[1]}" icons)" -eq 0 ] || fail "badges show at the usual distance: ${UI[1]}"
[ "$(field "${UI[2]}" icons)" -ge 35 ] || fail "badges do not show over every unit far out: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 8 ] || fail "missing screenshots"
echo "UI lineup screenshot test OK: $OUT"
