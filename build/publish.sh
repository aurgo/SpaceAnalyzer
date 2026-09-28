#!/usr/bin/env bash
# Publishes the single-file executable for every platform into artifacts/<rid>/.
# Framework-dependent: each file is a few hundred KB and needs the .NET 10 runtime.
#
#   ./build/publish.sh                     # all platforms
#   ./build/publish.sh win-x64 linux-x64   # only these
set -euo pipefail
cd "$(dirname "$0")/.."

RIDS=("$@")
if [ ${#RIDS[@]} -eq 0 ]; then
  RIDS=(win-x64 win-arm64 osx-arm64 osx-x64 linux-x64 linux-arm64)
fi

for rid in "${RIDS[@]}"; do
  dotnet publish src/SpaceAnalyzer -c Release -r "$rid" -o "artifacts/$rid" --nologo -v quiet
  for f in artifacts/"$rid"/SpaceAnalyzer artifacts/"$rid"/SpaceAnalyzer.exe; do
    [ -f "$f" ] && printf '%-12s %8s KB  %s\n' "$rid" "$(( $(wc -c < "$f") / 1024 ))" "$f"
  done
done
