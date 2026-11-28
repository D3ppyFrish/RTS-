#!/usr/bin/env bash
# Scripted-input screenshot test for Clan Kage's hidden villages. In test mode with fog of war, playing the Settlers
# against Clan Kage: a Kage house, barracks and watchtower stand on open grass south-east of your town hall, and a
# rider of yours stands 7 columns off. The ground they stand on is in its sight, but only the watchtower shows: the
# house and barracks are hidden. A worker put down beside them finds them, and they show.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-hidden-village-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/hidden-village}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI hidden village test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: open grass south-east of the town hall (152..176 x 393..412). Clan Kage's house covers
# 166..169 x 394..397 and its barracks 166..171 x 401..405, its watchtower 158..160 x 393..395; the rider stands at
# 159,400, 7 columns west of the house and barracks.
STEPS="
wait 20
building house 166,394 1
building barracks 166,401 1
building watchtower 158,393 1
spawn rider 159,400
ticks 12
camera 167,400 300 45 22
wait 3
print
shot $OUT/ui-hidden-village-hidden.png
spawn worker 164,399
ticks 12
wait 3
print
shot $OUT/ui-hidden-village-found.png
"
LOG="$OUT/ui-hidden-village.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --fog --factions=settlers,kage --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" hidden_buildings)" -ge 3 ] || fail "the Kage house and barracks (and their town hall) are not hidden: ${UI[0]}"
[ "$(field "${UI[1]}" hidden_buildings)" -eq "$(( $(field "${UI[0]}" hidden_buildings) - 2 ))" ] \
  || fail "the worker did not find the house and barracks: ${UI[1]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI hidden village screenshot test OK: $OUT"
