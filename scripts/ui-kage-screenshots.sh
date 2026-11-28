#!/usr/bin/env bash
# Scripted-input screenshot test for Clan Kage, the ratfolk ninja clan. First the start screen: Clan Kage are picked
# (seven factions to choose from), and the match starts with their own units by your town hall in the west: workers,
# kunoichi, shinobi, shuriken throwers and shadow casters, no spearmen, riders, archers or mages. Then, in test mode as
# Clan Kage: kunoichi and shinobi stand still in the grass and hide, a close-up shows the four ratfolk, and Settler
# spearmen come close and the hidden ones go for them. Saves PNGs to OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-kage-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/kage}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI Clan Kage test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234: the west start (player 0) has its town hall at 128..134 x 381..387.
STEPS="
wait 10
clickui start_kage
wait 3
print
shot $OUT/ui-kage-start-screen.png
clickui start_button
waitmatch
wait 10
camera 131,391 25 40 13
wait 5
print
shot $OUT/ui-kage-start.png
"
LOG="$OUT/ui-kage.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --menu --seed=1234 --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 2 ] || fail "expected 2 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" faction)" = kage ] || fail "Clan Kage were not picked: ${UI[0]}"
[ "$(field "${UI[1]}" screen)" = match ] || fail "the match did not start: ${UI[1]}"
[ "$(field "${UI[1]}" faction)" = kage ] || fail "you are not playing Clan Kage: ${UI[1]}"

# Test mode as Clan Kage, on open grass south-east of the town hall (152..176 x 393..412): one of each of their four
# units in a row for a close look, then kunoichi and shinobi standing still (hiding), and Settler spearmen sent at them.
STEPS="
wait 20
clickui test_tab_world
spawn kunoichi 156,398 1
spawn shinobi 158,398 1
spawn shuriken_thrower 160,398 1
spawn shadow_caster 162,398 1
ticks 20
camera 159,398 20 22 6
wait 5
print
shot $OUT/ui-kage-close.png
spawn kunoichi 158,404 3
spawn shinobi 162,404 2
ticks 40
print
camera 160,402 18 38 14
wait 5
shot $OUT/ui-kage-hiding.png
spawn spearman 160,407 3 1
ticks 15
print
ticks 40
camera 160,404 18 38 14
wait 5
shot $OUT/ui-kage-ambush.png
"
LOG2="$OUT/ui-kage-test.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --factions=kage,settlers --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG2"

! grep -q '^Input script' "$LOG2" || fail "a scripted step failed: $(grep '^Input script' "$LOG2")"
mapfile -t UI < <(grep '^UI ' "$LOG2")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" hiding)" -ge 2 ] || fail "the kunoichi and shinobi standing still do not hide: ${UI[0]}"
[ "$(field "${UI[1]}" hiding)" -ge 7 ] || fail "the kunoichi and shinobi standing still do not hide: ${UI[1]}"
[ "$(field "${UI[2]}" fighting)" -ge 1 ] || fail "nobody went for the spearmen: ${UI[2]}"
[ "$(field "${UI[2]}" hiding)" -lt "$(field "${UI[1]}" hiding)" ] || fail "nobody came out of hiding at the spearmen: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG" "$LOG2" | awk -F: '{s+=$2} END {print s}')" -eq 5 ] || fail "missing screenshots"
echo "UI Clan Kage screenshot test OK: $OUT"
