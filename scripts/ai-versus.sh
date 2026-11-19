#!/usr/bin/env bash
# Head to head between two computer player levels (issue #47): one match per seed on the default 768x64x768 map from
# the usual start, with fog of war, at most 40 game minutes each. Level A plays player 0 on odd seeds and
# player 1 on even seeds, so both sides of each map get played. Prints each match and the tally.
# Usage: scripts/ai-versus.sh [A] [B] [SEEDS] [OUT_DIR]   (default: hard classic 20 build/ai-versus)
set -euo pipefail
A="${1:-hard}"; B="${2:-classic}"; N="${3:-20}"; OUT="${4:-build/ai-versus}"
cd "$(dirname "$0")/.."
mkdir -p "$OUT"
dotnet build tools/MatchRunner -c Release -v q -nologo >/dev/null
JOBS="${JOBS:-$(nproc)}"
run() {
  local seed="$1" levels
  if (( seed % 2 )); then levels="$A,$B"; else levels="$B,$A"; fi
  dotnet run --no-build -c Release --project tools/MatchRunner -- --ai --fog --levels="$levels" --seed="$seed" \
    --minutes=40 --runs=1 > "$OUT/versus-$seed.txt" 2>&1 || true
}
export -f run; export A B OUT
seq 1 "$N" | xargs -P "$JOBS" -I{} bash -c 'run {}'
wins=0; losses=0; other=0
for seed in $(seq 1 "$N"); do
  line="$(grep '^AI match' "$OUT/versus-$seed.txt" || echo "no result")"
  if (( seed % 2 )); then mine=0; else mine=1; fi
  if [[ "$line" =~ player\ ([0-9]+)\ won ]]; then
    if [ "${BASH_REMATCH[1]}" = "$mine" ]; then wins=$((wins + 1)); r="$A won"; else losses=$((losses + 1)); r="$B won"; fi
  else other=$((other + 1)); r="no winner"; fi
  echo "seed $seed ($A is player $mine): $r. $line"
done
echo "$A against $B over $N seeds: $wins wins, $losses losses, $other without a winner"
