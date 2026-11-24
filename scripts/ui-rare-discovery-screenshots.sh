#!/usr/bin/env bash
# Scripted-input screenshot test for the rare discovery techs (issue #154), in test mode with fog of war on: a
# finished crystal lens and a delver beside a spearman are put down by the west start's town hall. The delver is
# shown close up; then the lens is selected, Gaze (Ctrl+G) is pressed, the camera goes far east over ground never
# seen, and a click there aims the lens: the circle round that spot comes out of the fog. Saves PNGs to OUT_DIR and
# fails if the UI state is not as expected.
# Usage: scripts/ui-rare-discovery-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/rare-discovery}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI rare discovery test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: town hall at 128..134 x 381..387, open grass south-east of it (152..176 x 393..412). The lens
# (3x3) goes at 152..154 x 396..398, the delver and a spearman a few columns east of it. The far spot, 300,400, is
# well beyond anything the start sees.
STEPS="
wait 20
building crystal_lens 152,396
spawn delver 160,400
spawn spearman 162,400
ticks 5
camera 161,400 330 35 9
wait 5
print
shot $OUT/ui-delver.png
camera 153,397 0 55 30
wait 3
clickworld 153,g,397
wait 2
key ctrl+g
wait 2
print
camera 300,400 0 60 80
wait 5
shot $OUT/ui-lens-before.png
click 800,450
ticks 10
wait 10
print
shot $OUT/ui-lens-gaze.png
"
LOG="$OUT/ui-rare-discovery.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --seed=1234 --test-mode --fog --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" fog)" = on ] || fail "fog of war should be on: ${UI[0]}"
[ "$(field "${UI[1]}" lens_selected)" -eq 1 ] && [ "$(field "${UI[1]}" tool)" = Gaze ] \
  || fail "the lens should be selected and Gaze the tool: ${UI[1]}"
[ "$(field "${UI[2]}" gazes)" -eq 1 ] && [ "$(field "${UI[2]}" tool)" = Select ] || fail "the lens should be gazing: ${UI[2]}"
[ "$(field "${UI[2]}" seen)" -gt $(( $(field "${UI[1]}" seen) + 2000 )) ] || fail "the gaze should show more of the map: ${UI[1]} / ${UI[2]}"
[ "$(field "${UI[2]}" mana)" -lt "$(field "${UI[1]}" mana)" ] || fail "the gaze should cost mana: ${UI[1]} / ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI rare discovery screenshot test OK: $OUT"
