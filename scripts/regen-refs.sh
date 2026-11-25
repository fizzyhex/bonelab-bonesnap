#!/usr/bin/env bash
# Regenerates ReferenceStubs/ from the BONELAB install, for CI builds.
# Run this from game/mod updates, then commit ReferenceStubs/.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$repo/ReferenceStubs"
cd "$repo"

dotnet tool restore > /dev/null

# Use Directory.Build.props to resolve BONELAB_DIR with the Steam registry
# fallback.
json="$(dotnet msbuild BoneSnap.csproj -nologo -t:ResolveAssemblyReferences -p:DeployToGame=false \
  -getProperty:BONELAB_DIR -getItem:ReferencePath)"

# Normalize JSON-escaped backslashes along with ordinary path separators.
normalize() { sed -E 's#[\\/]+#/#g'; }

game="$(printf '%s' "$json" | grep -o '"BONELAB_DIR": "[^"]*"' | sed 's/.*: "//;s/"$//' | normalize)"
[ -n "$game" ] || { echo "couldn't resolve BONELAB_DIR" >&2; exit 1; }

case "$(cygpath -u "$game" 2>/dev/null || echo "$game")" in
  "$repo"/*) echo "BONELAB_DIR points inside this repo ($game) - set it to your real install" >&2; exit 1 ;;
esac

mapfile -t dlls < <(printf '%s' "$json" | grep -o '"FullPath": "[^"]*"' | sed 's/.*: "//;s/"$//' \
  | normalize | grep -F "$game/" | sort -u)
[ "${#dlls[@]}" -gt 0 ] || { echo "no references resolved from $game" >&2; exit 1; }

echo "stripping ${#dlls[@]} assemblies from $game"
rm -rf "$out"
for dll in "${dlls[@]}"; do
  rel="${dll:${#game}+1}"
  mkdir -p "$out/$(dirname "$rel")"
  dotnet refasmer -q --omit-non-api-members=true -o "$out/$rel" "$dll"
  echo "  $rel"
done

echo "done -> ReferenceStubs/ ($(du -sh "$out" | cut -f1)). ready to commit."
