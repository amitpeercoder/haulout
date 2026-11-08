#!/usr/bin/env bash
set -euo pipefail

MODEL="${1:-sonnet}"
# A run normally finishes in under a minute. Kill it after this many seconds.
LIMIT="${PLAYTEST_TIMEOUT:-300}"
case "$MODEL" in
  sonnet|opus|haiku) ;;
  *) echo "usage: $0 [sonnet|opus|haiku]" >&2; exit 2 ;;
esac

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
DLL="$ROOT/src/Haulout.Mcp/bin/Debug/net8.0/Haulout.Mcp.dll"
DOTNET="/opt/homebrew/opt/dotnet@8/bin/dotnet"

TS="$(date +%Y%m%d-%H%M%S)"
RUNS="$HERE/runs"
BASE="$RUNS/$TS-$MODEL"
CFG="$BASE.mcp.json"
TRACE="$BASE.trace.jsonl"
OUT="$BASE.json"
DRY="${PLAYTEST_DRY_RUN:-}"

# Only the Mcp project (pulls in Haulout.Sim); the solution may not compile.
if [ -z "$DRY" ]; then
  dotnet build "$ROOT/src/Haulout.Mcp" -v q --nologo >/dev/null
fi

mkdir -p "$RUNS"
CFG_JSON=$(cat <<EOF
{"mcpServers":{"haulout":{"command":"$DOTNET","args":["$DLL"],"env":{"HAULOUT_TRACE":"$TRACE"}}}}
EOF
)

# --strict-mcp-config: ignore the repo's .mcp.json and the user's other servers.
# --disallowedTools: no built-ins, so the agent cannot read the game's source.
CMD=(claude -p "$(cat "$HERE/prompt.md")"
  --mcp-config "$CFG" --strict-mcp-config
  --allowedTools "mcp__haulout__*"
  --disallowedTools "Bash,Read,Edit,Write,Glob,Grep,WebFetch,WebSearch,Task,Agent,NotebookEdit"
  --model "$MODEL" --output-format json --max-turns 60)

if [ -n "$DRY" ]; then
  echo "# config -> $CFG"; echo "$CFG_JSON"
  echo "# cwd: $ROOT"; echo "# command (stdout -> $OUT):"
  printf '%q ' "${CMD[@]}"; echo
  exit 0
fi

echo "$CFG_JSON" > "$CFG"
cd "$ROOT"
set +e
env -u CLAUDECODE timeout --kill-after=10 "$LIMIT" "${CMD[@]}" > "$OUT"
RC=$?
set -e
if [ "$RC" -eq 124 ] || [ "$RC" -eq 137 ]; then
  echo "playtest TIMED OUT after ${LIMIT}s (model=$MODEL); partial trace at $TRACE" >&2
elif [ "$RC" -ne 0 ]; then
  echo "claude exited non-zero ($RC, see $OUT)" >&2
fi

echo "--- agent result (tail) ---"
python3 - "$OUT" <<'PY' || echo "(could not read result)"
import json,sys
try:
    r=json.load(open(sys.argv[1]))
    if isinstance(r,list): r=next((x for x in reversed(r) if isinstance(x,dict) and "result" in x),{})
    print("\n".join(str(r.get("result","(no result field)")).strip().splitlines()[-8:]))
except Exception as e:
    print("(unreadable output: %s)"%e)
PY
echo "--- score ---"
"$HERE/score.py" "$TRACE" || echo "(no trace written: $TRACE)"
