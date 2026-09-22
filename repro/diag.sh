#!/usr/bin/env bash
# Runs one self-dump case in the background; if createdump is still alive after 10s, samples it and
# captures the security-related system log, so a hang shows where createdump is blocked.
# Usage: diag.sh <label> <command...>
set -u
label="$1"; shift
out="$RESULTS/$label"
mkdir -p "$out"
echo; echo "===== $label"

start=$(date '+%Y-%m-%d %H:%M:%S')
"$@" > "$out/app.txt" 2>&1 &
app=$!

# Wait up to 10s for the app to finish; otherwise treat createdump as stuck.
for _ in $(seq 1 20); do kill -0 $app 2>/dev/null || break; sleep 0.5; done

if kill -0 $app 2>/dev/null; then
  cd_pid=$(pgrep -n createdump || true)
  echo "app still running after 10s; createdump pid: ${cd_pid:-none}"
  if [ -n "${cd_pid:-}" ]; then
    ps -o pid,ppid,stat,etime,command -p "$cd_pid" || true
    sudo sample "$cd_pid" 5 -file "$out/createdump.sample.txt" >/dev/null 2>&1 || echo "(sample failed)"
  fi
  sudo sample "$app" 3 -file "$out/app.sample.txt" >/dev/null 2>&1 || true
fi

wait $app; echo "(app exit code $?)"
grep -vE '^\[createdump\] [0-9a-f]{8} [0-9a-f]{16}' "$out/app.txt" | tail -20

sudo log show --style compact --start "$start" \
  --predicate 'process == "createdump" OR process == "taskgated" OR process == "taskgated-helper" OR process == "amfid" OR process == "authd" OR process == "syspolicyd" OR eventMessage CONTAINS[c] "task_for_pid" OR eventMessage CONTAINS[c] "createdump"' \
  > "$out/security.log" 2>&1 || true
echo "--- security log lines: $(wc -l < "$out/security.log")"; tail -25 "$out/security.log"

if [ -f "$out/createdump.sample.txt" ]; then
  echo "--- createdump sample (main thread call graph, top)"
  sed -n '/Call graph:/,/Total number in stack/p' "$out/createdump.sample.txt" | head -45
fi

sudo pkill -9 createdump 2>/dev/null && echo "(killed leftover createdump)"
true
