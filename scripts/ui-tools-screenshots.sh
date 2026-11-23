#!/usr/bin/env bash
# Scripted-input screenshot test for Better tools (issue #146), in test mode: you get every tech (Better tools
# among them). A tree is put down on open grass with three Orc workers standing east of it; a militia of yours is
# box-selected and, with the fell tool (Ctrl+X), fells the tree towards them: it comes down on them. Then a seam
# of iron ore and one of gold are laid four blocks under the grass a little way off, a foreman of yours is
# selected and sounds the ground there (Ctrl+Z): the buried ore shows through the ground. Saves PNGs to OUT_DIR
# and fails if the UI state is not as expected.
# Usage: scripts/ui-tools-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/tools}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI tools test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: open grass south-east of the town hall (152..176 x 393..412). The tree stands at
# 158,402; the militia at 150,402, west of it (out from under its crown); the Orc workers at 164..168,402 where it falls. The ore is laid
# under 170..174 x 394..396 and the foreman sounds the ground at 172,399.
STEPS="
wait 20
clickui test_tab_world
clickui test_techs_0
clickui test_tab_place
clickui test_cat_tree
select test_item broad
clickui test_place
clickworld 158,g,402
ticks 3
key escape
spawn militia 150,402
spawn worker 164,402 1 1
spawn worker 166,402 1 1
spawn worker 168,402 1 1
blocks 170,g-4,394 172,g-4,396 IronOre
blocks 173,g-5,394 174,g-5,395 GoldOre
ticks 5
camera 162,404 20 55 30
wait 3
dragworld 148,g,400 152,g,404
wait 2
key ctrl+x
wait 2
dragworld 158,g,402 166,g,402
ticks 3
print
ticks 400
spawn foreman 172,400
camera 162,404 340 45 26
wait 5
print
shot $OUT/ui-fell-on-target.png
dragworld 171,g,399 173,g,401
wait 2
key ctrl+z
wait 2
clickworld 172,g,399
ticks 3
print
ticks 220
camera 172,397 20 50 18
wait 5
print
shot $OUT/ui-prospect-ore.png
"
LOG="$OUT/ui-tools.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step found nothing to click"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 4 ] || fail "expected 4 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" fellers)" -eq 1 ] || fail "the militia cannot fell: ${UI[0]}"
[ "$(field "${UI[0]}" felling)" -eq 1 ] || fail "the militia is not felling the tree: ${UI[0]}"
[ "$(field "${UI[1]}" aimed_felled)" -eq 1 ] || fail "the tree did not come down: ${UI[1]}"
[ "$(field "${UI[1]}" tree_crushed)" -ge 2 ] || fail "the tree did not come down on the Orc workers: ${UI[1]}"
[ "$(field "${UI[2]}" prospectors)" -eq 1 ] || fail "the foreman cannot sound the ground: ${UI[2]}"
[ "$(field "${UI[2]}" prospecting)" -eq 1 ] || fail "the foreman is not sounding the ground: ${UI[2]}"
[ "$(field "${UI[3]}" soundings)" -eq 1 ] || fail "the ground was not sounded: ${UI[3]}"
[ "$(field "${UI[3]}" prospected)" -ge 13 ] || fail "the buried ore does not show: ${UI[3]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI tools screenshot test OK: $OUT"
