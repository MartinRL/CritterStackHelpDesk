#!/usr/bin/env bash
# CodeScene CodeHealth gate, lifted from kvissig.se. On failure block the stop (exit 2) and
# feed the sub-threshold files back to the agent so it self-corrects.
# Fix the CODE (reduce complexity, split methods, name things) — NEVER lower the threshold.
#
# Modes:
#   --changed   Stop-hook / local: only scope files touched vs HEAD (+ staged). Fast gate.
#   --all       CI / baseline: the whole prod-C# set.
#   --report    Read-only baseline: per-file CH, min/mean, files < THRESHOLD. No exit 2.
#
# Requires: cs (CodeScene devtools CLI) on PATH + CS_ACCESS_TOKEN set. jq for parsing.
# CH schema (cs 1.0.33): `cs review <file> --output-format json` -> { "score": <0-10>, "review": [...] }.
# One file per invocation; score null = "no scorable code" (e.g. record-only files) -> skipped.
set -uo pipefail

# Claude's Bash tool and the Stop hook run non-interactive bash that does NOT source ~/.bashrc,
# so cs would get no token and the gate would silently pass. Pull the token in if it's missing.
# CI injects CS_ACCESS_TOKEN as env, so this is a no-op there.
[ -z "${CS_ACCESS_TOKEN:-}" ] && source ~/.bashrc 2>/dev/null

THRESHOLD=9.4

# Documented per-file exemptions (path regex). NEVER lower the global THRESHOLD; add a line
# here with the CH + reason instead. None so far.
EXEMPT_RE='^$'

mode="${1:---changed}"

# stop_hook_active guard (only relevant when invoked as a Stop hook, which passes JSON on stdin).
if [ "$mode" = "--changed" ] && [ ! -t 0 ]; then
  input="$(cat)"
  case "$input" in
    *'"stop_hook_active":true'*) exit 0 ;;
  esac
fi

cd "${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel)}" || exit 0

# Prod C# scope: the API and the notification consumer. Excludes tests, the demo console,
# obj/bin and generated code. Tracked + untracked-not-ignored, so a brand-new file is scored
# BEFORE its first commit.
scope_files() {
  { git ls-files -- '*.cs'; git ls-files --others --exclude-standard -- '*.cs'; } | sort -u \
    | grep -E '^(Helpdesk\.Api|NotificationService)/' \
    | grep -viE '(/obj/|/bin/|\.Tests/|\.g\.cs$)'
}

# Emit "path<TAB>score" for the given files. cs review scores ONE file per call; null score
# (record-only / no scorable code) is skipped.
score_files() {
  for f in "$@"; do
    s="$(cs review "$f" --output-format json 2>/dev/null | jq -r '.score // empty')"
    [ -n "$s" ] && printf '%s\t%s\n' "$f" "$s"
  done
}

# Below-threshold awk filter: prints "path: CH x.xx (< t)" for score < THRESHOLD.
below() { awk -F'\t' -v t="$THRESHOLD" '$2 != "" && $2+0 < t {printf "%s: CH %.2f (< %s)\n", $1, $2, t}'; }

case "$mode" in
  --report)
    scored="$(score_files $(scope_files))"
    echo "$scored" | awk -F'\t' '{printf "  %-50s CH %s\n", $1, $2}'
    echo "$scored" | awk -F'\t' 'NR==1{min=$2} {s+=$2; n++; if ($2+0<min) min=$2} END{if (n) printf "  -- min %.2f  mean %.2f  (%d files)\n", min, s/n, n}'
    echo "== Files < $THRESHOLD =="
    echo "$scored" | below | sed 's/^/  /'
    exit 0
    ;;
  --all)
    files="$(scope_files)"
    ;;
  --changed)
    changed="$( { git diff --name-only HEAD; git diff --name-only --cached; \
                  git ls-files --others --exclude-standard; } | sort -u )"
    files="$(comm -12 <(scope_files | sort) <(echo "$changed" | sort))"
    ;;
  *)
    echo "usage: codehealth.sh [--changed|--all|--report]" >&2; exit 2 ;;
esac

[ -z "$files" ] && exit 0

failures="$(score_files $files | below | grep -vE "$EXEMPT_RE")"
if [ -n "$failures" ]; then
  echo "CodeHealth gate FAILED — raise the CODE to CH >= $THRESHOLD, do NOT lower the threshold:" >&2
  echo "$failures" >&2
  exit 2
fi
exit 0
