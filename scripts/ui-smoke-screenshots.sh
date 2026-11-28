#!/usr/bin/env bash
# Scripted-input screenshot test for Clan Kage's smoke bombs. In test mode as Clan Kage with every tech: kunoichi and
# Settler spearmen fight on open grass, the kunoichi are selected, the Smoke bomb button throws a bomb at their feet and
# a cloud of grey smoke hangs over them (the fight stops in it), then they walk out of it, away from the spearmen.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-smoke-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/smoke}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI smoke bomb test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Open grass south-east of the west town hall (152..176 x 393..412).
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
spawn kunoichi 160,400 4
spawn spearman 160,403 4 1
ticks 40
camera 160,401 330 40 14
wait 3
dragworld 157,g,397 163,g,401
wait 2
print
clickui smoke_bomb
ticks 3
print
ticks 20
camera 160,401 330 38 13
wait 3
shot $OUT/ui-smoke-cloud.png
clickworld 160,g,390 right
ticks 60
print
camera 160,396 330 40 16
wait 3
shot $OUT/ui-smoke-slipped.png
"
LOG="$OUT/ui-smoke.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=kage,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" selected)" -ge 1 ] || fail "no kunoichi selected: ${UI[0]}"
[ "$(field "${UI[0]}" smoke_bombs)" -eq 0 ] || fail "smoke thrown before the button: ${UI[0]}"
[ "$(field "${UI[1]}" smoke_bombs)" -ge 1 ] || fail "the Smoke bomb button threw nothing: ${UI[1]}"
[ "$(field "${UI[1]}" smoke_clouds)" -ge 1 ] || fail "no cloud of smoke: ${UI[1]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI smoke bomb screenshot test OK: $OUT"
