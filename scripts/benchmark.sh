#!/usr/bin/env bash
# Runs the --benchmark mode on a virtual screen and prints the result line.
# Usage: scripts/benchmark.sh [SECONDS] [extra game args]
set -euo pipefail
SECONDS_ARG="${1:-10}"; shift || true
cd "$(dirname "$0")/.."
dotnet build game/Game.csproj -v q -nologo >/dev/null
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true
xvfb-run -a -s "-screen 0 1600x900x24" \
  godot --path game --rendering-driver opengl3 -- --benchmark="$SECONDS_ARG" "$@" 2>&1 | grep -E "^(Loaded|BENCHMARK|Benchmark)"
