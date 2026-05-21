#!/bin/bash
# Runs `dotnet run` in a loop. Restarts the app when /workspace/.reload changes.
# This file is bind-mounted from the host, so `touch .reload` from the host
# triggers an in-container restart.

set -u

SENTINEL="/workspace/.reload"
PROJECT="src/Noto.Server"

touch "$SENTINEL" 2>/dev/null || true
LAST_MTIME=$(stat -c %Y "$SENTINEL" 2>/dev/null || echo 0)

cleanup() {
    if [ -n "${APP_PID:-}" ] && kill -0 "$APP_PID" 2>/dev/null; then
        kill "$APP_PID" 2>/dev/null
        wait "$APP_PID" 2>/dev/null || true
    fi
}
trap cleanup EXIT INT TERM

cd /workspace

while true; do
    echo ""
    echo "════════════════════════════════════════"
    echo "  noto · starting at $(date '+%H:%M:%S')"
    echo "════════════════════════════════════════"

    dotnet run --project "$PROJECT" --no-launch-profile &
    APP_PID=$!

    # Poll the sentinel file while the app is running
    while kill -0 "$APP_PID" 2>/dev/null; do
        sleep 2
        CUR_MTIME=$(stat -c %Y "$SENTINEL" 2>/dev/null || echo 0)
        if [ "$CUR_MTIME" != "$LAST_MTIME" ]; then
            echo ""
            echo "[noto] reload triggered — stopping app..."
            kill "$APP_PID" 2>/dev/null || true
            wait "$APP_PID" 2>/dev/null || true
            LAST_MTIME="$CUR_MTIME"
            break
        fi
    done

    # If we got here because the app exited on its own, wait a bit then restart
    if ! kill -0 "$APP_PID" 2>/dev/null; then
        echo "[noto] app exited — restarting in 2s (Ctrl+C the container to stop entirely)"
        sleep 2
    fi
done
