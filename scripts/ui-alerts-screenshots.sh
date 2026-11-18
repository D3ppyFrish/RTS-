#!/usr/bin/env bash
# Scripted-input screenshot test for alerts, the idle workers button (issue #42) and the Army button (#41). Seed 1234, west start:
# the Idle workers button counts the starting workers, and two clicks select one idle worker after another
# with the camera on each; the workers build a house, a "House finished" alert shows, and clicking it with
# the camera far away brings the camera back to the house. Then a battle (--battle): F2 selects your whole
# army and none of your workers, then your army attack-moves into the enemy's: an under attack alert shows, and clicking it moves the camera to the
# fight. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-alerts-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI alerts test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: town hall at 84..90 x 253..259, starting units in rows just south of it and open
# ground for a house south-east of it (as in ui-economy-screenshots.sh).
STEPS="
wait 20
camera 87,261 45 55 22
wait 5
print
clickui idle_button
wait 3
print
clickui idle_button
wait 3
print
shot $OUT/ui-idle-worker.png
camera 87,261 45 55 22
wait 3
down 520,260
mouse 800,450
mouse 1080,640
up 1080,640
wait 2
key h
clickworld 96,g,266
key plus*2
ticks 500
print
shot $OUT/ui-alert-house.png
camera 200,150
wait 3
clickui alert_0
wait 3
print
shot $OUT/ui-alert-clicked.png
"
LOG="$OUT/ui-alerts.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 5 ] || fail "expected 5 UI lines, got ${#UI[@]}"
idle="$(field "${UI[0]}" idle)"
[ "$idle" -ge 2 ] && [ "$(field "${UI[0]}" alerts)" -eq 0 ] || fail "the starting workers are not counted idle, or alerts show at the start: ${UI[0]}"
[ "$(field "${UI[1]}" selected)" -eq 1 ] && [ "$(field "${UI[1]}" unit)" = worker ] || fail "the idle workers button did not select a worker: ${UI[1]}"
cam="$(field "${UI[1]}" camera)"
[ "${cam%,*}" -ge 75 ] && [ "${cam%,*}" -le 100 ] && [ "${cam#*,}" -ge 255 ] && [ "${cam#*,}" -le 275 ] \
  || fail "the camera is not on the idle worker: ${UI[1]}"
[ "$(field "${UI[2]}" selected)" -eq 1 ] && [ "$(field "${UI[2]}" unit)" = worker ] \
  && [ "$(field "${UI[2]}" first_id)" -ne "$(field "${UI[1]}" first_id)" ] || fail "a second click did not select the next idle worker: ${UI[2]}"
[ "$(field "${UI[3]}" sites)" -eq 0 ] && [ "$(field "${UI[3]}" buildings)" -eq 2 ] || fail "the house was not built: ${UI[3]}"
[[ ",$(field "${UI[3]}" alert_kinds)," == *,BuildingFinished,* ]] || fail "no alert for the finished house: ${UI[3]}"
[ "$(field "${UI[3]}" alert0)" = BuildingFinished ] || fail "the newest alert is not the finished house: ${UI[3]}"
cam="$(field "${UI[4]}" camera)"
# The camera was far off (200,150); the house is a few columns from the town hall (84..90 x 253..259).
[ "${cam%,*}" -ge 75 ] && [ "${cam%,*}" -le 105 ] && [ "${cam#*,}" -ge 250 ] && [ "${cam#*,}" -le 285 ] \
  || fail "clicking the alert did not move the camera to the house: ${UI[4]}"

# A battle at the map centre: your army attack-moves east into the enemy's.
STEPS="
wait 20
camera 248,218 0 55 22
wait 5
print
key f2
wait 2
print
shot $OUT/ui-army-selected.png
dragworld 236,g,208 245,g,229
wait 2
clickworld 262,g,222 right ctrl
ticks 120
print
shot $OUT/ui-alert-attack.png
camera 100,100
wait 3
clickui alert_0
wait 3
print
"
LOG="$OUT/ui-alerts-battle.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --world=512x64x512 --seed=1234 --focus=248,218 --battle=16 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"
! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click in the battle"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines in the battle, got ${#UI[@]}"
[ "$(field "${UI[0]}" alerts)" -eq 0 ] || fail "alerts before the battle: ${UI[0]}"
army="$(field "${UI[1]}" army)"
[ "$army" -gt 0 ] && [ "$(field "${UI[1]}" selected)" -eq "$army" ] && [ "$(field "${UI[1]}" selected_workers)" -eq 0 ] \
  || fail "F2 did not select the whole army and only the army: ${UI[1]}"
UI=("${UI[0]}" "${UI[@]:2}")
[[ ",$(field "${UI[1]}" alert_kinds)," == *,UnitsAttacked,* ]] || fail "no under attack alert in the battle: ${UI[1]}"
# Under attack alerts for the one fight do not pile up.
[ "$(field "${UI[1]}" alerts)" -le 2 ] || fail "under attack alerts repeat for the same fight: ${UI[1]}"
cam="$(field "${UI[2]}" camera)"
[ "${cam%,*}" -ge 225 ] && [ "${cam%,*}" -le 270 ] && [ "${cam#*,}" -ge 200 ] && [ "${cam#*,}" -le 240 ] \
  || fail "clicking the alert did not move the camera to the fight: ${UI[2]}"
[ "$(cat "$OUT"/ui-alerts.log "$OUT"/ui-alerts-battle.log | grep -c '^Saved screenshot')" -eq 5 ] || fail "missing screenshots"
echo "UI alerts screenshot test OK: $OUT"
