#!/usr/bin/env bash
# Screenshots of the UI in each of its looks (issue #46; see game/scripts/UiStyle.cs): the start screen, the
# HUD with the town hall selected, the tech tree and the end of match panel, for timber, stone and crystal
# (each forced with --ui-look; in play each faction has its own, from uiLook in data/factions.json).
# Saves PNGs to OUT_DIR as LOOK-start.png, LOOK-hud.png, LOOK-techs.png and LOOK-end.png, and fails if a
# scripted step found nothing to click or a screenshot is missing.
# Usage: scripts/ui-style-screenshots.sh [OUT_DIR] [LOOK...]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/style}")"
shift || true
LOOKS=("${@:-timber stone crystal}")
read -r -a LOOKS <<< "${LOOKS[*]}"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI style test failed: $1" >&2; exit 1; }

# Seed 1234: the west start (player 0, the Settlers) with its town hall at 84..90 x 253..259.
for LOOK in "${LOOKS[@]}"; do
  STEPS="
wait 10
shot $OUT/$LOOK-start.png
clickui start_fog
clickui start_button
waitmatch
wait 10
camera 92,262 30 52 30
wait 5
clickworld 87,g,256
ticks 30
mouse 30,330
wait 30
shot $OUT/$LOOK-hud.png
key k
wait 3
mouse 1500,860
wait 3
shot $OUT/$LOOK-techs.png
key k
clickui surrender_button
wait 2
clickui surrender_yes
ticks 20
wait 5
shot $OUT/$LOOK-end.png
"
  LOG="$OUT/$LOOK.log"
  xvfb-run -a -s "-screen 0 1600x900x24" \
    timeout 600 godot --path game --rendering-driver opengl3 -- --world=512x64x512 --menu --seed=1234 --ui-look="$LOOK" --replays="$OUT/replays" --input="$STEPS" 2>&1 \
    | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"
  ! grep -q '^Input script' "$LOG" || fail "$LOOK: a scripted step found nothing to click"
  [ "$(grep -c '^Saved screenshot' "$LOG")" -eq 4 ] || fail "$LOOK: missing screenshots"
done
rm -rf "$OUT/replays"
echo "UI style screenshots OK: $OUT"
