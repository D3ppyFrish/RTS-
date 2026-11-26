#!/usr/bin/env bash
# Scripted-input screenshot test for the start screen's map size. Large is picked (1024 by 1024 blocks) and the
# match starts on a world that size, with more monster lairs spread across it than the standard map has; the camera
# looks out over the land from above the town. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-mapsize-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/mapsize}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI map size test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

STEPS="
wait 10
print
select start_size 1
wait 3
print
shot $OUT/ui-mapsize-start-screen.png
clickui start_button
waitmatch
wait 10
print
camera 512,512 0 75 420
wait 20
shot $OUT/ui-mapsize-large.png
"
LOG="$OUT/ui-mapsize.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --menu --seed=1234 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" size)" = 768 ] || fail "the start screen does not start on the standard map: ${UI[0]}"
[ "$(field "${UI[1]}" size)" = 1024 ] || fail "Large was not picked: ${UI[1]}"
[ "$(field "${UI[2]}" screen)" = match ] || fail "the match did not start: ${UI[2]}"
[ "$(field "${UI[2]}" world)" = 1024x128x1024 ] || fail "the world is not the large map: ${UI[2]}"
# The standard map has 4 boar herds (2 for each 512 by 512 blocks); the large one 11, less any that found no room.
boars="$(field "${UI[2]}" lair_kinds | tr ',' '\n' | sed -n 's/^boar://p')"
[ "${boars:-0}" -gt 6 ] || fail "the large map has only ${boars:-0} boar herds: ${UI[2]}"
echo "UI map size test OK: Large picked, a 1024x128x1024 world with $boars boar herds"
