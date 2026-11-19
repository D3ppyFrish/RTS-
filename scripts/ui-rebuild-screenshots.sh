#!/usr/bin/env bash
# Scripted-input screenshot test for rebuild orders: rebuild orders are turned on (Shift+P) and every worker
# made automatic (Shift+J); two enemy sappers (--raid) knock down the house behind the town hall, which
# leaves a pale ghost over its rubble; the starting soldiers deal with the sappers, the automatic workers
# clear the rubble, and the house is placed again and built where it stood. Saves PNGs to OUT_DIR and fails
# if the UI state is not as expected.
# Usage: scripts/ui-rebuild-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/rebuild}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234, west start: town hall at 128..134 x 381..387; --houses=1 puts a house just north of it (129,372)
# and --raid=2 two enemy sappers beside it, already ordered to knock it down. The starting soldiers in
# front of the hall deal with them once they come into sight.
STEPS="
wait 20
camera 131,380 30 50 32
wait 5
key shift+p
key shift+j
ticks 2
print
shot $OUT/ui-rebuild-on.png
key plus*2
ticks 500
print
shot $OUT/ui-rebuild-ruin.png
ticks 2600
print
shot $OUT/ui-rebuild-again.png
"
LOG="$OUT/ui-rebuild.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --houses=1 --raid=2 --goods=200 --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot|Raid)" | tee "$LOG"

fail() { echo "UI rebuild test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
grep -q '^Raid: 2 sappers' "$LOG" || fail "no raid was set up"
[ "$(field "${UI[0]}" rebuild)" -eq 1 ] && [ "$(field "${UI[0]}" auto_all)" -eq 1 ] || fail "rebuild orders or automatic workers are not on: ${UI[0]}"
[ "$(field "${UI[0]}" buildings)" -eq 2 ] && [ "$(field "${UI[0]}" ruins)" -eq 0 ] || fail "expected the town hall and the house: ${UI[0]}"
[ "$(field "${UI[1]}" buildings)" -eq 1 ] && [ "$(field "${UI[1]}" ruins)" -eq 1 ] || fail "the house did not fall and leave a ruin: ${UI[1]}"
[ "$(field "${UI[2]}" buildings)" -eq 2 ] && [ "$(field "${UI[2]}" ruins)" -eq 0 ] || fail "the house was not put back: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI rebuild screenshot test OK: $OUT"
