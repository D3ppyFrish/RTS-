#!/usr/bin/env bash
# Determinism check used by CI: generates a 10-minute scripted match, replays it twice in one
# process, then replays the saved command log in a second process and compares the hash traces.
# Usage: scripts/determinism.sh [OUT_DIR] [extra MatchRunner args]
set -euo pipefail
OUT="${1:-build/determinism}"; shift || true
cd "$(dirname "$0")/.."
mkdir -p "$OUT"
dotnet build tools/MatchRunner -c Release -v q -nologo >/dev/null
RUN=(dotnet run --no-build -c Release --project tools/MatchRunner --)
"${RUN[@]}" --minutes=10 --hash-every=200 --runs=2 --record="$OUT/match.log" --hashes="$OUT/hashes-1.txt" "$@"
"${RUN[@]}" --log="$OUT/match.log" --hash-every=200 --runs=2 --hashes="$OUT/hashes-2.txt"
if ! diff -q "$OUT/hashes-1.txt" "$OUT/hashes-2.txt" >/dev/null; then
  echo "DESYNC: replaying the saved log in a new process gave different hashes:" >&2
  diff "$OUT/hashes-1.txt" "$OUT/hashes-2.txt" | head -5 >&2
  exit 1
fi
echo "Determinism OK: $(wc -l < "$OUT/hashes-1.txt") hash samples match across processes"
