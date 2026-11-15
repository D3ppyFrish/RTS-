#!/usr/bin/env bash
# Builds a Windows release into build/windows. Needs export templates (scripts/setup.sh --with-export-templates).
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build/windows
[ -d game/.godot ] || godot --headless --path game --import >/dev/null 2>&1 || true
godot --headless --path game --export-release "Windows Desktop" ../build/windows/RTS.exe
ls -la build/windows
