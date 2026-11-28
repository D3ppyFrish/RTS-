#!/usr/bin/env bash
# Scripted-input screenshot test for Clan Kage's shadow step. In test mode as Clan Kage with every tech, at dusk: a shadow
# caster and three kunoichi stand on open grass and are selected, the Shadow step button is pressed and a spot a dozen
# columns off is clicked; they go into the shadows and step out there at once, in a swirl of dark violet smoke.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-shadowstep-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/shadowstep}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI shadow step test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Open grass south-east of the west town hall (152..176 x 393..412).
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
spawn shadow_caster 162,400 1
spawn kunoichi 161,402 3
ticks 40
camera 160,401 330 40 18
wait 3
dragworld 156,g,395 167,g,407
wait 2
print
shot $OUT/ui-shadowstep-before.png
camera 167,402 330 40 18
wait 3
clickui shadow_step
wait 1
clickworld 172,g,404
ticks 8
print
shot $OUT/ui-shadowstep-out.png
"
LOG="$OUT/ui-shadowstep.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=kage,settlers --time-of-day=19.5 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" step_casters)" -ge 1 ] || fail "no shadow caster selected: ${UI[0]}"
[ "$(field "${UI[0]}" shadow_steps)" -eq 0 ] || fail "a step before the button: ${UI[0]}"
[ "$(field "${UI[1]}" shadow_steps)" -ge 1 ] || fail "the Shadow step button stepped nobody: ${UI[1]}"
[ "$(field "${UI[1]}" units_stepped)" -ge 4 ] || fail "not all four went through the shadows: ${UI[1]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI shadow step screenshot test OK: $OUT"
