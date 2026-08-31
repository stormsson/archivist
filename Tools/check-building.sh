#!/usr/bin/env bash
# Type-checks Archivist.Building, which nothing else can see.
#
# run-acceptance.sh compiles Generation + Render; check-editor.sh adds Editor + Tests.
# Archivist.Building (runtime and its Editor asmdef) is covered by NEITHER, so a
# mistake there builds clean headlessly and fails only after a domain reload inside
# Unity. That is the whole room, the player, the paper and the table -- the half of
# the project most likely to be edited.
#
# Same shape as check-editor.sh: a throwaway project under the system temp dir,
# compiled against the real Unity managed assemblies. It is a COMPILE check, not a
# test run -- it catches syntax errors, type errors, bad usings and signature drift,
# and says nothing about behaviour.
#
# Two things this needs that check-editor.sh does not:
#   * every module in Managed/UnityEngine/, not just UnityEngine.dll. Without them
#     any file touching UGUI fails with "CoreModule missing".
#   * Library/ScriptAssemblies/{Unity.InputSystem,Unity.InputSystem.ForUI,
#     UnityEngine.UI}.dll -- the packages Archivist.Building.asmdef references.
#     Those are built by Unity, so the project must have been opened once.
#
# Usage:   Tools/check-building.sh
#          UNITY_MANAGED=/path/to/Unity.app/Contents/Managed Tools/check-building.sh
set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET=/usr/local/share/dotnet/x64/dotnet
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# --- locate the editor (same search as check-editor.sh) ----------------------------
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt" 2>/dev/null)"
HUB_SECONDARY="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"

if [ -z "${UNITY_MANAGED:-}" ]; then
  CANDIDATES=()
  if [ -f "$HUB_SECONDARY" ]; then
    SECONDARY="$(tr -d '"' < "$HUB_SECONDARY")"
    [ -n "$SECONDARY" ] && CANDIDATES+=("$SECONDARY/$VERSION/Unity.app/Contents/Managed")
  fi
  CANDIDATES+=("/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/Managed")
  CANDIDATES+=("/Applications/Unity/Unity.app/Contents/Managed")

  for c in "${CANDIDATES[@]}"; do
    if [ -f "$c/UnityEngine.dll" ] && [ -f "$c/UnityEditor.dll" ]; then UNITY_MANAGED="$c"; break; fi
  done
fi

if [ -z "${UNITY_MANAGED:-}" ] || [ ! -f "$UNITY_MANAGED/UnityEngine.dll" ]; then
  echo "  SKIP  Building type check - Unity $VERSION not found."
  echo "        Set UNITY_MANAGED=/path/to/Unity.app/Contents/Managed to override."
  exit 0
fi

SA="$ROOT/Library/ScriptAssemblies"
for need in Unity.InputSystem.dll Unity.InputSystem.ForUI.dll UnityEngine.UI.dll; do
  if [ ! -f "$SA/$need" ]; then
    echo "  SKIP  Building type check - $need not in Library/ScriptAssemblies."
    echo "        Open the project in Unity once to build the package assemblies."
    exit 0
  fi
done

# --- throwaway project -------------------------------------------------------------
WORK="${TMPDIR:-/tmp}/archivist-check-building"
mkdir -p "$WORK" || exit 1

# Every module beside UnityEngine.dll. Managed/UnityEngine/ holds both the
# UnityEngine.*Module and UnityEditor.*Module assemblies; reference the lot rather
# than guessing which file needs which.
MODULES=""
for dll in "$UNITY_MANAGED/UnityEngine/"*.dll; do
  [ -f "$dll" ] || continue
  name="$(basename "$dll" .dll)"
  MODULES="$MODULES
    <Reference Include=\"$name\"><HintPath>$dll</HintPath></Reference>"
done

cat > "$WORK/BuildingCheck.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>BuildingCheck</AssemblyName>
    <DefineConstants>UNITY_EDITOR;UNITY_2020_1_OR_NEWER</DefineConstants>
    <NoWarn>CS0649;CS0169;CS0436;CS8032</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$ROOT/Assets/Archivist/Generation/**/*.cs" />
    <Compile Include="$ROOT/Assets/Archivist/Render/**/*.cs" />
    <Compile Include="$ROOT/Assets/Archivist/Building/Runtime/**/*.cs" />
    <Compile Include="$ROOT/Assets/Archivist/Building/Editor/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="UnityEngine"><HintPath>$UNITY_MANAGED/UnityEngine.dll</HintPath></Reference>
    <Reference Include="UnityEditor"><HintPath>$UNITY_MANAGED/UnityEditor.dll</HintPath></Reference>
    <Reference Include="Unity.InputSystem"><HintPath>$SA/Unity.InputSystem.dll</HintPath></Reference>
    <Reference Include="Unity.InputSystem.ForUI"><HintPath>$SA/Unity.InputSystem.ForUI.dll</HintPath></Reference>
    <Reference Include="UnityEngine.UI"><HintPath>$SA/UnityEngine.UI.dll</HintPath></Reference>$MODULES
  </ItemGroup>
</Project>
EOF

echo "Building type check  (Unity $VERSION)"
out="$("$DOTNET" build "$WORK/BuildingCheck.csproj" -v q --nologo 2>&1)"
status=$?

if [ "$status" != "0" ]; then
  echo "  FAIL  Archivist.Building does not compile"
  echo "$out" | grep -E "error CS" | sort -u | sed 's|^|          |'
  exit 1
fi

echo "  PASS  Generation + Render + Building (runtime + editor) compile"
exit 0
