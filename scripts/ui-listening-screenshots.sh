#!/usr/bin/env bash
# Scripted-input screenshot test for units underground and listening posts, in test mode with fog of war: a
# trench is dug on open grass south-east of your town hall, four Orc sappers are put down in it and it is roofed
# over with stone, so they are underground. Your watchtower sees the ground over them but they are not drawn,
# even with the height slice cut down into the tunnel; once a listening post of yours stands nearby, they show. Saves PNGs to OUT_DIR and fails if the UI state is
# not as expected.
# Usage: scripts/ui-listening-screenshots.sh [OUT_DIR]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/listening}")"
mkdir -p "$OUT"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true

fail() { echo "UI listening post test failed: $1" >&2; exit 1; }
field() { [[ "$1" =~ (^| )$2=([^ ]+) ]] && echo "${BASH_REMATCH[2]}"; }

# Seed 1234, west start: town hall at 128..134 x 381..387 and open grass south-east of it (152..176 x 393..412).
STEPS="
wait 20
clickui test_tab_place
clickui test_cat_building
select test_item watchtower
clickui test_place
clickworld 166,g,413
key escape
ticks 10
blocks 160,g-2,400 172,g,401 Air
ticks 4
spawn sapper 162,400 1 1
spawn sapper 165,401 1 1
spawn sapper 168,400 1 1
spawn sapper 171,401 1 1
ticks 8
print
blocks 159,g,399 173,g,402 Stone
ticks 12
camera 166,394 20 45 24
key pagedown*5
wait 5
print
shot $OUT/ui-listening-hidden.png
clickui test_tab_place
clickui test_cat_building
select test_item listening post
clickui test_place
clickworld 178,g,404
key escape
ticks 12
print
wait 3
shot $OUT/ui-listening-heard.png
"
LOG="$OUT/ui-listening.log"
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --test-mode --fog --input="$STEPS" 2>&1 \
  | grep -E "^(UI|Saved|Screenshot|Input script)" | tee "$LOG"

! grep -q '^Input script' "$LOG" || fail "a scripted step failed: $(grep '^Input script' "$LOG")"
mapfile -t UI < <(grep '^UI ' "$LOG")
[ "${#UI[@]}" -eq 3 ] || fail "expected 3 UI lines, got ${#UI[@]}"
[ "$(field "${UI[0]}" fog)" = on ] || fail "no fog of war: ${UI[0]}"
[ "$(field "${UI[0]}" enemies_shown)" -ge 4 ] || fail "the sappers in the open trench are not shown: ${UI[0]}"
[ "$(field "${UI[1]}" enemies_shown)" -eq 0 ] || fail "sappers under the roof are shown without a listening post: ${UI[1]}"
[ "$(field "${UI[2]}" enemies_shown)" -ge 4 ] || fail "the listening post did not show the sappers underground: ${UI[2]}"
[ "$(grep -c '^Saved screenshot' "$LOG")" -eq 2 ] || fail "missing screenshots"
echo "UI listening post screenshot test OK: $OUT"
