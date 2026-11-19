#!/usr/bin/env bash
# Screenshot test for the dusk tree. First the tree at night, bare, in its purple mist, with its warden.
# Then as the Settlers (--dusk-party): a party east of the tree attack-moves on the Dusk Warden and kills
# it; right-clicking the tree sends the battle priests to heal it, the tree grows its canopy and one priest
# gets the Dusk Staff, whose Dusk Bloom button heals the party. Then as the Orcs: their party beats the
# warden, which is recruited from the Beaten button, and hacks the tree down. Saves PNGs to OUT_DIR and
# fails if the UI state is not as expected.
# Usage: scripts/ui-dusktree-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/dusk-tree}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

# Seed 1234: the dusk tree's trunk is at 413,590 (lowest trunk block at level 26) and its warden stands
# by it at 415,588. The ground east of the tree is level with it, so --dusk-party puts the party in ranks
# from 428 (15 columns east of the trunk) eastward, 583..597 north to south.
NIGHT_STEPS="
wait 20
camera 413,590 30 25 16
wait 10
shot $OUT/dusk-tree-night.png
camera 413,590 210 20 16
wait 10
shot $OUT/dusk-tree-night-2.png
"
SETTLER_STEPS="
wait 20
camera 413,590 30 30 24
wait 10
print
shot $OUT/dusk-tree-guarded.png
camera 428,590 0 75 40
wait 5
dragworld 425,g,580 434,g,600
wait 2
print
clickworld 415,g,588 right ctrl
ticks 700
print
camera 413,590 30 35 26
wait 5
clickworld 413,31,590 right
ticks 2
print
ticks 60
shot $OUT/dusk-tree-healing.png
ticks 260
print
camera 413,590 30 30 26
wait 10
shot $OUT/dusk-tree-healed.png
clickui dusk_bloom
ticks 2
print
wait 3
shot $OUT/dusk-tree-staff.png
"
ORC_STEPS="
wait 20
camera 428,590 0 75 40
wait 5
dragworld 425,g,580 434,g,600
wait 2
print
clickworld 415,g,588 right ctrl
ticks 700
print
clickworld 413,31,590 right
ticks 2
print
ticks 300
print
camera 413,590 30 35 26
wait 10
shot $OUT/dusk-tree-razed.png
clickui beaten_button
wait 3
clickui tame_recruit
ticks 2
print
"
LOG="$OUT/ui-dusktree.log"
run() {
  xvfb-run -a -s "-screen 0 1600x900x24" \
    godot --path game --rendering-driver opengl3 -- "$@" 2>&1 | grep -E "^(UI|Saved|Screenshot|Input script)"
}
{
  run --focus=dusk --time-of-day=0 --input="$NIGHT_STEPS"
  run --player=0 --focus=dusk --dusk-party=2 --input="$SETTLER_STEPS"
  run --player=1 --focus=dusk --dusk-party=2 --input="$ORC_STEPS"
} | tee "$LOG"

fail() { echo "UI dusk tree test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }
! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 11 ] || fail "expected 11 UI lines, got ${#UI[@]}"
# The Settlers: the tree stands guarded; the party is selected, kills the warden and its priests heal the tree.
[ "$(field "${UI[0]}" dusk_tree)" = guarded ] && [ "$(field "${UI[0]}" warden)" = wild ] || fail "the tree is not guarded: ${UI[0]}"
[ "$(field "${UI[1]}" own)" -eq 12 ] || fail "the party is not selected: ${UI[1]}"
[ "$(field "${UI[2]}" dusk_tree)" = unguarded ] && [ "$(field "${UI[2]}" warden)" = dead ] || fail "the party did not kill the warden: ${UI[2]}"
[ "$(field "${UI[3]}" tending)" -ge 1 ] || fail "no priest went to heal the tree: ${UI[3]}"
[ "$(field "${UI[4]}" dusk_tree)" = healed ] && [ "$(field "${UI[4]}" staff)" = own ] || fail "the tree was not healed, or nobody got the staff: ${UI[4]}"
[ "$(field "${UI[5]}" bloom_ready)" -eq 0 ] || fail "Dusk Bloom did not go off: ${UI[5]}"
# The Orcs: they beat the warden, hack the tree down and recruit the warden.
[ "$(field "${UI[6]}" own)" -eq 10 ] || fail "the Orcs' party is not selected: ${UI[6]}"
[ "$(field "${UI[7]}" warden)" = beaten ] && [ "$(field "${UI[7]}" dusk_tree)" = unguarded ] || fail "the Orcs did not beat the warden: ${UI[7]}"
[ "$(field "${UI[8]}" tending)" -ge 1 ] || fail "nobody went to hack at the tree: ${UI[8]}"
[ "$(field "${UI[9]}" dusk_tree)" = razed ] || fail "the tree was not hacked down: ${UI[9]}"
[ "$(field "${UI[10]}" warden)" = own ] && [ "$(field "${UI[10]}" unit)" = dusk_warden ] || fail "the warden was not recruited: ${UI[10]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 7 ] || fail "missing screenshots"
echo "UI dusk tree screenshot test OK: $OUT"
