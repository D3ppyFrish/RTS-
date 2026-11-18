#!/usr/bin/env bash
# Scripted-input screenshot test for evolution: the game starts with --xp=150, so every starting unit of
# yours (Settlers) can take its first form and stands on a gold ring. The starting units are selected;
# the spearmen become knights (evolve button), the archers are set to become longbowmen on their own (the
# auto button) and do, and the other types take a form each. Then one knight is selected on its own.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-evolution-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 84..90 x 253..259, the 24 starting units (four of each of the six
# trained types, in turn: worker, spearman, archer, rider, sapper, mage) in rows from 82,260, so there is
# a spearman at 83,260.
STEPS="
wait 20
camera 87,261 45 55 22
wait 5
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
ticks 2
print
shot $OUT/ui-evolve-ready.png
clickui evolve_knight
ticks 2
print
clickui auto_evolve_archer
ticks 30
print
clickui evolve_foreman
ticks 2
clickui evolve_outrider
ticks 2
clickui evolve_demolisher
ticks 2
clickui evolve_seer
ticks 2
print
shot $OUT/ui-evolved.png
camera 86,261 20 40 9
wait 5
clickworld 83,g+1,260
wait 2
print
shot $OUT/ui-forms.png
"
LOG="$OUT/ui-evolution.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --xp=150 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

fail() { echo "UI evolution test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" own)" -eq 24 ] && [ "$(field "${UI[0]}" can_evolve)" -eq 24 ] && [ "$(field "${UI[0]}" evolved)" -eq 0 ] \
  || fail "the starting units are not all selected and ready to evolve: ${UI[0]}"
[ "$(field "${UI[1]}" evolved)" -eq 4 ] && [ "$(field "${UI[1]}" can_evolve)" -eq 20 ] \
  || fail "the spearmen did not become knights: ${UI[1]}"
[ "$(field "${UI[2]}" auto_evolve)" = archer:longbowman ] && [ "$(field "${UI[2]}" evolved)" -eq 8 ] \
  || fail "the archers did not become longbowmen on their own: ${UI[2]}"
[ "$(field "${UI[3]}" evolved)" -eq 24 ] && [ "$(field "${UI[3]}" can_evolve)" -eq 0 ] \
  || fail "not every unit took a form: ${UI[3]}"
[ "$(field "${UI[4]}" own)" -eq 1 ] && [ "$(field "${UI[4]}" unit)" = knight ] && [ "$(field "${UI[4]}" xp)" -eq 150 ] \
  || fail "the knight is not selected on its own, or lost its experience: ${UI[4]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI evolution screenshot test OK: $OUT"
