#!/bin/bash
# Stardew Valley Dedicated Server - Linux Launcher
# Starts Xvfb virtual display and launches SMAPI
#
# Usage: ./start.sh [game_dir]
# If game_dir is not specified, uses the script's parent directory

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
GAME_DIR="${1:-$(dirname "$SCRIPT_DIR")}"

echo "[Launcher] Stardew Valley Dedicated Server"
echo "[Launcher] Game directory: $GAME_DIR"

# Check if SMAPI exists
if [ ! -f "$GAME_DIR/StardewModdingAPI" ]; then
    echo "[Launcher] ERROR: StardewModdingAPI not found in $GAME_DIR"
    echo "[Launcher] Please install SMAPI first (run install.sh or update via AMP)"
    exit 1
fi

# Check for Xvfb
if ! command -v Xvfb &>/dev/null; then
    echo "[Launcher] ERROR: Xvfb not found. Install it with: sudo apt-get install xvfb"
    exit 1
fi

# Find a free display number
DISPLAY_NUM=99
while [ -e "/tmp/.X${DISPLAY_NUM}-lock" ]; do
    DISPLAY_NUM=$((DISPLAY_NUM + 1))
done

echo "[Launcher] Starting virtual display :${DISPLAY_NUM}"

# Start Xvfb in the background
Xvfb :${DISPLAY_NUM} -screen 0 1024x768x24 -ac +extension GLX +render -noreset &
XVFB_PID=$!

# Wait for Xvfb to be ready
sleep 1

# Verify Xvfb started
if ! kill -0 $XVFB_PID 2>/dev/null; then
    echo "[Launcher] ERROR: Xvfb failed to start"
    exit 1
fi

export DISPLAY=:${DISPLAY_NUM}

echo "[Launcher] Virtual display ready on :${DISPLAY_NUM}"
echo "[Launcher] Launching StardewModdingAPI..."

# Cleanup function
cleanup() {
    echo "[Launcher] Shutting down..."
    if [ -n "$XVFB_PID" ] && kill -0 $XVFB_PID 2>/dev/null; then
        kill $XVFB_PID 2>/dev/null || true
        echo "[Launcher] Xvfb stopped"
    fi
}

trap cleanup EXIT INT TERM

# Launch SMAPI
cd "$GAME_DIR"
chmod +x StardewModdingAPI 2>/dev/null || true
exec ./StardewModdingAPI
