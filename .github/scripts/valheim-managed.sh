#!/usr/bin/env bash
# Valheim's game DLLs for CI: only valheim_server_Data/Managed/*.dll of the free dedicated server (Steam app 896660,
# anonymous login), fetched with DepotDownloader (64-bit; pinned and checksum-verified). Nothing from the game is committed.
# Prints which server build it got (depot manifests, assembly_valheim.dll SHA-256), also to the run summary on CI.
#   valheim-managed.sh <dir>    put the DLLs into <dir>
set -euo pipefail

VERSION=3.4.0
SHA256=a999dec66b4850fc961bd50366696d23c2d0fad7b18790e6a5647b2f19097a53  # DepotDownloader-linux-x64.zip
APP=896660

dest="${1:?usage: valheim-managed.sh <dir>}"
work="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/depotdownloader"
mkdir -p "$work"

curl -sSL -o "$work/dd.zip" \
  "https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_${VERSION}/DepotDownloader-linux-x64.zip"
echo "$SHA256  $work/dd.zip" | sha256sum -c --quiet -
unzip -o -q "$work/dd.zip" -d "$work"

printf 'regex:^valheim_server_Data/Managed/.*\\.dll$\n' > "$work/files.txt"
"$work/DepotDownloader" -app "$APP" -os linux -filelist "$work/files.txt" -dir "$work/server" | tee "$work/download.log"

mkdir -p "$dest"
cp "$work/server/valheim_server_Data/Managed/"*.dll "$dest/"
test -f "$dest/assembly_valheim.dll"

{
  echo "### Valheim dedicated server (app $APP)"
  awk '/^Processing depot / { depot = $3 } /^Manifest [0-9]+ / { sub(/^Manifest /, ""); print "- depot " depot ": manifest " $0 }' "$work/download.log"
  echo "- assembly_valheim.dll SHA-256: \`$(sha256sum "$dest/assembly_valheim.dll" | cut -d' ' -f1)\`"
} | tee -a "${GITHUB_STEP_SUMMARY:-/dev/null}"
