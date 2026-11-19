#!/usr/bin/env bash
# Scripted-input screenshot test for the match flow (issue #16). First the start screen: the Orcs are
# picked, with the computer playing the Settlers, and the match starts at the Orcs' town hall in the east.
# You surrender: the Defeat panel shows and the match's replay is saved, and the match runner plays the
# replay back to the same end (same winner, same final state hash). Look around and Results hide and show
# the panel again, and New match goes back to the start screen. Then, started from the command line, 40
# units next to the Orcs' town hall bring it down: Victory (no replay, since --units is a test setup).
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-match-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
dotnet build tools/MatchRunner -c Release -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true
REPLAYS="$OUT/replays"
rm -rf "$REPLAYS"

fail() { echo "UI match test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234: the west start (player 0, the Settlers) at 131,384 and the east start (player 1, the Orcs)
# at 655,384, whose town hall covers 652..658 x 381..387.
STEPS="
wait 10
print
shot $OUT/ui-start.png
clickui start_orcs
wait 2
print
clickui start_button
waitmatch
wait 10
print
shot $OUT/ui-match-start.png
clickui surrender_button
wait 3
shot $OUT/ui-surrender.png
clickui surrender_yes
ticks 20
wait 5
print
shot $OUT/ui-defeat.png
clickui end_look
wait 3
print
clickui surrender_button
wait 3
print
clickui end_new
wait 5
print
"
LOG="$OUT/ui-match.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --menu --seed=1234 --replays="$REPLAYS" --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script|Match over|Replay saved|Player)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 7 ] || fail "expected 7 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" screen)" = start ] && [ "$(field "${UI[0]}" player)" -eq 0 ] && [ "$(field "${UI[0]}" seed)" = 1234 ] \
  && [ "$(field "${UI[0]}" computer)" -eq 1 ] || fail "the start screen is not as set up: ${UI[0]}"
[ "$(field "${UI[1]}" player)" -eq 1 ] || fail "picking the Orcs did not pick player 1: ${UI[1]}"
grep -q '^Player 0 (Settlers) is a computer player' "$LOG" || fail "the computer is not playing the Settlers"
[ "$(field "${UI[2]}" screen)" = match ] && [ "$(field "${UI[2]}" player)" -eq 1 ] && [ "$(field "${UI[2]}" result)" = none ] \
  && [ "$(field "${UI[2]}" terrain_tool)" -eq 0 ] && [ "$(field "${UI[2]}" units)" -eq 24 ] \
  || fail "the match did not start as the Orcs (without the terrain tool): ${UI[2]}"
[ "$(field "${UI[3]}" screen)" = end ] && [ "$(field "${UI[3]}" result)" = defeat ] && [ "$(field "${UI[3]}" replay)" = saved ] \
  && [ "$(field "${UI[3]}" paused)" -eq 1 ] || fail "surrendering did not end the match in defeat with a replay: ${UI[3]}"
[ "$(field "${UI[4]}" screen)" = match ] && [ "$(field "${UI[4]}" result)" = defeat ] || fail "Look around did not close the panel: ${UI[4]}"
[ "$(field "${UI[5]}" screen)" = end ] || fail "Results did not open the panel again: ${UI[5]}"
[ "$(field "${UI[6]}" screen)" = start ] && [ "$(field "${UI[6]}" player)" -eq 1 ] && [ "$(field "${UI[6]}" computer)" -eq 1 ] \
  || fail "New match did not go back to the start screen as it was: ${UI[6]}"

# The replay plays back to the same end: the same winner and the same state on the last tick.
REPLAY="$(sed -n 's/^Replay saved to //p' "$LOG")"
[ -f "$REPLAY" ] || fail "no replay file: '$REPLAY'"
END_LINE="$(grep '^Match over' "$LOG")"
[[ "$END_LINE" =~ player\ 0\ won\;\ tick\ ([0-9]+),\ state\ hash\ ([0-9A-F]+) ]] || fail "unexpected end: $END_LINE"
TICK="${BASH_REMATCH[1]}" HASH="${BASH_REMATCH[2]}"
RUN_LOG="$OUT/ui-match-replay.log"
dotnet run --no-build -c Release --project tools/MatchRunner -- --log="$REPLAY" --runs=2 --hash-every=20 | tee "$RUN_LOG" \
  || fail "the replay did not play back the same twice"
grep -q "^Run 1: $TICK ticks .*final hash $HASH\$" "$RUN_LOG" || fail "the replay does not end as the match did (tick $TICK, hash $HASH)"
grep -q '^Result: player 0 won' "$RUN_LOG" || fail "the replay does not end with the Settlers winning"

# Victory, from the command line: 40 of your units just west of the Orcs' town hall, selected with a box
# and sent at it with a right click, at 4x speed.
STEPS="
wait 10
camera 649,384 0 60 36
wait 5
print
dragworld 638,g,379 648,g,389
wait 2
print
clickworld 655,g,384 right
key plus*2
ticks 1200
wait 5
print
shot $OUT/ui-victory.png
"
LOG="$OUT/ui-match-victory.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --seed=1234 --units=40 --focus=643,384 --replays="$REPLAYS" --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script|Match over|Replay saved)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" screen)" = match ] && [ "$(field "${UI[0]}" player)" -eq 0 ] && [ "$(field "${UI[0]}" terrain_tool)" -eq 1 ] \
  || fail "the command-line match did not start (with the terrain tool): ${UI[0]}"
[ "$(field "${UI[1]}" own)" -eq 40 ] || fail "the 40 units are not selected: ${UI[1]}"
[ "$(field "${UI[2]}" screen)" = end ] && [ "$(field "${UI[2]}" result)" = victory ] && [ "$(field "${UI[2]}" replay)" = none ] \
  || fail "bringing down the Orcs' town hall did not end the match in victory: ${UI[2]}"
grep -q '^Match over at .*Victory, player 0 won' "$LOG" || fail "no victory line"
[ "$(ls "$REPLAYS" | wc -l)" -eq 1 ] || fail "a test setup's match was saved as a replay"
[ "$(cat "$OUT"/ui-match.log "$LOG" | grep -c '^Saved screenshot')" -eq 5 ] || fail "missing screenshots"
echo "UI match screenshot test OK: $OUT"
