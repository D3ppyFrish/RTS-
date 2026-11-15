#!/usr/bin/env bash
# Scripted-input screenshot test for the work priorities menu: an idle worker is selected and the menu opened
# with Ctrl+J (it starts on the selected worker); wood is put on its list and moved to the top; then the
# default list is picked and smelting taken off it; Escape closes the menu. Saves PNGs to OUT_DIR and fails
# if the UI state is not as expected.
# Usage: scripts/ui-priorities-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/priorities}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

STEPS="
wait 20
camera 87,261 45 55 22
wait 5
key space
key period
wait 2
key ctrl+j
wait 3
print
shot $OUT/ui-priorities-worker.png
clickui prio_toggle_wood
wait 2
clickui prio_up_wood
wait 2
clickui prio_up_wood
wait 2
clickui prio_up_wood
wait 2
clickui prio_up_wood
wait 2
key space
ticks 2
key space
wait 3
print
shot $OUT/ui-priorities-wood-first.png
clickui prio_target_default
wait 2
clickui prio_toggle_smelt
wait 2
key space
ticks 2
key space
wait 3
print
shot $OUT/ui-priorities-default.png
key escape
wait 2
print
"
LOG="$OUT/ui-priorities.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --input="$STEPS" 2>&1 | grep -E "^(UI|Saved|Screenshot)" | tee "$LOG"

fail() { echo "UI priorities test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" priorities_menu)" -eq 1 ] && [ "$(field "${UI[0]}" priorities)" = default ] \
  || fail "menu not open on a worker following the default: ${UI[0]}"
[ "$(field "${UI[1]}" priorities)" = wood,build,farm,smelt,needed ] || fail "wood not put first on the worker's list: ${UI[1]}"
[ "$(field "${UI[1]}" default_priorities)" = build,farm,smelt,needed ] || fail "default list changed too soon: ${UI[1]}"
[ "$(field "${UI[2]}" default_priorities)" = build,farm,needed ] && [ "$(field "${UI[2]}" priorities)" = wood,build,farm,smelt,needed ] \
  || fail "smelting not taken off the default list (or the worker's own list changed): ${UI[2]}"
[ "$(field "${UI[3]}" priorities_menu)" -eq 0 ] || fail "Escape did not close the menu: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI priorities screenshot test OK: $OUT"
