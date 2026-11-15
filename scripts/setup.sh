#!/usr/bin/env bash
# Installs the toolchain: .NET 8 SDK and Godot (C#/mono build).
# Usage: scripts/setup.sh [--with-export-templates]
# Safe to run more than once. Needs root (apt) on Linux.
set -euo pipefail

GODOT_VERSION="${GODOT_VERSION:-4.7.2-stable}"
GODOT_DIR="${GODOT_DIR:-/opt/godot}"
BIN_DIR="${BIN_DIR:-/usr/local/bin}"
BASE_URL="https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}"

if ! command -v dotnet >/dev/null 2>&1; then
  apt-get update -qq
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-8.0 unzip
fi

BIN="${GODOT_DIR}/Godot_v${GODOT_VERSION}_mono_linux_x86_64/Godot_v${GODOT_VERSION}_mono_linux.x86_64"
if [ ! -x "$BIN" ]; then
  mkdir -p "$GODOT_DIR"
  curl -sSL -o /tmp/godot.zip "${BASE_URL}/Godot_v${GODOT_VERSION}_mono_linux_x86_64.zip"
  unzip -q -o /tmp/godot.zip -d "$GODOT_DIR"
  rm /tmp/godot.zip
fi
mkdir -p "$BIN_DIR" && ln -sf "$BIN" "$BIN_DIR/godot"

if [ "${1:-}" = "--with-export-templates" ]; then
  TDIR="${HOME}/.local/share/godot/export_templates/${GODOT_VERSION/-/.}.mono"
  if [ ! -f "${TDIR}/windows_release_x86_64.exe" ]; then
    mkdir -p "$TDIR"
    curl -sSL -o /tmp/templates.tpz "${BASE_URL}/Godot_v${GODOT_VERSION}_mono_export_templates.tpz"
    unzip -q -j -o /tmp/templates.tpz 'templates/windows_*x86_64*' 'templates/version.txt' -d "$TDIR"
    rm /tmp/templates.tpz
  fi
fi

"$BIN_DIR/godot" --headless --version
dotnet --version
