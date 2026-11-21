#!/usr/bin/env bash
# Scripted-input screenshot test for gold: a gold lode in the hills north of the west start (bright yellow seams
# showing on the rock), the starting workers sent to dig it, and the gold they carry home counted in the top bar.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-gold-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/gold}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387, starting units in rows just south of it. A gold lode
# shows on the hills' rock about 155 blocks north, round 116..122,90,225..229.
STEPS="
wait 20
camera 119,227 20 62 24
wait 5
print
shot $OUT/ui-gold-lode.png
camera 131,389 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
camera 119,229 35 45 26
wait 5
clickworld 121,g,229 right
key plus*2
ticks 900
print
camera 119,227 20 68 16
wait 5
shot $OUT/ui-gold-digging.png
ticks 3000
print
"
LOG="$OUT/ui-gold.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI gold test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" gold)" -eq 0 ] || fail "nobody starts with gold: ${UI[0]}"
[ "$(field "${UI[1]}" mining_gold)" -ge 2 ] || fail "the workers should be mining gold: ${UI[1]}"
[ "$(field "${UI[2]}" gold)" -gt 0 ] || fail "no gold reached storage: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI gold screenshot test OK: $OUT"
