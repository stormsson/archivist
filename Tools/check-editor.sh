#!/usr/bin/env bash
# Type-checks the two assemblies run-acceptance.sh cannot see.
#
# §11: "Tools/GenHarness compiles only Generation and Render. The Editor and Tests
# assemblies are NOT covered, so a mistake there builds clean headlessly and fails only
# inside Unity." That is ~6600 lines of Editor code plus the EditMode tests with no
# check at all short of opening the editor and waiting for a domain reload.
#
# This compiles Generation + Render + Editor + Tests together against the real Unity
# managed assemblies, in a throwaway project under the system temp dir. It is a
# COMPILE check, not a test run: it catches syntax errors, type errors, bad usings and
# signature drift. It does not execute anything, so it says nothing about behaviour --
# run-acceptance.sh remains the behavioural gate.
#
# Usage:   Tools/check-editor.sh
# Override the editor location if the Hub put it somewhere unusual:
#          UNITY_MANAGED=/path/to/Unity.app/Contents/Managed Tools/check-editor.sh
set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET=/usr/local/share/dotnet/x64/dotnet
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# --- locate the editor -------------------------------------------------------------
# The Hub's default is /Applications/Unity/Hub/Editor, but it honours a configurable
# secondary install path and a lot of installs live there instead, so read that file
# rather than assuming. ProjectVersion.txt names the version this project expects.
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt" 2>/dev/null)"
HUB_SECONDARY="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"

if [ -z "${UNITY_MANAGED:-}" ]; then
  CANDIDATES=()
  if [ -f "$HUB_SECONDARY" ]; then
    # The file is a bare JSON string: "/Users/me/Unity Editors"
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
  echo "  SKIP  Editor/Tests type check - Unity $VERSION not found."
  echo "        Looked in the Hub secondary install path and /Applications/Unity."
  echo "        Set UNITY_MANAGED=/path/to/Unity.app/Contents/Managed to override."
  exit 0   # not a failure: the machine simply has no editor installed
fi

NUNIT="$ROOT/Library/PackageCache/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll"
if [ ! -f "$NUNIT" ]; then
  echo "  SKIP  Editor/Tests type check - nunit.framework.dll not in Library/PackageCache."
  echo "        Open the project in Unity once to populate it."
  exit 0
fi

# --- throwaway project -------------------------------------------------------------
# Kept out of the repo: it is a check, not a build artifact, and a stray csproj beside
# the sources would confuse both Unity and the IDE.
WORK="${TMPDIR:-/tmp}/archivist-check-editor"
mkdir -p "$WORK" || exit 1

cat > "$WORK/EditorCheck.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>            <!-- Unity 6000.0 ships C# 9 -->
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>EditorCheck</AssemblyName>
    <DefineConstants>UNITY_EDITOR;UNITY_2020_1_OR_NEWER;UNITY_INCLUDE_TESTS</DefineConstants>
    <NoWarn>CS0649;CS0169;CS0436</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$ROOT/Assets/Archivist/Generation/**/*.cs" />
    <Compile Include="$ROOT/Assets/Archivist/Render/**/*.cs" />
    <Compile Include="$ROOT/Assets/Archivist/Editor/**/*.cs" />
    <Compile Include="$ROOT/Assets/Archivist/Tests/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="UnityEngine"><HintPath>$UNITY_MANAGED/UnityEngine.dll</HintPath></Reference>
    <Reference Include="UnityEditor"><HintPath>$UNITY_MANAGED/UnityEditor.dll</HintPath></Reference>
    <Reference Include="nunit.framework"><HintPath>$NUNIT</HintPath></Reference>
  </ItemGroup>
</Project>
EOF

echo "Editor + Tests type check  (Unity $VERSION)"
out="$("$DOTNET" build "$WORK/EditorCheck.csproj" -v q --nologo 2>&1)"
status=$?

if [ "$status" != "0" ]; then
  echo "  FAIL  Editor/Tests do not compile"
  echo "$out" | grep -E "error CS" | sort -u | sed 's|^|          |'
  exit 1
fi

echo "  PASS  Generation + Render + Editor + Tests compile"
exit 0
