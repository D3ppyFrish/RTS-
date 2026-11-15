#!/usr/bin/env bash
# Renders the game on a virtual screen and saves a PNG. Used for automated visual checks.
# Usage: scripts/screenshot.sh OUTPUT.png [extra game args, e.g. --seed=7]
set -euo pipefail
OUT="$(realpath -m "${1:-screenshots/latest.png}")"; shift || true
mkdir -p "$(dirname "$OUT")"
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --screenshot="$OUT" "$@" 2>&1 | grep -i screenshot
