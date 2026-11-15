#!/usr/bin/env bash
# AI check used by CI (issue #15): computer players play each other on 512x64x512 maps from the usual
# start, one match per seed (seeds 1 to 5 unless given), at most 40 game minutes each. Every match must
# end with a winner, and replaying its command log (twice) must give the same hashes and the same winner.
# The players are the Hard computer player (player 0) against the Classic one, with fog of war (issue
# #47); LEVELS (say LEVELS=classic) and FOG=0 change that. The matches run side by side; each one's
# command log and output go to OUT_DIR.
# Usage: [LEVELS=a,b] [FOG=0] scripts/ai-matches.sh [OUT_DIR] [SEED...]
set -euo pipefail
OUT="${1:-build/ai-matches}"; shift || true
SEEDS=("$@")
[ ${#SEEDS[@]} -gt 0 ] || SEEDS=(1 2 3 4 5)
cd "$(dirname "$0")/.."
mkdir -p "$OUT"
dotnet build tools/MatchRunner -c Release -v q -nologo >/dev/null
RUN=(dotnet run --no-build -c Release --project tools/MatchRunner --)
EXTRA=(--levels="${LEVELS:-hard,classic}")
[ "${FOG:-1}" = 0 ] || EXTRA+=(--fog)
pids=()
for seed in "${SEEDS[@]}"; do
  "${RUN[@]}" --ai "${EXTRA[@]}" --seed="$seed" --minutes=40 --runs=2 --hash-every=1200 --record="$OUT/match-$seed.log" \
    > "$OUT/match-$seed.txt" 2>&1 &
  pids+=($!)
done
failed=0
for k in "${!SEEDS[@]}"; do
  seed="${SEEDS[$k]}"
  if wait "${pids[$k]}"; then
    echo "seed $seed: $(grep '^AI match' "$OUT/match-$seed.txt")"
  else
    echo "seed $seed FAILED:" >&2
    cat "$OUT/match-$seed.txt" >&2
    failed=1
  fi
done
[ "$failed" = 0 ] || exit 1
echo "AI matches OK: ${#SEEDS[@]} seeds, each won within 40 minutes and replayed the same"
