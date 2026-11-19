#!/usr/bin/env bash
# Scripted-input screenshot test for test mode (issue #89). Seed 1234 in test mode, with day and night: the
# test panel places trees of several kinds, digs a pit and fills it with water, lays lava, puts down wild
# wolves, the other side's spearmen and barracks, a wolf lair and blood; turns on invulnerability; sets dusk
# and rain; saves the scenario, then loads it back (played back to where it was saved, paused). Saves PNGs to
# OUT_DIR and fails if the UI state is not as expected.
# Usage: scripts/ui-test-mode-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/ui}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI test mode test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

SCEN="$OUT/scenarios"
rm -rf "$SCEN"
# Seed 1234, west start: town hall at 128..134 x 381..387 and open grass south-east of it (152..176 x 393..412).
STEPS="
wait 20
camera 158,402 30 55 34
wait 5
print
clickui test_cat_tree
select test_item pine
clickui test_place
clickworld 152,g,394
select test_item willow
clickworld 156,g,393
select test_item mushroom
clickworld 160,g,394
select test_item birch
clickworld 164,g,393
clickui test_cat_block
select test_item air
spin test_size 5
spin test_height 2
clickworld 153,g,404
ticks 3
select test_item water
clickworld 153,g,404
select test_item lava
spin test_size 2
clickworld 160,g,406
clickui test_cat_unit
select test_item wolf
select test_owner wild
spin test_size 3
clickworld 166,g,402
select test_item spearman
select test_owner orcs
spin test_size 4
clickworld 168,g,408
clickui test_cat_building
select test_item barracks
clickworld 174,g,398
clickui test_cat_lair
clickworld 176,g,410
clickui test_cat_blood
spin test_size 2
clickworld 158,g,412
key escape
ticks 10
print
shot $OUT/ui-test-place.png
clickui test_tab_cheats
clickui test_cheat_invulnerable_0
clickui test_tab_world
clickui test_time_20
clickui test_weather_rain
ticks 10
print
shot $OUT/ui-test-dusk-rain.png
clickui test_tab_save
key space
wait 3
print
clickui test_save
wait 3
print
clickui test_load
wait 20
print
shot $OUT/ui-test-loaded.png
"
LOG="$OUT/ui-test-mode.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --daynight --scenarios="$SCEN" --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script|Scenario)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 6 ] || fail "expected 6 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" test)" = 1 ] && [ "$(field "${UI[0]}" test_panel)" = 1 ] || fail "no test panel: ${UI[0]}"
units0="$(field "${UI[0]}" sim_units)"; buildings0="$(field "${UI[0]}" sim_buildings)"; lairs0="$(field "${UI[0]}" lairs_all)"
[ "$(field "${UI[1]}" test_placed)" -eq 12 ] || fail "not every click placed something: ${UI[1]}"
lairs="$(field "${UI[1]}" lairs_all)"
[ "$lairs" -eq $((lairs0 + 1)) ] || fail "no new lair: ${UI[1]}"
units="$(field "${UI[1]}" sim_units)"
# 3 wolves, 4 spearmen and the new lair's pack.
[ "$units" -ge $((units0 + 8)) ] || fail "the units were not placed: $units0 -> $units"
[ "$(field "${UI[1]}" sim_buildings)" -eq $((buildings0 + 1)) ] || fail "the barracks was not placed: ${UI[1]}"
[ "$(field "${UI[1]}" blood)" -gt 0 ] || fail "no blood: ${UI[1]}"
[ "$(field "${UI[2]}" cheats)" = "Invulnerable:0" ] || fail "invulnerability is not on: ${UI[2]}"
[ "$(field "${UI[2]}" weather)" = Rain ] || fail "the weather is not rain: ${UI[2]}"
minute="$(field "${UI[2]}" minute)"
[ "$minute" -ge 1200 ] && [ "$minute" -le 1210 ] || fail "it is not dusk: ${UI[2]}"
[ "$(field "${UI[3]}" paused)" = 1 ] || fail "not paused before saving: ${UI[3]}"
grep -q '^Scenario saved to' "$LOG" || fail "the scenario was not saved"
ls "$SCEN"/scenario-*.log >/dev/null 2>&1 || fail "no scenario file in $SCEN"
grep -q '^test-mode 1' "$SCEN"/scenario-*.log || fail "the scenario is not marked test mode"
grep -q '^Scenario played back' "$LOG" || fail "the scenario was not loaded"
[ "$(field "${UI[5]}" tick)" = "$(field "${UI[4]}" tick)" ] || fail "the loaded scenario is at another tick: ${UI[4]} / ${UI[5]}"
for f in sim_units sim_buildings lairs_all blood cheats weather minute; do
  [ "$(field "${UI[5]}" $f)" = "$(field "${UI[4]}" $f)" ] || fail "the loaded scenario differs in $f: ${UI[4]} / ${UI[5]}"
done
[ "$(field "${UI[5]}" paused)" = 1 ] || fail "the loaded scenario is not paused: ${UI[5]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 3 ] || fail "missing screenshots"
echo "UI test mode screenshot test OK: $OUT"
