#!/usr/bin/env bash
# Scripted-input screenshot test for Clan Kage's clan honour. In test mode as Clan Kage: +1000 goods gives the clan 100
# honour too (shown in the top bar). Two kunoichi beside three of the enemy's workers on open grass cut them down, each
# an assassination worth honour. A barracks of yours is selected: its Train master ninja button costs 40 honour; pressed,
# the master ninja is trained (honour paid when queued) and steps out, and the button says you have one.
# Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-honour-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/honour}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI honour test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Open grass south-east of the west town hall (152..176 x 393..412): your barracks covers 166..171 x 394..398, two
# houses (room for the master ninja) stand at its west edge, and the
# kunoichi and the enemy's workers stand south-west of it.
STEPS="
wait 20
clickui test_tab_world
clickui test_goods_0
building barracks 166,394
building house 152,394
building house 152,399
spawn kunoichi 156,406 2
spawn worker 159,406 3 1
ticks 10
print
ticks 300
print
camera 165,400 330 40 18
wait 3
clickworld 168,g,396
wait 2
print
shot $OUT/ui-honour-barracks.png
clickui train_master_ninja
ticks 640
wait 2
print
camera 166,400 330 36 12
wait 3
shot $OUT/ui-honour-master-ninja.png
"
LOG="$OUT/ui-honour.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=kage,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
start=$(field "${UI[0]}" honour)
[ "$start" -ge 100 ] || fail "test mode's goods gave no honour: ${UI[0]}"
after=$(field "${UI[1]}" honour)
[ "$after" -ge $(( start + 2 )) ] || fail "the kunoichi earned no honour for the workers: ${UI[0]} / ${UI[1]}"
[ "$(field "${UI[2]}" building)" = "barracks" ] || fail "the barracks is not selected: ${UI[2]}"
[ "$(field "${UI[3]}" heroes)" -eq 1 ] || fail "no master ninja trained: ${UI[3]}"
[ "$(field "${UI[3]}" honour)" -eq $(( $(field "${UI[2]}" honour) - 40 )) ] || fail "the master ninja did not cost 40 honour: ${UI[2]} / ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI honour screenshot test OK: $OUT"
