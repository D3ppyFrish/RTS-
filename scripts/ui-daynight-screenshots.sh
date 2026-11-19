#!/usr/bin/env bash
# Screenshots of the world through a day and its weather (see game/scripts/WorldLighting.cs, DayCycle.cs
# and Ambience.cs): the west start with three houses, six torches and a campfire, at morning (smoke from
# the roofs), noon, dusk and midnight (with --time-of-day), at midnight again after clicking the time of
# day button, which keeps it day, at dusk in a storm (torches out, rain), and at night over the lava and
# the swamp. Saves PNGs to OUT_DIR as morning.png, noon.png, dusk.png, night.png, always-day.png,
# storm.png, lava.png and swamp.png, and fails if a scripted step found nothing to click or a screenshot is
# missing.
# Usage: scripts/ui-daynight-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/daynight}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "Day and night test failed: $1" >&2; exit 1; }

LOG="$OUT/daynight.log"
: > "$LOG"
run() {
  local name=$1 hour=$2 steps=$3; shift 3
  xvfb-run -a -s "-screen 0 1600x900x24" \
    timeout 600 godot --path game --rendering-driver opengl3 -- --seed=1234 --focus=start0 --houses=3 --lights=6 \
      --cam-yaw=45 --cam-pitch=35 --cam-distance=30 --time-of-day="$hour" "$@" --input="$steps" 2>&1 \
    | grep -E "^(Saved|Input script)" | tee -a "$LOG"
}
for pair in morning:8.5 noon:13 dusk:19.3 night:0; do
  run "${pair%%:*}" "${pair#*:}" "
wait 10
shot $OUT/${pair%%:*}.png
"
done
run always-day 0 "
wait 10
clickui day_button
wait 5
shot $OUT/always-day.png
"
run storm 20 "
wait 40
shot $OUT/storm.png
" --weather=storm --cam-distance=22
run lava 23 "
wait 30
shot $OUT/lava.png
" --focus=lava --cam-distance=18 --cam-pitch=40
run swamp 23 "
wait 30
shot $OUT/swamp.png
" --focus=swamp --cam-pitch=30
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 8 ] || fail "missing screenshots"
echo "Day and night screenshots OK: $OUT"
