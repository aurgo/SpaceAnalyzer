#!/usr/bin/env bash
# Runs SpaceAnalyzer on a virtual X server (Xvfb): checks that it starts and survives mouse and keyboard
# input, and saves a screenshot after each step. Needs: xvfb, imagemagick (import), xdotool.
#
#   ./build/smoke-linux.sh publish/SpaceAnalyzer /usr/share
set -euo pipefail

EXE="${1:-publish/SpaceAnalyzer}"
FOLDER="${2:-/usr/share}"
OUT="${3:-screenshots}"
mkdir -p "$OUT"

Xvfb :99 -screen 0 1600x1000x24 > /dev/null 2>&1 &
XVFB=$!
export DISPLAY=:99
sleep 2

# The app keeps running after an unexpected error: it also writes it here.
export SPACEANALYZER_ERROR_LOG="$PWD/$OUT/linux-errors.log"
rm -f "$SPACEANALYZER_ERROR_LOG"

"$EXE" "$FOLDER" > "$OUT/linux-app.log" 2>&1 &
APP=$!

alive() {
  if ! kill -0 "$APP" 2> /dev/null; then
    echo "SpaceAnalyzer exited $1"
    cat "$OUT/linux-app.log"
    kill "$XVFB" || true
    exit 1
  fi
  if [ -s "$SPACEANALYZER_ERROR_LOG" ]; then
    echo "SpaceAnalyzer reported an error $1:"
    cat "$SPACEANALYZER_ERROR_LOG"
    import -window root "$OUT/linux-error.png" || true
    kill "$APP" "$XVFB" || true
    exit 1
  fi
}
shot() { import -window root "$OUT/$1"; }

sleep 1.5
alive "while starting"
shot linux-0-scanning.png

# The title becomes "<folder> — SpaceAnalyzer" once the scan is done.
for _ in $(seq 1 360); do
  alive "while scanning"
  if xdotool search --name "— SpaceAnalyzer" > /dev/null 2>&1; then break; fi
  sleep 0.5
done
xdotool search --name "— SpaceAnalyzer" > /dev/null || { shot linux-timeout.png; echo "The scan did not finish in time"; exit 1; }
sleep 0.8
shot linux-1-treemap.png

xdotool mousemove 420 480   # no window manager: the window sits at (0,0)
sleep 1.5
shot linux-2-hover.png
alive "after moving the mouse"

xdotool click --repeat 2 --delay 80 1
sleep 1.2
shot linux-3-zoomed.png
alive "after a double-click"

xdotool type --delay 80 "dll"
sleep 1.2
shot linux-4-search.png
alive "after typing"

xdotool key Escape Escape
xdotool click 3
sleep 0.8
shot linux-5-menu.png
xdotool key Escape
alive "after the context menu"

xdotool key BackSpace
sleep 1.2
shot linux-6-back.png
alive "after using the keyboard"

kill "$APP"
kill "$XVFB" || true
echo "SpaceAnalyzer ran fine on Linux (X11)."
